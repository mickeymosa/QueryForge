using System.Globalization;

namespace QueryForge.Api;

// Abstract Data Types (ADTs) keep the *meaning* of our data separate from the UI and storage.
public sealed record DataIssue(int RowNumber, string Column, string Code, string Severity, string Message);
public sealed record ColumnProfile(string Name, int Filled, int Missing);
public sealed record QualityReport(
    int RowCount,
    int ColumnCount,
    int RowsWithIssues,
    IReadOnlyList<ColumnProfile> Columns,
    IReadOnlyList<DataIssue> Issues);
public sealed record ParsedDataset(
    string FileName,
    string Sha256,
    IReadOnlyList<string> Headers,
    IReadOnlyList<Dictionary<string, string>> Rows);

public sealed record NumericMetric(
    string Column,
    int IncludedRows,
    int ExcludedRows,
    decimal Sum,
    decimal? Average);

// Consumers rely on these contracts, not on how profiling works internally.
public interface IQualityProfiler
{
    QualityReport Analyze(
        IReadOnlyList<string> headers,
        IReadOnlyList<Dictionary<string, string>> rows);

    NumericMetric Summarize(
        string column,
        IReadOnlyList<string> headers,
        IReadOnlyList<Dictionary<string, string>> rows,
        QualityReport report);
}

public sealed class DeterministicQualityProfiler : IQualityProfiler
{
    public QualityReport Analyze(
        IReadOnlyList<string> headers,
        IReadOnlyList<Dictionary<string, string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var issues = new List<DataIssue>();
        var profiles = new List<ColumnProfile>();
        foreach (var header in headers)
        {
            int missing = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].TryGetValue(header, out string? value) ||
                    string.IsNullOrWhiteSpace(value))
                {
                    missing++;
                    issues.Add(new(i + 2, header, "MISSING_VALUE", "warning",
                        $"Missing value in {header}"));
                }
            }
            profiles.Add(new(header, rows.Count - missing, missing));
        }

        string? identifier = headers.FirstOrDefault(h =>
            h.Equals("order_id", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("orderid", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("id", StringComparison.OrdinalIgnoreCase));

        if (identifier is not null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Count; i++)
            {
                var value = rows[i].GetValueOrDefault(identifier, "").Trim();
                if (value.Length > 0 && !seen.Add(value))
                {
                    issues.Add(new(i + 2, identifier, "DUPLICATE_ID", "error",
                        "Identifier already appeared in an earlier row"));
                }
            }
        }

        string? amountColumn = headers.FirstOrDefault(h =>
            h.Equals("amount", StringComparison.OrdinalIgnoreCase));
        if (amountColumn is not null)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                string value = rows[i].GetValueOrDefault(amountColumn, "");
                if (string.IsNullOrWhiteSpace(value))
                    continue; // Missing-value check already reports it.

                if (!TryDecimal(value, out decimal amount) || amount <= 0)
                {
                    issues.Add(new(i + 2, amountColumn, "INVALID_AMOUNT", "error",
                        "Amount must be a positive invariant-culture decimal"));
                }
            }
        }

        return new(rows.Count, headers.Count,
            issues.Select(x => x.RowNumber).Distinct().Count(),
            profiles, issues);
    }

    public NumericMetric Summarize(
        string column,
        IReadOnlyList<string> headers,
        IReadOnlyList<Dictionary<string, string>> rows,
        QualityReport report)
    {
        // Only a known header is accepted: the user never supplies SQL.
        if (!headers.Contains(column, StringComparer.Ordinal))
            throw new ArgumentException("Unknown column", nameof(column));

        var excluded = report.Issues.Select(i => i.RowNumber).ToHashSet();
        decimal sum = 0;
        int included = 0;

        for (int i = 0; i < rows.Count; i++)
        {
            if (excluded.Contains(i + 2))
                continue;

            string text = rows[i].GetValueOrDefault(column, "");
            if (TryDecimal(text, out decimal value))
            {
                sum += value;
                included++;
            }
        }
        return new(column, included, rows.Count - included, sum,
            included == 0 ? null : sum / included);
    }

    private static bool TryDecimal(string input, out decimal value) =>
        decimal.TryParse(input, NumberStyles.AllowLeadingSign |
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowLeadingWhite |
            NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture, out value);
}
