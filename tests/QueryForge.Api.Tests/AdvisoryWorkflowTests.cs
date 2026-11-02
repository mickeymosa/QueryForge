using QueryForge.Api;
using System.Text.Json;
using Xunit;

namespace QueryForge.Api.Tests;

public sealed class AdvisoryWorkflowTests
{
    private readonly IQualityProfiler profiler = new DeterministicQualityProfiler();

    [Fact]
    public void PlannerPreviewsDeleteImpactWithoutChangingSource()
    {
        var headers = new[] { "order_id", "customer", "amount", "currency" };
        var rows = new List<Dictionary<string,string>>
        {
            new() {["order_id"]="A1",["customer"]="Ahmed",["amount"]="10",["currency"]="EUR"},
            new() {["order_id"]="A1",["customer"]="Nora",["amount"]="20",["currency"]="EUR"},
            new() {["order_id"]="A3",["customer"]="",["amount"]="30",["currency"]="EUR"}
        };
        var source = new DatasetEntity
        {
            Id=Guid.NewGuid(),
            Sha256=new string('f',64),
            FileName="orders.csv",
            RowCount=rows.Count,
            HeadersJson=JsonSerializer.Serialize(headers),
            RowsJson=JsonSerializer.Serialize(rows),
            ReportJson=JsonSerializer.Serialize(profiler.Analyze(headers,rows))
        };
        var snapshot = source.RowsJson;
        var service = new DeterministicDataInsightsService();
        var workflow = new ReadOnlyEtlAdvisor(service,profiler,new EtlPipeline())
            .PlanAndPreview(source);
        Assert.False(workflow.UsesExternalAi);
        Assert.True(workflow.HumanApprovalRequired);
        Assert.Equal(2,workflow.ReadOnlyPreviews.Count);
        Assert.All(workflow.ReadOnlyPreviews,proposal=>
        {
            Assert.Equal(3,proposal.RowsBefore);
            Assert.Equal(2,proposal.RowsAfter);
            Assert.Equal(1,proposal.RowsRemoved);
            Assert.NotEmpty(proposal.PreviewFingerprint);
        });
        Assert.Equal(snapshot,source.RowsJson);
    }

    [Fact]
    public void MixedCurrencyRequiresHumanDecisionRatherThanInventedConversion()
    {
        var headers=new[]{"id","amount","currency"};
        var rows=new List<Dictionary<string,string>>
        {
            new(){["id"]="1",["amount"]="10",["currency"]="EUR"},
            new(){["id"]="2",["amount"]="20",["currency"]="USD"}
        };
        var source = new DatasetEntity
        {
            Id=Guid.NewGuid(), FileName="currency.csv", RowCount=2,
            Sha256=new string('c',64),
            HeadersJson=JsonSerializer.Serialize(headers),
            RowsJson=JsonSerializer.Serialize(rows),
            ReportJson=JsonSerializer.Serialize(profiler.Analyze(headers,rows))
        };
        var report = new ReadOnlyEtlAdvisor(
            new DeterministicDataInsightsService(),profiler,new EtlPipeline())
            .PlanAndPreview(source);
        Assert.Empty(report.ReadOnlyPreviews);
        Assert.Contains(report.SafetyNotes,n=>n.Contains("no derived dataset",StringComparison.OrdinalIgnoreCase));
    }
}
