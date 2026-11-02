using System.Security.Cryptography;
using System.Globalization;
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
    RenameColumn,
    FilterEquals,
    FilterContains,
    FilterAmountAbove,
    AddColumn,
    CopyColumn,
    SortAscending,
    ReplaceText
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
            if (columnRequired && step.Operation != TransformOperation.AddColumn)
            {
                if (string.IsNullOrWhiteSpace(col) || !headers.Contains(col, StringComparer.Ordinal))
                    throw new EtlValidationException($"Step {index + 1}: select an existing, exact column name.");
            }
            if (step.Operation is TransformOperation.FillMissing or TransformOperation.Replace or TransformOperation.RenameColumn or TransformOperation.AddColumn or TransformOperation.CopyColumn or TransformOperation.ReplaceText)
            {
                if (step.To is null || step.To.Length > 200 || step.To.Length == 0)
                    throw new EtlValidationException($"Step {index + 1}: a non-empty replacement or target is required (max 200 chars).");
            }
            if ((step.Operation is TransformOperation.Replace or TransformOperation.ReplaceText or TransformOperation.FilterEquals or TransformOperation.FilterContains or TransformOperation.FilterAmountAbove) &&
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
                case TransformOperation.FilterEquals:
                    affected = rows.RemoveAll(r => !string.Equals(
                        r.Values[col!].Trim(), step.From!.Trim(), StringComparison.OrdinalIgnoreCase));
                    break;
                case TransformOperation.FilterContains:
                    affected = rows.RemoveAll(r => !r.Values[col!].Contains(
                        step.From!, StringComparison.OrdinalIgnoreCase));
                    break;
                case TransformOperation.FilterAmountAbove:
                    if (!decimal.TryParse(step.From, NumberStyles.AllowLeadingSign |
                            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal threshold))
                        throw new EtlValidationException($"Step {index + 1}: threshold must be a decimal number using a dot.");
                    affected = rows.RemoveAll(r =>
                        !decimal.TryParse(r.Values[col!], NumberStyles.AllowLeadingSign |
                            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite |
                            NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out decimal val)
                        || val <= threshold);
                    break;
                case TransformOperation.AddColumn:
                    string newName = (col ?? "").Trim();
                    if (newName.Length is < 1 or > 100 ||
                        headers.Contains(newName, StringComparer.OrdinalIgnoreCase))
                        throw new EtlValidationException($"Step {index + 1}: new column name must be unique and 1–100 characters.");
                    headers.Add(newName);
                    foreach (var row in rows) row.Values.Add(newName, step.To!);
                    affected = rows.Count;
                    break;
                case TransformOperation.CopyColumn:
                    string copyTarget = step.To!.Trim();
                    if (copyTarget.Length is < 1 or > 100 ||
                        headers.Contains(copyTarget, StringComparer.OrdinalIgnoreCase))
                        throw new EtlValidationException($"Step {index + 1}: copy target must be a unique column name.");
                    headers.Add(copyTarget);
                    foreach (var row in rows) row.Values.Add(copyTarget, row.Values[col!]);
                    affected = rows.Count;
                    break;
                case TransformOperation.SortAscending:
                    rows = rows.OrderBy(r => r.Values[col!], StringComparer.OrdinalIgnoreCase)
                        .ThenBy(r => r.OriginalCsvRow).ToList();
                    // Count moved rows by tracking original index, not by claiming rows were edited.
                    affected = rows.Where((row, position) => row.OriginalCsvRow !=
                        (position < rowsBefore ? position + 2 : -1)).Count();
                    break;
                case TransformOperation.ReplaceText:
                    foreach (var row in rows)
                    {
                        string oldValue = row.Values[col!];
                        string newValue = oldValue.Replace(step.From!, step.To!, StringComparison.Ordinal);
                        if (oldValue != newValue) { row.Values[col!] = newValue; affected++; }
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
