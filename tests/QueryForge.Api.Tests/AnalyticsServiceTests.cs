using System.Text.Json;
using QueryForge.Api;
using Xunit;

namespace QueryForge.Api.Tests;

public sealed class AnalyticsServiceTests
{
    private readonly IDataInsightsService service = new DeterministicDataInsightsService();
    private readonly IQualityProfiler profiler = new DeterministicQualityProfiler();

    private DatasetEntity Source()
    {
        string[] headers = ["order_id","customer","amount","currency","region"];
        List<Dictionary<string,string>> rows =
        [
            Row("A1","Ahmed","10","EUR","EU"),
            Row("A2","Sara","20","EUR","US"),
            Row("A3","Mona","30","GBP","EU"),
            Row("A4","Omar","0","EUR","EU"),
            Row("A1","Nora","40","EUR","US"),
            Row("A6","","50","EUR","EU")
        ];
        var report = profiler.Analyze(headers,rows);
        return new()
        {
            Id=Guid.Parse("3c644a80-3b5a-4fb4-8c5c-90099deefdb0"),
            FileName="orders.csv",RowCount=rows.Count,Sha256=new string('d',64),
            HeadersJson=JsonSerializer.Serialize(headers),
            RowsJson=JsonSerializer.Serialize(rows),
            ReportJson=JsonSerializer.Serialize(report)
        };
    }

    private static Dictionary<string,string> Row(
        string id,string customer,string amount,string currency,string region) =>
        new()
        {
            ["order_id"]=id,["customer"]=customer,["amount"]=amount,
            ["currency"]=currency,["region"]=region
        };

    [Fact]
    public void Insights_ReportTrueCountsAndSuggestedCharts()
    {
        var insight = service.GetInsights(Source());
        Assert.Equal(6,insight.RowCount);
        Assert.Equal(4,insight.RowsWithIssues);
        Assert.Equal(33.33m,insight.QualityScorePercent);
        Assert.Contains(insight.SuggestedCharts,c=>c.Kind==ChartKind.Histogram&&c.Column=="amount");
        Assert.Contains(insight.SuggestedCharts,c=>c.Kind==ChartKind.CountByCategory&&c.Column=="region");
        Assert.Equal("region",insight.SuggestedCharts[0].Column);
    }

    [Fact]
    public void CountByCategory_DefaultsToExcludingQualityFlaggedRows()
    {
        var chart = service.BuildChart(Source(),new(ChartKind.CountByCategory,"region"));
        Assert.Equal(2,chart.IncludedRows);
        Assert.Equal(4,chart.ExcludedRows);
        Assert.Equal(1m,chart.Points.Single(p=>p.Label=="EU").Value);
        Assert.Equal(1m,chart.Points.Single(p=>p.Label=="US").Value);
        Assert.True(chart.ExcludeFlaggedRows);
    }

    [Fact]
    public void CategoryCounts_AllowExplicitInclusionOfFlaggedRows()
    {
        var chart=service.BuildChart(Source(),
            new(ChartKind.CountByCategory,"region",ExcludeFlaggedRows:false));
        Assert.Equal(6,chart.IncludedRows);
        Assert.Equal(0,chart.ExcludedRows);
        Assert.Equal(4m,chart.Points.Single(p=>p.Label=="EU").Value);
        Assert.Equal(2m,chart.Points.Single(p=>p.Label=="US").Value);
    }

    [Fact]
    public void SumByCategory_UsesOneCurrencyAndAuditableTotals()
    {
        var chart=service.BuildChart(Source(),new(ChartKind.SumByCategory,"region","amount"));
        Assert.Equal("EUR",chart.Unit);
        Assert.Equal(30m,chart.Points.Sum(p=>p.Value));
        Assert.Equal(2,chart.IncludedRows);
    }

