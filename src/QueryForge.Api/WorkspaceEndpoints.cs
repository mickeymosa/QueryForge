using System.Globalization;
using System.Text.Json;

namespace QueryForge.Api;

public sealed record WorkspaceAnalysis(string Aggregate, string? Measure = null,
    string? GroupBy = null, int Top = 10, bool ExcludeFlagged = false);
public sealed record WorkspaceGroup(string Label, string Value, int Records);
public sealed record WorkspaceResult(string Aggregate, string? Measure, string? GroupBy,
    string? Total, string Unit, int SourceRows, int UsedRows, int FlaggedExcluded,
    int InvalidMeasureRows, int GroupCount, IReadOnlyList<WorkspaceGroup> Groups,
    string Chart, Guid DatasetId, string DatasetSha256);
public sealed record CleaningSuggestion(string Description, int AffectedRows, TransformStep Step);
public sealed class WorkspaceValidationException(string message) : Exception(message);

public static class WorkspaceService
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private const NumberStyles NumericStyle = NumberStyles.AllowLeadingSign |
        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    public static IReadOnlyList<CleaningSuggestion> Recommend(DatasetEntity source)
    {
        var rows = source.Rows;
        var headers = source.Headers;
        var suggestions = new List<CleaningSuggestion>();
        int trim = rows.Count(r => headers.Any(h => r.GetValueOrDefault(h, "") != r.GetValueOrDefault(h, "").Trim()));
        if (trim > 0) suggestions.Add(new("Trim surrounding whitespace in all fields", trim, new(TransformOperation.Trim)));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int duplicates = rows.Count(r => !seen.Add(JsonSerializer.Serialize(headers.Select(h => r.GetValueOrDefault(h, "")).ToArray())));
        if (duplicates > 0) suggestions.Add(new("Remove exact repeated rows (confirm they are accidental)", duplicates, new(TransformOperation.DropExactDuplicates)));
        return suggestions;
    }

    private sealed class Bucket
    {
        public int Count;
        public decimal Sum;
        public void Add(decimal n) { Count++; Sum = checked(Sum + n); }
        public decimal Value(string aggregate) => aggregate switch
        {
            "count" => Count, "sum" => Sum, "average" => Sum / Count,
            _ => throw new WorkspaceValidationException("Unsupported calculation.")
        };
    }
    public static WorkspaceResult Analyze(DatasetEntity source, WorkspaceAnalysis plan)
    {
        if (plan.Aggregate is not ("count" or "sum" or "average"))
            throw new WorkspaceValidationException("Use count, sum or average.");
        if (plan.Top is < 1 or > 20) throw new WorkspaceValidationException("Top must be between 1 and 20.");
        var headers = source.Headers;
        bool numeric = plan.Aggregate != "count";
        if (numeric && (plan.Measure is null || !headers.Contains(plan.Measure, StringComparer.Ordinal)))
            throw new WorkspaceValidationException("Choose an existing numeric measure.");
        if (!numeric && plan.Measure is not null)
            throw new WorkspaceValidationException("Count counts records, not values. Clear the measure.");
        if (plan.GroupBy is not null && !headers.Contains(plan.GroupBy, StringComparer.Ordinal))
            throw new WorkspaceValidationException("Choose an existing group column.");
        var rows = source.Rows;
        var flagged = source.Report.Issues.Select(i => i.RowNumber).ToHashSet();
        var selected = rows.Where((r,i) => !plan.ExcludeFlagged || !flagged.Contains(i+2)).ToList();
        var unit = numeric ? "unspecified units" : "records";
        var currency = headers.FirstOrDefault(h => h.Equals("currency", StringComparison.OrdinalIgnoreCase));
        if (numeric && currency is not null && selected.Count > 0)
        {
            var units = selected.Select(r => r.GetValueOrDefault(currency, "").Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (units.Length != 1 || units[0].Length == 0)
                throw new WorkspaceValidationException("Mixed or missing currency codes: clean or filter the data first. No conversion was performed.");
            unit = units[0].ToUpperInvariant();
        }
        var total = new Bucket();
        var groups = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        int invalid = 0;
        foreach (var row in selected)
        {
            decimal value = 0;
            if (numeric && !decimal.TryParse(row.GetValueOrDefault(plan.Measure!, ""), NumericStyle, Culture, out value))
            { invalid++; continue; }
            total.Add(value);
            if (plan.GroupBy is null) continue;
            var label = row.GetValueOrDefault(plan.GroupBy, "");
            if (!groups.TryGetValue(label, out var bucket)) groups[label] = bucket = new Bucket();
            bucket.Add(value);
        }
        string? result = total.Count == 0 && numeric ? null : total.Value(plan.Aggregate).ToString(Culture);
        var ranked = groups.OrderByDescending(g => g.Value.Value(plan.Aggregate))
            .ThenBy(g => g.Key, StringComparer.Ordinal).Take(plan.Top)
            .Select(g => new WorkspaceGroup(g.Key, g.Value.Value(plan.Aggregate).ToString(Culture), g.Value.Count)).ToArray();
        // A single value is a KPI, not a chart; nominal categories never get a line chart.
        return new(plan.Aggregate, plan.Measure, plan.GroupBy, result, unit, rows.Count,
            total.Count, rows.Count-selected.Count, invalid, groups.Count, ranked,
            ranked.Length >= 2 ? "bar" : "none", source.Id, source.Sha256);
    }
}

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/workspace/{id:guid}/recommend", async (Guid id, IDatasetRepository repo, CancellationToken ct) =>
        {
            var source = await repo.GetAsync(id, ct);
            return source is null ? Results.NotFound() : Results.Ok(WorkspaceService.Recommend(source));
        });
        app.MapPost("/api/workspace/{id:guid}/analyze", async (Guid id, WorkspaceAnalysis plan, IDatasetRepository repo, CancellationToken ct) =>
        {
            var source = await repo.GetAsync(id, ct);
            if (source is null) return Results.NotFound();
            try { return Results.Ok(WorkspaceService.Analyze(source, plan)); }
            catch (WorkspaceValidationException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (OverflowException) { return Results.BadRequest(new { error = "Numeric values exceed the supported decimal aggregation range." }); }
        });
        return app;
    }
}
