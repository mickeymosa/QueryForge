using System.Text.Json;
using QueryForge.Api;
using Xunit;

namespace QueryForge.Api.Tests;

public sealed class PivotServiceTests
{
    private readonly PivotService service = new();
    private readonly IQualityProfiler profiler = new DeterministicQualityProfiler();

    private DatasetEntity Source()
    {
        string[] headers = ["order_id", "region", "product", "amount", "currency"];
        var rows = new List<Dictionary<string,string>>
        {
            Row("A1","EU","Hardware","10","EUR"),
            Row("A2","EU","Software","20","EUR"),
            Row("A3","US","Hardware","30","EUR"),
            Row("A4","US","Hardware","40","EUR"),
            Row("A5","EU","Hardware","bad","EUR"),
            Row("A6","US","Software","50","EUR"),
            Row("A7","EU","Software","15","EUR")
        };
        return Build(headers,rows);
    }

    private DatasetEntity Build(string[] headers,List<Dictionary<string,string>> rows) => new()
    {
        Id = Guid.Parse("b7863623-2da3-45a8-98c1-a45cb82819a4"),
        FileName = "pivot-fixture.csv", Sha256 = new string('f',64),
        RowCount = rows.Count, HeadersJson = JsonSerializer.Serialize(headers),
        RowsJson = JsonSerializer.Serialize(rows),
        ReportJson = JsonSerializer.Serialize(profiler.Analyze(headers,rows))
    };

    private static Dictionary<string,string> Row(
        string id,string region,string product,string amount,string currency) => new()
    {
        ["order_id"]=id,["region"]=region,["product"]=product,
        ["amount"]=amount,["currency"]=currency
    };

    [Fact]
    public void TwoDimensionalSum_HasCorrectCellsAndTotals()
    {
        var result=service.Build(Source(),new("region","product",PivotAggregate.Sum,"amount"));
        Assert.Equal(["Hardware","Software"],result.ColumnLabels);
        Assert.Equal(["EU","US"],result.Rows.Select(r=>r.Label));
        Assert.Equal([10m,35m],result.Rows[0].Values);
        Assert.Equal([70m,50m],result.Rows[1].Values);
        Assert.Equal(45m,result.Rows[0].Total);
        Assert.Equal(120m,result.Rows[1].Total);
        Assert.Equal([80m,85m],result.ColumnTotals);
        Assert.Equal(165m,result.GrandTotal);
        Assert.Equal("EUR",result.Unit);
        Assert.Equal(6,result.IncludedRows);
        Assert.Equal(1,result.ExcludedRows);
        Assert.Equal(0,result.InvalidNumericRows);
        Assert.Equal(new string('f',64),result.DatasetSha256);
    }

    [Fact]
    public void AverageTotals_AreWeightedAndNotMeansOfMeans()
    {
        var result=service.Build(Source(),new("region","product",PivotAggregate.Average,"amount"));
        Assert.Equal(17.5m,result.Rows[0].Values[1]);
        Assert.Equal(35m,result.Rows[1].Values[0]);
        Assert.Equal(15m,result.Rows[0].Total);
        Assert.Equal(40m,result.Rows[1].Total);
        Assert.Equal(27.5m,result.GrandTotal);
        Assert.Equal(80m/3m,result.ColumnTotals[0]);
        Assert.Equal(85m/3m,result.ColumnTotals[1]);
    }

    [Fact]
    public void OneDimension_CountAndMissingCrossCellsAreZero()
    {
        var one=service.Build(Source(),new("region",null,PivotAggregate.Count));
        Assert.Equal(["All records"],one.ColumnLabels);
        Assert.Equal([3m,3m],one.Rows.Select(r=>r.Total));
        Assert.Equal(6m,one.GrandTotal);

        var two=service.Build(Source(),new("product","region",PivotAggregate.Count));
        Assert.Equal(6m,two.GrandTotal);
    }

    [Fact]
    public void CountCanIncludeQualityFlaggedRowsWhenExplicitlySelected()
    {
        var result=service.Build(Source(),
            new("product","region",PivotAggregate.Count,ExcludeFlaggedRows:false));
        Assert.Equal(7,result.IncludedRows);
        Assert.Equal(0,result.ExcludedRows);
        Assert.Equal(7m,result.GrandTotal);
        Assert.Equal(4m,result.ColumnTotals[0]);
        Assert.Equal(3m,result.ColumnTotals[1]);
    }

    [Fact]
    public void NumericInvalidRowsAreSkippedAndCounted()
    {
        var result=service.Build(Source(),
            new("region","product",PivotAggregate.Sum,"amount",ExcludeFlaggedRows:false));
        Assert.Equal(165m,result.GrandTotal);
        Assert.Equal(7,result.IncludedRows);
        Assert.Equal(1,result.InvalidNumericRows);
        Assert.Equal(0,result.ExcludedRows);
    }