    [Fact]
    public void MixedCurrencySum_IsRefused()
    {
        Assert.Throws<ChartValidationException>(()=>service.BuildChart(Source(),
            new(ChartKind.SumByCategory,"region","amount",ExcludeFlaggedRows:false)));
    }

    [Fact]
    public void Histogram_UsesNumericValuesAndIncludesBothExtremes()
    {
        var chart=service.BuildChart(Source(),new(ChartKind.Histogram,"amount",TopN:5));
        Assert.Equal(5,chart.Points.Count);
        Assert.Equal(2m,chart.Points.Sum(x=>x.Value));
        Assert.Equal("records",chart.Unit);
    }

    [Fact]
    public void Histogram_NonnumericValuesAreCountedAsExcludedFromBuckets()
    {
        var source=Source();
        source.RowsJson=JsonSerializer.Serialize(new[]
        {
            Row("B1","Ahmed","10","EUR","EU"),
            Row("B2","Sara","not a decimal","EUR","US")
        });
        source.RowCount=2;
        source.ReportJson=JsonSerializer.Serialize(
            profiler.Analyze(source.Headers,source.Rows));
        var chart=service.BuildChart(source,
            new(ChartKind.Histogram,"amount",ExcludeFlaggedRows:false));
        Assert.Equal(1,chart.InvalidNumericRows);
        Assert.Single(chart.Points);
    }

    [Fact]
    public void ChartRejectsUnknownColumnAndUnknownMeasure()
    {
        Assert.Throws<ChartValidationException>(()=>service.BuildChart(Source(),
            new(ChartKind.CountByCategory,"does_not_exist")));
        Assert.Throws<ChartValidationException>(()=>service.BuildChart(Source(),
            new(ChartKind.SumByCategory,"region","not_a_measure")));
    }

    [Fact]
    public void ChartRejectsUnsafeTopN()
    {
        Assert.Throws<ChartValidationException>(()=>service.BuildChart(Source(),
            new(ChartKind.CountByCategory,"region",TopN:0)));
        Assert.Throws<ChartValidationException>(()=>service.BuildChart(Source(),
            new(ChartKind.CountByCategory,"region",TopN:21)));
    }

    [Fact]
    public void TopN_AccuratelyCombinesRemainingCategories()
    {
        var source=Source();
        var chart=service.BuildChart(source,
            new(ChartKind.CountByCategory,"customer",TopN:2,ExcludeFlaggedRows:false));
        Assert.Equal(3,chart.Points.Count);
        Assert.Equal("(other categories)",chart.Points[^1].Label);
        Assert.Equal(6m,chart.Points.Sum(x=>x.Value));
    }

    [Fact]
    public void Advisor_ReportsOnlyEvidenceAndNeverExecutesEtl()
    {
        var source=Source();
        var originalRows=source.RowsJson;
        var advice=service.BuildAdvisoryReport(source);
        Assert.False(advice.AiEnabled);
        Assert.Equal("QueryForge deterministic evidence advisor",advice.GeneratedBy);
        Assert.Equal(source.Sha256,advice.DatasetSha256);
        Assert.Contains("6 records",advice.ExecutiveSummary);
        Assert.Contains(advice.SuggestedTransformations,x=>x.Title=="Inspect duplicate keys");
        Assert.Contains(advice.SuggestedTransformations,x=>x.Title=="Separate currencies before aggregating");
        Assert.Equal(originalRows,source.RowsJson);
    }

    [Fact]
    public void EmptySource_GeneratesSafeEmptyChartsAndSummary()
    {
        var source=Source();source.RowCount=0;
        source.RowsJson="[]";
        source.ReportJson=JsonSerializer.Serialize(profiler.Analyze(source.Headers,[]));
        var insights=service.GetInsights(source);
        Assert.Equal(100m,insights.QualityScorePercent);
        Assert.Empty(insights.SuggestedCharts);
        var chart=service.BuildChart(source,new(ChartKind.CountByCategory,"region"));
        Assert.Empty(chart.Points);
    }
}
