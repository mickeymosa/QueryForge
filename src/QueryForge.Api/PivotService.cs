using System.Globalization;
using System.Text.Json.Serialization;

namespace QueryForge.Api;

/// <summary>
/// Deterministic pivoting over the strictly bounded legacy dataset (2 MiB / 3,000 rows).
/// No arbitrary SQL, formulas, cross-dataset joins or dynamic schema mutation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PivotAggregate>))]
public enum PivotAggregate { Count, Sum, Average }

public sealed record PivotRequest(
    string RowDimension,
    string? ColumnDimension,
    PivotAggregate Aggregate,
    string? Measure = null,
    bool ExcludeFlaggedRows = true);

public sealed record PivotRow(
    string Label,
    IReadOnlyList<string?> Values,
    string? Total,
    int Records);

public sealed record PivotResult(
    Guid DatasetId,
    string DatasetSha256,
    string RowDimension,
    string? ColumnDimension,
    string? Measure,
    PivotAggregate Aggregate,
    string Unit,
    int IncludedRows,
    int ExcludedRows,
    int InvalidNumericRows,
    bool ExcludeFlaggedRows,
    IReadOnlyList<string> ColumnLabels,
    IReadOnlyList<PivotRow> Rows,
    IReadOnlyList<string?> ColumnTotals,
    string? GrandTotal,
    IReadOnlyList<string> Notes);

public sealed class PivotValidationException(string message) : Exception(message);

public sealed class PivotService
{
    public const int MaxRowGroups = 20;
    public const int MaxColumnGroups = 12;

    private sealed class Totals
    {
        public decimal Sum { get; private set; }
        public int Records { get; private set; }
        public void Add(decimal value)
        {
            Sum = checked(Sum + value);
            Records++;
        }
        public decimal? Value(PivotAggregate aggregate) =>
            aggregate switch
            {
                PivotAggregate.Count => Records,
                PivotAggregate.Sum => Records == 0 ? null : Sum,
                PivotAggregate.Average => Records == 0 ? null : Sum / Records,
                _ => throw new PivotValidationException("Unsupported aggregation.")
            };
    }

