using QueryForge.Api;
using Xunit;

namespace QueryForge.Api.Tests;

public sealed class QualityProfilerTests
{
    private readonly IQualityProfiler profiler = new DeterministicQualityProfiler();

    private static Dictionary<string, string> Row(string id, string amount, string customer = "Ahmed")
        => new() { ["order_id"] = id, ["amount"] = amount, ["customer"] = customer };

    private static string[] Headers => ["order_id", "amount", "customer"];

    [Fact]
    public void CleanDataset_HasNoIssues()
    {
        var result = profiler.Analyze(Headers, [Row("A1", "15.50"), Row("A2", "40")]);
        Assert.Equal(2, result.RowCount);
        Assert.Empty(result.Issues);
        Assert.Equal(0, result.RowsWithIssues);
    }

    [Fact]
    public void EmptyDataset_HasNoIssues()
    {
        var result = profiler.Analyze(Headers, []);
        Assert.Equal(0, result.RowCount);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void DuplicateIds_AreDetectedCaseInsensitively()
    {
        var result = profiler.Analyze(Headers, [Row("A1", "10"), Row("a1", "15")]);
        Assert.Contains(result.Issues, i => i.Code == "DUPLICATE_ID" && i.RowNumber == 3);
    }

    [Fact]
    public void DuplicateIds_TrimLeadingAndTrailingSpaces()
    {
        var result = profiler.Analyze(Headers, [Row(" A1 ", "10"), Row("A1", "15")]);
        Assert.Contains(result.Issues, i => i.Code == "DUPLICATE_ID");
    }

    [Fact]
    public void InvalidAmount_FlagsZeroAndNegative()
    {
        var result = profiler.Analyze(Headers, [Row("A1", "0"), Row("A2", "-25")]);
        Assert.Equal(2, result.Issues.Count(i => i.Code == "INVALID_AMOUNT"));
    }

    [Fact]
    public void MissingValue_IsReportedWithExcelRowNumber()
    {
        var result = profiler.Analyze(Headers, [Row("A1", "10", "")]);
        Assert.Contains(result.Issues, i => i.Code == "MISSING_VALUE"
            && i.RowNumber == 2 && i.Column == "customer");
    }

    [Fact]
    public void BadDecimal_IsRejectedWithoutParsingAsThousands()
    {
        var result = profiler.Analyze(Headers, [Row("A1", "12,34")]);
        Assert.Contains(result.Issues, i => i.Code == "INVALID_AMOUNT");
    }

    [Fact]
    public void Sum_ExcludesAllFlaggedRows()
    {
        var rows = new List<Dictionary<string, string>>
        {
            Row("A1", "12.50"), Row("A1", "20"), Row("A3", "0"), Row("A4", "8")
        };
        var report = profiler.Analyze(Headers, rows);
        var metric = profiler.Summarize("amount", Headers, rows, report);
        Assert.Equal(20.50m, metric.Sum);
        Assert.Equal(2, metric.IncludedRows);
        Assert.Equal(2, metric.ExcludedRows);
        Assert.Equal(10.25m, metric.Average);
    }

    [Fact]
    public void MixedCurrencies_AreFlaggedWithoutAutomaticConversion()
    {
        string[] headers = ["order_id", "amount", "currency"];
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["order_id"] = "A1", ["amount"] = "20", ["currency"] = "EUR" },
            new() { ["order_id"] = "A2", ["amount"] = "30", ["currency"] = "GBP" },
            new() { ["order_id"] = "A3", ["amount"] = "10", ["currency"] = "eur" }
        };

        var report = profiler.Analyze(headers, rows);
        Assert.Single(report.Issues.Where(i => i.Code == "MIXED_CURRENCY"));
        Assert.Equal(3, report.Issues.Single(i => i.Code == "MIXED_CURRENCY").RowNumber);
        var metric = profiler.Summarize("amount", headers, rows, report);
        Assert.Equal(30m, metric.Sum);
        Assert.Equal(2, metric.IncludedRows);
    }

    [Fact]
    public void NoCurrencyColumn_DoesNotInventCurrencyIssues()
    {
        var report = profiler.Analyze(Headers, [Row("A1", "12"), Row("A2", "13")]);
        Assert.DoesNotContain(report.Issues, issue => issue.Code == "MIXED_CURRENCY");
    }

    [Fact]
    public void UnknownMetricColumn_Throws()
    {
        var rows = new List<Dictionary<string, string>> { Row("A1", "10") };
        var report = profiler.Analyze(Headers, rows);
        Assert.Throws<ArgumentException>(() => profiler.Summarize("secret", Headers, rows, report));
    }

    [Fact]
    public void NonnumericSelectedColumn_HasNoAverage()
    {
        var rows = new List<Dictionary<string, string>> { Row("A1", "10") };
        var report = profiler.Analyze(Headers, rows);
        var metric = profiler.Summarize("customer", Headers, rows, report);
        Assert.Null(metric.Average);
        Assert.Equal(0, metric.IncludedRows);
    }
}