    [Fact]
    public void MixedCurrenciesAreRejectedForSumAndAverageButAllowedForCounts()
    {
        var source=Source();
        var rows=source.Rows;
        rows[5]["currency"]="USD";
        source.ReportJson=JsonSerializer.Serialize(profiler.Analyze(source.Headers,rows));
        source.RowsJson=JsonSerializer.Serialize(rows);
        Assert.Throws<PivotValidationException>(()=>service.Build(source,
            new("region","product",PivotAggregate.Sum,"amount",ExcludeFlaggedRows:false)));
        Assert.Throws<PivotValidationException>(()=>service.Build(source,
            new("region",null,PivotAggregate.Average,"amount",ExcludeFlaggedRows:false)));
        Assert.Equal(7m,service.Build(source,
            new("region",null,PivotAggregate.Count,ExcludeFlaggedRows:false)).GrandTotal);
    }

    [Fact]
    public void MissingCurrencyIsRejectedRatherThanSilentlyConverted()
    {
        var source=Source();
        var rows=source.Rows;
        rows[0]["currency"]="";
        source.ReportJson=JsonSerializer.Serialize(profiler.Analyze(source.Headers,rows));
        source.RowsJson=JsonSerializer.Serialize(rows);
        Assert.Throws<PivotValidationException>(()=>service.Build(source,
            new("region",null,PivotAggregate.Sum,"amount",ExcludeFlaggedRows:false)));
    }

    [Fact]
    public void MissingCategoriesHaveExplicitBucket()
    {
        var source=Source();
        var rows=source.Rows;
        rows[0]["region"]="";
        source.ReportJson=JsonSerializer.Serialize(profiler.Analyze(source.Headers,rows));
        source.RowsJson=JsonSerializer.Serialize(rows);
        var result=service.Build(source,
            new("region","product",PivotAggregate.Count,ExcludeFlaggedRows:false));
        Assert.Contains(result.Rows,r=>r.Label=="(missing)");
        Assert.Equal(7m,result.GrandTotal);
    }

    [Fact]
    public void RejectsUnknownColumnsAndInvalidMeasure()
    {
        var source=Source();
        Assert.Throws<PivotValidationException>(()=>service.Build(source,
            new("nonexistent",null,PivotAggregate.Count)));
        Assert.Throws<PivotValidationException>(()=>service.Build(source,
            new("region","nonexistent",PivotAggregate.Count)));
        Assert.Throws<PivotValidationException>(()=>service.Build(source,
            new("region","region",PivotAggregate.Count)));
        Assert.Throws<PivotValidationException>(()=>service.Build(source,
            new("region",null,PivotAggregate.Sum,"missing")));
        Assert.Throws<PivotValidationException>(()=>service.Build(source,
            new("region",null,PivotAggregate.Sum)));
        Assert.Throws<PivotValidationException>(()=>service.Build(source,
            new("region",null,(PivotAggregate)99)));
    }

    [Fact]
    public void EnforcesCategoryLimitsWithoutQuietlyDroppingRows()
    {
        string[] headers=["order_id","region","product","amount","currency"];
        var manyRows=Enumerable.Range(0,21)
            .Select(i=>Row("id"+i,"Region"+i,"P1","1","EUR")).ToList();
        Assert.Throws<PivotValidationException>(()=>service.Build(Build(headers,manyRows),
            new("region",null,PivotAggregate.Count)));

        var manyColumns=Enumerable.Range(0,13)
            .Select(i=>Row("id"+i,"R1","Product"+i,"1","EUR")).ToList();
        Assert.Throws<PivotValidationException>(()=>service.Build(Build(headers,manyColumns),
            new("region","product",PivotAggregate.Count)));
    }

    [Fact]
    public void EmptyDataReturnsSafeEmptyResults()
    {
        var source=Source();
        source.RowsJson="[]";
        source.RowCount=0;
        source.ReportJson=JsonSerializer.Serialize(profiler.Analyze(source.Headers,[]));
        var result=service.Build(source,new("region",null,PivotAggregate.Count));
        Assert.Empty(result.Rows);
        Assert.Equal(0m,result.GrandTotal);
        Assert.Equal(0,result.IncludedRows);

        var numeric=service.Build(source,new("region",null,PivotAggregate.Average,"amount"));
        Assert.Null(numeric.GrandTotal);
    }

    [Fact]
    public void SourceJsonRemainsUnchangedAndResponseIsBounded()
    {
        var source=Source();
        string original=source.RowsJson;
        var result=service.Build(source,new("region","product",PivotAggregate.Sum,"amount"));
        Assert.Equal(original,source.RowsJson);
        Assert.True(result.Rows.Count<=PivotService.MaxRowGroups);
        Assert.True(result.ColumnLabels.Count<=PivotService.MaxColumnGroups);
    }
}
