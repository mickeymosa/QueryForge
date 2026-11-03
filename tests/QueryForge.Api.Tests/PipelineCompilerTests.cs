using QueryForge.PipelineCore;
using Xunit;

namespace QueryForge.Api.Tests;

public sealed class PipelineCompilerTests
{
    private static readonly IReadOnlyDictionary<string, PipelineSource> Catalog =
        new Dictionary<string, PipelineSource>(StringComparer.Ordinal)
        {
            ["demo_orders"] = new(
                "demo_orders",
                Path.GetFullPath(Path.Combine("fixtures", "pipeline_orders.csv")),
                [
                    new("order_id", PipelineValueType.Text),
                    new("amount", PipelineValueType.Decimal),
                    new("currency", PipelineValueType.Text)
                ])
        };

    private static PipelineNode Source(string id = "source") =>
        new(id, PipelineNodeKind.Source, Source: new("demo_orders"));
    private static PipelineNode Filter(string id = "clean") =>
        new(id, PipelineNodeKind.Filter, Filter: new("amount",FilterComparison.GreaterThan,"0"));
    private static PipelineNode Aggregate(string id = "total") =>
        new(id, PipelineNodeKind.Aggregate, Aggregate:
            new("currency",AggregateFunction.Sum,"amount","total"));
    private static PipelineEdge Edge(string from,string to) => new(from,to);

    private static PipelineSpec Valid() => new(1,
        [Aggregate(), Source(), Filter()],
        [Edge("clean","total"), Edge("source","clean")], "total");

    [Fact]
    public void OrdersUnsortedDAGAndPropagatesOutputSchema()
    {
        var result = new PipelineCompiler().Compile(Valid(),Catalog);
        Assert.Equal(["source","clean","total"],result.OrderedNodeIds);
        Assert.Equal("currency",result.OutputSchema[0].Name);
        Assert.Equal("total",result.OutputSchema[1].Name);
        Assert.Equal(PipelineValueType.Decimal,result.OutputSchema[1].Type);
        Assert.Contains("SUM(",result.Sql);
        Assert.Contains("LIMIT 25",result.Sql);
        Assert.Single(result.Parameters);
        Assert.Equal(0m,result.Parameters[0].Value);
    }

    [Fact]
    public void RejectsCycles()
    {
        var spec = new PipelineSpec(1,
            [Source(),Filter(),Aggregate()],
            [Edge("source","clean"),Edge("clean","total"),Edge("total","source")],"total");
        var error = Assert.Throws<PipelineValidationException>(()=>
            new PipelineCompiler().Compile(spec,Catalog));
        Assert.Contains("cycle",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUnknownSources()
    {
        var spec = new PipelineSpec(1,
            [new("source",PipelineNodeKind.Source,Source:new("not_approved"))],[],"source");
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(spec,Catalog));
    }

    [Fact]
    public void RejectsDisconnectedNodesRatherThanIgnoringThem()
    {
        var spec = new PipelineSpec(1,[Source(),new("extra",PipelineNodeKind.Source,Source:new("demo_orders"))],
            [],"source");
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(spec,Catalog));
    }

