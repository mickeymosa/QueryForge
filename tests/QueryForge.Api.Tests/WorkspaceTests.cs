using System.Text.Json;
using QueryForge.Api;
using Xunit;

namespace QueryForge.Api.Tests;

public sealed class WorkspaceTests
{
    private static DatasetEntity Source(params string[][] cells)
    {
        string[] headers = ["id","product","amount","currency"];
        var rows = cells.Select(c => headers.Zip(c).ToDictionary(p => p.First, p => p.Second)).ToList();
        return new DatasetEntity {
            Id = Guid.NewGuid(), Sha256 = new string('a',64), RowCount = rows.Count,
            HeadersJson = JsonSerializer.Serialize(headers), RowsJson = JsonSerializer.Serialize(rows),
            ReportJson = JsonSerializer.Serialize(new DeterministicQualityProfiler().Analyze(headers,rows))
        };
    }

    [Fact]
    public void ExactDuplicatesPreserveDifferentRecordsWithSameId()
    {
        var source = Source(["1","A","10","EUR"],["1","A","10","EUR"],["1","B","20","EUR"]);
        var result = new EtlPipeline().Execute(source,new("clean",[new(TransformOperation.DropExactDuplicates)]));
        Assert.Equal(2,result.Rows.Count);
        Assert.Equal("B",result.Rows[1]["product"]);
        Assert.Equal(3,source.Rows.Count);
        Assert.Equal(1,Assert.Single(WorkspaceService.Recommend(source)).AffectedRows);
    }

    [Fact]
    public void ScalarAnswerHasNoChartAndReportsInvalidValues()
    {
        var source = Source(["1","A","10","EUR"],["2","B","bad","EUR"]);
        var result = WorkspaceService.Analyze(source,new("sum","amount"));
        Assert.Equal("10",result.Total);
        Assert.Equal("none",result.Chart);
        Assert.Equal(1,result.InvalidMeasureRows);
        Assert.Equal(1,result.UsedRows);
    }

    [Fact]
    public void RankedGroupsUseFullDatasetTotalAndSignedValues()
    {
        var source = Source(["1","A","10","EUR"],["2","B","-5","EUR"],["3","C","-10","EUR"]);
        var result = WorkspaceService.Analyze(source,new("sum","amount","product",2));
        Assert.Equal("-5",result.Total);
        Assert.Equal("bar",result.Chart);
        Assert.Equal(3,result.GroupCount);
        Assert.Equal("A",result.Groups[0].Label);
        Assert.Equal("-5",result.Groups[1].Value);
    }

    [Fact]
    public void AverageIsWeightedAndUnknownFieldsRejected()
    {
        var source = Source(["1","A","10","EUR"],["2","A","20","EUR"],["3","B","30","EUR"]);
        Assert.Equal("20",WorkspaceService.Analyze(source,new("average","amount","product")).Total);
        Assert.Throws<WorkspaceValidationException>(()=>WorkspaceService.Analyze(source,new("sum","invented")));
    }

    [Fact]
    public void MixedCurrenciesAreNotSilentlyCombined()
    {
        var source = Source(["1","A","10","EUR"],["2","B","20","USD"]);
        Assert.Throws<WorkspaceValidationException>(()=>WorkspaceService.Analyze(source,new("sum","amount")));
        Assert.Equal("2",WorkspaceService.Analyze(source,new("count")).Total);
    }

    [Fact]
    public void EmptyNumericResultIsNotZero()
    {
        var source = Source(["1","A","bad","EUR"]);
        Assert.Null(WorkspaceService.Analyze(source,new("average","amount")).Total);
    }
}
