using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QueryForge.Api;

// Explicit, allowlisted transformations only. No user-supplied code or SQL.
[JsonConverter(typeof(JsonStringEnumConverter<TransformOperation>))]
public enum TransformOperation
{
    Trim,
    Uppercase,
    Lowercase,
    Replace,
    FillMissing,
    DropMissing,
    DropDuplicates,
    RenameColumn
}

public sealed record TransformStep(
    TransformOperation Operation,
    string? Column = null,
    string? From = null,
    string? To = null);

public sealed record EtlRequest(
    string? PipelineName,
    IReadOnlyList<TransformStep>? Steps,
    string? PreviewFingerprint = null);

public sealed record EtlStepAudit(
    int Step,
    TransformOperation Operation,
    string? Column,
    int RowsBefore,
    int RowsAfter,
    int AffectedRows);

public sealed record TransformationLineage(
    Guid ParentDatasetId,
    string ParentSha256,
    string DefinitionSha256,
    string PipelineName,
    int SchemaVersion,
    DateTimeOffset AppliedUtc,
    int RowsBefore,
    int RowsAfter,
    IReadOnlyList<TransformStep> Steps,
    IReadOnlyList<EtlStepAudit> Audit,
    IReadOnlyList<int> OriginalRowNumbers);

public sealed record EtlExecution(
    IReadOnlyList<string> Headers,
    IReadOnlyList<Dictionary<string, string>> Rows,
    IReadOnlyList<int> OriginalRowNumbers,
    IReadOnlyList<EtlStepAudit> Audit,
    string DefinitionSha256,
    string DataSha256,
    string PreviewFingerprint);

public sealed class EtlValidationException(string message) : Exception(message);

// Pure transformation engine: no database, HTTP, filesystem, AI, or dynamic code.
public sealed class EtlPipeline
{
    public const int MaximumSteps = 12;
    private sealed record TrackedRow(int OriginalCsvRow, Dictionary<string, string> Values);

    public EtlExecution Execute(DatasetEntity source, EtlRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        var steps = request.Steps ?? [];
        if (steps.Count is < 1 or > MaximumSteps)
            throw new EtlValidationException("Choose between 1 and 12 transformations.");
        var name = (request.PipelineName ?? "Untitled pipeline").Trim();
        if (name.Length is < 1 or > 80)
            throw new EtlValidationException("Pipeline name must contain 1–80 characters.");

        var headers = source.Headers.ToList();
        var rows = source.Rows.Select((row, index) =>
            new TrackedRow(index + 2,
                new Dictionary<string, string>(row, StringComparer.Ordinal))).ToList();

        var definitionJson = JsonSerializer.Serialize(new { schemaVersion = 1, name, steps });
        var definitionSha = Hash(definitionJson);
        var fingerprint = Hash($"{source.Id:N}:{source.Sha256}:{definitionSha}");

        var audit = new List<EtlStepAudit>();
        for (int index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            // Reject undefined enum values as well as unsupported input.
            if (!Enum.IsDefined(step.Operation))
                throw new EtlValidationException($"Step {index + 1} has an invalid operation.");

            string? col = step.Column;
            bool columnRequired = step.Operation is not TransformOperation.Trim || !string.IsNullOrWhiteSpace(col);
            if (columnRequired)
            {
                if (string.IsNullOrWhiteSpace(col) || !headers.Contains(col, StringComparer.Ordinal))
                    throw new EtlValidationException($"Step {index + 1}: select an existing, exact column name.");
            }
            if (step.Operation is TransformOperation.FillMissing or TransformOperation.Replace or TransformOperation.RenameColumn)
            {
                if (step.To is null || step.To.Length > 200 || step.To.Length == 0)
                    throw new EtlValidationException($"Step {index + 1}: a non-empty replacement or target is required (max 200 chars).");
            }
            if (step.Operation == TransformOperation.Replace &&
                (string.IsNullOrEmpty(step.From) || step.From.Length > 200))
                throw new EtlValidationException($"Step {index + 1}: provide an original value (max 200 chars).");

            int rowsBefore = rows.Count;
            int affected = 0;
            switch (step.Operation)
            {
                case TransformOperation.Trim:
                    foreach (var row in rows)
                    {
                        IEnumerable<string> targets = col is null or "" ? headers : [col];
                        bool changed = false;
                        foreach (var key in targets)
                        {
                            var prior = row.Values[key];
                            var updated = prior.Trim();
                            if (updated != prior) { row.Values[key] = updated; changed = true; }
                        }
                        if (changed) affected++;
                    }
                    break;
                case TransformOperation.Uppercase:
                case TransformOperation.Lowercase:
                    foreach (var row in rows)
                    {
                        string before = row.Values[col!];
                        string after = step.Operation == TransformOperation.Uppercase
                            ? before.ToUpperInvariant() : before.ToLowerInvariant();
                        if (before != after) { row.Values[col!] = after; affected++; }
                    }
                    break;
                case TransformOperation.Replace:
                    foreach (var row in rows)
                    {
                        if (string.Equals(row.Values[col!], step.From, StringComparison.Ordinal))
                        {
                            row.Values[col!] = step.To!;
                            affected++;
                        }
                    }
                    break;
                case TransformOperation.FillMissing:
                    foreach (var row in rows)
                    {
                        if (string.IsNullOrWhiteSpace(row.Values[col!]))
                        {
                            row.Values[col!] = step.To!;
                            affected++;
                        }
                    }
                    break;
                case TransformOperation.DropMissing:
                    affected = rows.RemoveAll(r => string.IsNullOrWhiteSpace(r.Values[col!]));
                    break;
                case TransformOperation.DropDuplicates:
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    affected = rows.RemoveAll(r =>
                    {
                        var value = r.Values[col!].Trim();
                        // Empty keys are not automatically deduplicated. Quality checks flag them.
                        return value.Length > 0 && !seen.Add(value);
                    });
                    break;
                case TransformOperation.RenameColumn:
                    var target = step.To!.Trim();
                    if (target.Length == 0 || target.Length > 100 ||
                        (headers.Any(h => h.Equals(target, StringComparison.OrdinalIgnoreCase) && h != col)))
                        throw new EtlValidationException($"Step {index + 1}: target header is empty, too long, or already exists.");
                    int headerIndex = headers.IndexOf(col!);
                    if (target != col)
                    {
                        headers[headerIndex] = target;
                        foreach (var row in rows)
                        {
                            var value = row.Values[col!];
                            row.Values.Remove(col!);
                            row.Values[target] = value;
                        }
                        affected = rows.Count;
                    }
                    break;
                default:
                    throw new EtlValidationException($"Step {index + 1}: unsupported operation.");
            }
            audit.Add(new(index + 1, step.Operation, col, rowsBefore, rows.Count, affected));
        }

        // Canonical JSON data hash: computed independently of when/where it is persisted.
        var outputRows = rows.Select(r =>
        {
            var ordered = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in headers) ordered[key] = r.Values[key];
            return ordered;
        }).ToList();
        var dataHash = Hash(JsonSerializer.Serialize(new { headers, rows = outputRows }));

        return new(headers, outputRows, rows.Select(r => r.OriginalCsvRow).ToList(),
            audit, definitionSha, dataHash, fingerprint);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