    [Fact]
    public void RejectsInvalidEdgesAndDuplicateNodeIds()
    {
        var spec = new PipelineSpec(1,[Source(),Filter()],
            [Edge("source","unknown")],"clean");
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(spec,Catalog));
        var duplicated = new PipelineSpec(1,[Source(),Source()],[],"source");
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(duplicated,Catalog));
    }

    [Fact]
    public void RejectsWrongNodeSettingsOrInputCount()
    {
        var config = new PipelineSpec(1,[new("bad",PipelineNodeKind.Filter,Source:new("demo_orders"))],
            [],"bad");
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(config,Catalog));
        var rootFilter = new PipelineSpec(1,[Filter()],[],"clean");
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(rootFilter,Catalog));
    }

    [Fact]
    public void RejectsSchemaMismatch()
    {
        var spec = new PipelineSpec(1,[Source(),
            new("clean",PipelineNodeKind.Filter,Filter:new("missing",FilterComparison.Equals,"a"))],
            [Edge("source","clean")],"clean");
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(spec,Catalog));
    }

    [Fact]
    public void TextContainsRequiresTextColumn()
    {
        var spec = new PipelineSpec(1,[Source(),
            new("clean",PipelineNodeKind.Filter,Filter:new("amount",FilterComparison.Contains,"10"))],
            [Edge("source","clean")],"clean");
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(spec,Catalog));
    }

    [Fact]
    public void DecimalFilterRejectsInvalidValue()
    {
        var spec = new PipelineSpec(1,[Source(),
            new("clean",PipelineNodeKind.Filter,Filter:new("amount",FilterComparison.GreaterThan,"ten"))],
            [Edge("source","clean")],"clean");
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(spec,Catalog));
    }

    [Fact]
    public void SelectNodePropagatesReducedSchema()
    {
        var spec = new PipelineSpec(1,
            [Source(),new("pick",PipelineNodeKind.Select,Select:new(["currency","amount"]))],
            [Edge("source","pick")],"pick");
        var result = new PipelineCompiler().Compile(spec,Catalog);
        Assert.Equal(["currency","amount"],result.OutputSchema.Select(c=>c.Name));
        Assert.DoesNotContain("\"order_id\" FROM \"qf_source\"",
            result.Sql);
    }

    [Fact]
    public void FilterValuesCannotInjectSql()
    {
        const string attack = "EUR' OR 1=1 --";
        var spec = new PipelineSpec(1,[Source(),
            new("clean",PipelineNodeKind.Filter,Filter:new("currency",FilterComparison.Equals,attack))],
            [Edge("source","clean")],"clean");
        var plan = new PipelineCompiler().Compile(spec,Catalog);
        Assert.DoesNotContain(attack,plan.Sql,StringComparison.Ordinal);
        Assert.Contains("$p0",plan.Sql);
        Assert.Equal(attack,Assert.Single(plan.Parameters).Value);
    }

    [Fact]
    public void SQLIdentifiersCannotBeInjectedFromNodeNames()
    {
        var spec = new PipelineSpec(1,[
            new("x\"; DROP TABLE users;--",PipelineNodeKind.Source,Source:new("demo_orders"))],
            [],"x\"; DROP TABLE users;--");
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(spec,Catalog));
    }

    [Fact]
    public void UnknownSchemaVersionAndOversizedPreviewAreRejected()
    {
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(
            Valid() with {SchemaVersion=2},Catalog));
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(
            Valid(),Catalog,101));
    }

    [Fact]
    public void ReorderedInputDoesNotChangeCompilerOutput()
    {
        var spec=Valid();
        var sorted=spec with {Nodes=spec.Nodes.Reverse().ToArray(),
            Edges=spec.Edges.Reverse().ToArray()};
        var compiler=new PipelineCompiler();
        Assert.Equal(compiler.Compile(spec,Catalog).Sql,compiler.Compile(sorted,Catalog).Sql);
    }

    [Fact]
    public void AggregateRejectsUnknownMeasureAndNonNumericSum()
    {
        var spec = new PipelineSpec(1,
            [Source(),new("total",PipelineNodeKind.Aggregate,
                Aggregate:new("currency",AggregateFunction.Sum,"order_id","total"))],
            [Edge("source","total")],"total");
        Assert.Throws<PipelineValidationException>(()=>new PipelineCompiler().Compile(spec,Catalog));
    }

    [Fact]
    public void CountAggregateRequiresNoMeasure()
    {
        var spec = new PipelineSpec(1,
            [Source(),new("total",PipelineNodeKind.Aggregate,
                Aggregate:new("currency",AggregateFunction.Count,null,"records"))],
            [Edge("source","total")],"total");
        var plan=new PipelineCompiler().Compile(spec,Catalog);
        Assert.Contains("COUNT(*)",plan.Sql);
    }
}
