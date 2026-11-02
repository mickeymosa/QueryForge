using System.Text.Json;
using QueryForge.Api;
using Xunit;

namespace QueryForge.Api.Tests;

public sealed class AdvancedEtlOperationsTests
{
    private readonly EtlPipeline _pipeline = new();

    private static DatasetEntity Source() => new()
    {
        Id = Guid.Parse("4475981b-d594-4506-8f49-540b60953a18"),
        FileName = "advanced.csv",
        Sha256 = new string('c', 64),
        RowCount = 4,
        HeadersJson = JsonSerializer.Serialize(new[] { "id", "name", "amount", "region" }),
        RowsJson = JsonSerializer.Serialize(new[]
        {
            new Dictionary<string,string> { ["id"]="A1", ["name"]="  Ahmed  ", ["amount"]="10", ["region"]="EU" },
            new Dictionary<string,string> { ["id"]="A2", ["name"]="Sara", ["amount"]="22.50", ["region"]="US" },
            new Dictionary<string,string> { ["id"]="A3", ["name"]="Mona", ["amount"]="invalid", ["region"]="EU" },
            new Dictionary<string,string> { ["id"]="A4", ["name"]="Omar", ["amount"]="5", ["region"]="UK" }
        })
    };

    [Fact]
    public void FilterEquals_KeepsMatchingRowsAndSourceLineage()
    {
        var result = _pipeline.Execute(Source(), new("EU only",
            [new(TransformOperation.FilterEquals,"region",From:"EU")]));
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal([2,4], result.OriginalRowNumbers);
        Assert.Equal(2, result.Audit[0].AffectedRows);
    }

    [Fact]
    public void FilterContains_KeepsPartialTextMatches()
    {
        var result = _pipeline.Execute(Source(), new("Names",
            [new(TransformOperation.FilterContains,"name",From:"ar")]));
        Assert.Equal(2, result.Rows.Count); // Sara and Omar
        Assert.Equal([3,5], result.OriginalRowNumbers);
    }

    [Fact]
    public void FilterAmountAbove_RejectsInvalidNumericRows()
    {
        var result = _pipeline.Execute(Source(), new("Big",
            [new(TransformOperation.FilterAmountAbove,"amount",From:"9.99")]));
        Assert.Equal([2,3], result.OriginalRowNumbers);
        Assert.Equal(2, result.Audit[0].AffectedRows);
    }

    [Fact]
    public void FilterAmountAbove_RejectsAmbiguousDecimalFormat()
    {
        Assert.Throws<EtlValidationException>(() =>
            _pipeline.Execute(Source(), new("Bad",
            [new(TransformOperation.FilterAmountAbove,"amount",From:"9,99")])));
    }

    [Fact]
    public void AddColumn_CreatesImmutableCalculatedMetadata()
    {
        var source = Source();
        var result = _pipeline.Execute(source, new("Tag",
            [new(TransformOperation.AddColumn,"source_file",To:"orders-2026")]));
        Assert.Equal(5, result.Headers.Count);
        Assert.All(result.Rows, r => Assert.Equal("orders-2026",r["source_file"]));
        Assert.DoesNotContain("source_file",source.Headers);
    }

    [Fact]
    public void CopyColumn_KeepsOriginal()
    {
        var result = _pipeline.Execute(Source(), new("Copy",
            [new(TransformOperation.CopyColumn,"amount",To:"raw_amount")]));
        Assert.All(result.Rows,r=>Assert.Equal(r["amount"],r["raw_amount"]));
    }

    [Fact]
    public void AddOrCopyColumn_RejectsNameCollisions()
    {
        Assert.Throws<EtlValidationException>(() =>
            _pipeline.Execute(Source(), new("Bad",
            [new(TransformOperation.AddColumn,"name",To:"abc")])));
        Assert.Throws<EtlValidationException>(() =>
            _pipeline.Execute(Source(), new("Bad",
            [new(TransformOperation.CopyColumn,"amount",To:"name")])));
    }

    [Fact]
    public void SortAscending_IsStableAndRetainsOriginalRowReferences()
    {
        var result = _pipeline.Execute(Source(), new("Sort",
            [new(TransformOperation.SortAscending,"region")]));
        Assert.Equal([2,4,5,3],result.OriginalRowNumbers);
        Assert.Equal("EU",result.Rows[0]["region"]);
    }

    [Fact]
    public void ReplaceText_ChangesSubstringOnly()
    {
        var result = _pipeline.Execute(Source(), new("Clean",
            [new(TransformOperation.ReplaceText,"name",From:"  ",To:"")]));
        Assert.Equal("Ahmed",result.Rows[0]["name"]);
        Assert.Equal("Sara",result.Rows[1]["name"]);
    }

    [Fact]
    public void ChainedDerivedColumn_CanBeUsedByLaterStep()
    {
        var result = _pipeline.Execute(Source(), new("Chain",
        [
            new(TransformOperation.CopyColumn,"region",To:"region_copy"),
            new(TransformOperation.Lowercase,"region_copy"),
            new(TransformOperation.FilterEquals,"region_copy",From:"eu")
        ]));
        Assert.Equal(2,result.Rows.Count);
        Assert.Equal("EU",result.Rows[0]["region"]);
        Assert.Equal("eu",result.Rows[0]["region_copy"]);
    }

    [Fact]
    public void DeterministicHash_AfterMultipleAdvancedSteps()
    {
        var source = Source();
        var request = new EtlRequest("Stable",[
            new(TransformOperation.AddColumn,"batch",To:"pilot"),
            new(TransformOperation.SortAscending,"name"),
            new(TransformOperation.FilterAmountAbove,"amount",From:"0")
        ]);
        var first = _pipeline.Execute(source,request);
        var second = _pipeline.Execute(source,request);
        Assert.Equal(first.DataSha256,second.DataSha256);
        Assert.Equal(first.OriginalRowNumbers,second.OriginalRowNumbers);
    }
}
