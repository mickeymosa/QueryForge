using QueryForge.Api;
using System.Text.Json;
using Xunit;

namespace QueryForge.Api.Tests;

public sealed class EtlPipelineTests
{
    private readonly EtlPipeline pipeline = new();
    private static readonly IQualityProfiler quality = new DeterministicQualityProfiler();

    private static DatasetEntity Source() => new()
    {
        Id = Guid.Parse("15331d45-2579-45ca-9c5f-4d624ec6e0d8"),
        FileName = "test.csv",
        Sha256 = new string('b', 64),
        UploadedUtc = DateTimeOffset.UtcNow,
        RowCount = 4,
        HeadersJson = JsonSerializer.Serialize(new[] { "order_id", "customer", "amount", "currency" }),
        RowsJson = JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, string> { ["order_id"]=" A1 ", ["customer"]="  Ahmed  ", ["amount"]="10", ["currency"]=" eur " },
            new Dictionary<string, string> { ["order_id"]="A2", ["customer"]=" ", ["amount"]="20", ["currency"]="USD" },
            new Dictionary<string, string> { ["order_id"]="A1", ["customer"]="Sara", ["amount"]="30", ["currency"]="EUR" },
            new Dictionary<string, string> { ["order_id"]="A4", ["customer"]="Mona", ["amount"]="40", ["currency"]="EUR" }
        }),
        ReportJson = "{}"
    };

    [Fact]
    public void TrimAll_PreservesSourceAndChangesOutput()
    {
        var original = Source();
        var output = pipeline.Execute(original,
            new("Basic cleaning", [new(TransformOperation.Trim)]));
        Assert.Equal("Ahmed", output.Rows[0]["customer"]);
        Assert.Equal(" A1 ", original.Rows[0]["order_id"]);
        Assert.Equal(1, output.Audit[0].Step);
        Assert.Equal(2, output.Audit[0].AffectedRows);
        Assert.Equal([2,3,4,5], output.OriginalRowNumbers);
    }

    [Fact]
    public void DropDuplicates_KeepsFirstRecordAndSourceRowLineage()
    {
        var output = pipeline.Execute(Source(), new("Dedupe", [
            new(TransformOperation.Trim),
            new(TransformOperation.DropDuplicates, "order_id")
        ]));
        Assert.Equal(3, output.Rows.Count);
        Assert.Equal([2,3,5], output.OriginalRowNumbers);
        Assert.Equal(1, output.Audit[1].AffectedRows);
        Assert.Equal("A1", output.Rows[0]["order_id"]);
    }

    [Fact]
    public void DropMissing_KeepsTraceability()
    {
        var output = pipeline.Execute(Source(), new("Remove blanks", [
            new(TransformOperation.DropMissing,"customer")
        ]));
        Assert.Equal([2,4,5], output.OriginalRowNumbers);
        Assert.Equal(3, output.Rows.Count);
    }

    [Fact]
    public void RenameColumn_CreatesNewColumnAndEnablesProfiling()
    {
        var source = Source();
        var output = pipeline.Execute(source, new("Rename", [
            new(TransformOperation.RenameColumn,"amount",To:"total")
        ]));
        Assert.Contains("total", output.Headers);
        Assert.DoesNotContain("amount", output.Headers);
        Assert.Equal("10", output.Rows[0]["total"]);
        Assert.Contains("amount", source.Headers);
        Assert.Equal(4, quality.Analyze(output.Headers,output.Rows).RowCount);
    }

    [Fact]
    public void ReplaceAndUppercase_CanBeChained()
    {
        var output = pipeline.Execute(Source(), new("Custom", [
            new(TransformOperation.Trim, "currency"),
            new(TransformOperation.Uppercase,"currency"),
            new(TransformOperation.Replace,"currency",From:"USD",To:"EUR")
        ]));
        Assert.All(output.Rows,r=>Assert.Equal("EUR",r["currency"]));
        Assert.Equal(3,output.Audit.Count);
    }

    [Fact]
    public void FillMissing_ReplacesWhitespaceValues()
    {
        var output = pipeline.Execute(Source(), new("Fill", [
            new(TransformOperation.FillMissing,"customer",To:"Unknown")
        ]));
        Assert.Equal("Unknown",output.Rows[1]["customer"]);
    }

    [Fact]
    public void SameSourceAndPipeline_YieldSameFingerprintAndDataHash()
    {
        var source=Source();
        var request = new EtlRequest("Deterministic",[
            new(TransformOperation.Trim),
            new(TransformOperation.DropDuplicates,"order_id")
        ]);
        var a=pipeline.Execute(source,request);
        var b=pipeline.Execute(source,request);
        Assert.Equal(a.DataSha256,b.DataSha256);
        Assert.Equal(a.DefinitionSha256,b.DefinitionSha256);
        Assert.Equal(a.PreviewFingerprint,b.PreviewFingerprint);
    }

    [Fact]
    public void InvalidOrUnknownColumn_IsRejectedWithoutMutatingSource()
    {
        var source=Source();
        Assert.Throws<EtlValidationException>(() =>
            pipeline.Execute(source,new("Bad",[
                new(TransformOperation.DropMissing,"missing_column")
            ])));
        Assert.Equal(4,source.Rows.Count);
    }

    [Fact]
    public void RenameCollision_IsRejected()
    {
        Assert.Throws<EtlValidationException>(() =>
            pipeline.Execute(Source(),new("Bad",[
                new(TransformOperation.RenameColumn,"amount",To:"customer")
            ])));
    }

    [Fact]
    public void EmptyOrExcessiveSteps_AreRejected()
    {
        Assert.Throws<EtlValidationException>(() =>
            pipeline.Execute(Source(),new("Bad", [])));
        var tooMany=Enumerable.Repeat(new TransformStep(TransformOperation.Trim),13).ToArray();
        Assert.Throws<EtlValidationException>(() =>
            pipeline.Execute(Source(),new("Bad",tooMany)));
    }

    [Fact]
    public void Replace_RequiresBothValues()
    {
        Assert.Throws<EtlValidationException>(() =>
            pipeline.Execute(Source(),new("Bad",[
                new(TransformOperation.Replace,"customer",From:"Ahmed")
            ])));
    }

    [Fact]
    public void UnknownEnumValue_IsRejected()
    {
        Assert.Throws<EtlValidationException>(() =>
            pipeline.Execute(Source(),new("Bad",[
                new((TransformOperation)888,"customer")
            ])));
    }

    [Fact]
    public void DataQuality_IsRecomputedAfterCleaning()
    {
        var source=Source();
        var output=pipeline.Execute(source,new("Basic",[
            new(TransformOperation.Trim)
        ]));
        var report=quality.Analyze(output.Headers,output.Rows);
        Assert.Contains(report.Issues,x=>x.Code=="DUPLICATE_ID");
        Assert.Contains(report.Issues,x=>x.Code=="MISSING_VALUE");
    }
}