    // Send exact decimal text over JSON: JavaScript Number can otherwise silently
    // round high-precision financial totals while rendering or exporting.
    private static string? Exact(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string Category(Dictionary<string, string> row, string header)
    {
        string value = row.GetValueOrDefault(header, "").Trim();
        return value.Length == 0 ? "(missing)" : value;
    }

    private static string Canonical(Dictionary<string,string> lookup, string value)
    {
        if (lookup.TryGetValue(value, out var existing)) return existing;
        lookup.Add(value, value);
        return value;
    }

    public PivotResult Build(DatasetEntity dataset, PivotRequest request)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(request);
        string[] headers = dataset.Headers;
        if (string.IsNullOrWhiteSpace(request.RowDimension) ||
            !headers.Contains(request.RowDimension, StringComparer.Ordinal))
            throw new PivotValidationException("Choose an exact existing row dimension.");
        if (!string.IsNullOrEmpty(request.ColumnDimension) &&
            !headers.Contains(request.ColumnDimension, StringComparer.Ordinal))
            throw new PivotValidationException("Choose an exact existing column dimension.");
        if (request.ColumnDimension == request.RowDimension)
            throw new PivotValidationException("Row and column dimensions must be different.");
        if (!Enum.IsDefined(request.Aggregate))
            throw new PivotValidationException("Unsupported aggregation.");
        if ((request.Aggregate is PivotAggregate.Sum or PivotAggregate.Average) &&
            (string.IsNullOrWhiteSpace(request.Measure) ||
             !headers.Contains(request.Measure, StringComparer.Ordinal)))
            throw new PivotValidationException("Sum and average require an existing numeric measure.");

        var rows = dataset.Rows;
        if (rows.Count > CsvImportService.MaxRows)
            throw new PivotValidationException("This pivot operates only on the bounded editable dataset.");
        var flagged = dataset.Report.Issues.Select(i => i.RowNumber).ToHashSet();
        var selected = rows.Select((row, index) => new { Row = row, CsvRow = index + 2 })
            .Where(x => !request.ExcludeFlaggedRows || !flagged.Contains(x.CsvRow))
            .Select(x => x.Row).ToList();

        string unit = request.Aggregate == PivotAggregate.Count ? "records" : "unspecified units";
        var notes = new List<string>
        {
            request.ExcludeFlaggedRows
                ? "Rows with any reported data-quality issue are excluded. This includes warnings."
                : "Quality-flagged rows are included by explicit user choice.",
            "Missing dimension values are labeled (missing). Counts include all selected records.",
            "Sum and average skip unparseable measures and report their count. Averages are weighted by valid record count.",
            "Pivot results are calculated on the bounded editable data engine; the large-file v2 path is not connected."
        };
        if (request.Aggregate is PivotAggregate.Sum or PivotAggregate.Average)
        {
            var currency = headers.FirstOrDefault(x =>
                x.Equals("currency", StringComparison.OrdinalIgnoreCase));
            if (currency is not null && selected.Count > 0)
            {
                var units = selected.Select(x => x.GetValueOrDefault(currency,"").Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (units.Length != 1 || units[0].Length == 0)
                    throw new PivotValidationException(
                        "Cannot aggregate mixed or missing currencies. Filter or clean to one currency first.");
                unit = units[0].ToUpperInvariant();
            }
            else if (currency is null)
                notes.Add("No currency column exists. Confirm units and business semantics before interpreting sums or averages.");
        }

        var rowNames = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var colNames = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var cells = new Dictionary<(string Row,string Column), Totals>();
        var rowTotals = new Dictionary<string,Totals>(StringComparer.OrdinalIgnoreCase);
        var columnTotals = new Dictionary<string,Totals>(StringComparer.OrdinalIgnoreCase);
        var grand = new Totals();
        int invalid = 0;
        foreach (var source in selected)
        {
            var rowLabel = Canonical(rowNames, Category(source, request.RowDimension));
            string colLabel = request.ColumnDimension is null or ""
                ? "All records"
                : Canonical(colNames, Category(source, request.ColumnDimension));
            if (rowNames.Count > MaxRowGroups ||
                (request.ColumnDimension is not null and not "" && colNames.Count > MaxColumnGroups))
                throw new PivotValidationException(
                    $"Too many unique categories. Maximum {MaxRowGroups} row groups and {MaxColumnGroups} column groups. Filter data first.");
            if (request.Aggregate != PivotAggregate.Count &&
                !decimal.TryParse(source.GetValueOrDefault(request.Measure!, ""),
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint |
                    NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                    CultureInfo.InvariantCulture, out _))
            {
                invalid++;
                continue;
            }
            decimal value;
            if (request.Aggregate == PivotAggregate.Count) value = 1m;
            else
                value = decimal.Parse(source.GetValueOrDefault(request.Measure!, ""),
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint |
                    NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                    CultureInfo.InvariantCulture);

            var key = (rowLabel, colLabel);
            if (!cells.TryGetValue(key, out var cell))
                cells[key] = cell = new Totals();
            cell.Add(value);
            if (!rowTotals.TryGetValue(rowLabel, out var rt))
                rowTotals[rowLabel] = rt = new Totals();
            rt.Add(value);
            if (!columnTotals.TryGetValue(colLabel, out var ct))
                columnTotals[colLabel] = ct = new Totals();
            ct.Add(value);
            grand.Add(value);
        }

        var categories = rowNames.Values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x, StringComparer.Ordinal).ToArray();
        string[] columns = request.ColumnDimension is null or ""
            ? ["All records"]
            : colNames.Values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x, StringComparer.Ordinal).ToArray();
        var result = categories.Select(label =>
            new PivotRow(
                label,
                columns.Select(col => Exact(cells.TryGetValue((label,col), out var bucket)
                    ? bucket.Value(request.Aggregate)
                    : request.Aggregate == PivotAggregate.Count ? 0m : (decimal?)null)).ToArray(),
                Exact(rowTotals.TryGetValue(label, out var total)
                    ? total.Value(request.Aggregate)
                    : request.Aggregate == PivotAggregate.Count ? 0m : null),
                rowTotals.TryGetValue(label, out var records) ? records.Records : 0
            )).ToArray();

        string?[] columnValues = columns.Select(col => Exact(columnTotals.TryGetValue(col, out var total)
            ? total.Value(request.Aggregate)
            : request.Aggregate == PivotAggregate.Count ? 0m : (decimal?)null)).ToArray();
        return new(dataset.Id,dataset.Sha256,request.RowDimension,
            request.ColumnDimension is null or "" ? null : request.ColumnDimension,
            request.Aggregate == PivotAggregate.Count ? null : request.Measure,
            request.Aggregate,unit,selected.Count,rows.Count-selected.Count,
            invalid,request.ExcludeFlaggedRows,columns,result,
            columnValues,Exact(grand.Value(request.Aggregate)),notes);
    }
}
