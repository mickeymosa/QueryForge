using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using QueryForge.Api;
using Xunit;

namespace QueryForge.Api.Tests;

public sealed class StudioAiProviderTests
{
    private sealed class FakeRepository(DatasetEntity source) : IDatasetRepository
    {
        public int Saves { get; private set; }
        public Task AddAsync(DatasetEntity dataset, CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }
        public Task<DatasetEntity?> GetAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<DatasetEntity?>(id == source.Id ? source : null);
        public Task<List<DatasetEntity>> ListAsync(CancellationToken ct) =>
            Task.FromResult(new List<DatasetEntity> { source });
    }

    private sealed class FakeHttpHandler(string responseBody) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Bearer { get; private set; }
        public string? Body { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Url = request.RequestUri?.ToString();
            Bearer = request.Headers.Authorization?.Parameter;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private static DatasetEntity Source() => new()
    {
        Id = Guid.NewGuid(), Sha256 = new string('d',64), FileName = "example.csv",
        RowCount = 1, HeadersJson = JsonSerializer.Serialize(new[]{"order_id","amount"}),
        RowsJson = JsonSerializer.Serialize(new[]{
            new Dictionary<string,string> { ["order_id"]="SECRET_CUSTOMER",["amount"]="20" }
        }), ReportJson = "{}"
    };

    private static StudioAiPlanner Planner(DatasetEntity source, FakeHttpHandler handler,
        FakeRepository repository, string? key = "unit-test-api-key")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string,string?>
            {
                ["Ai:Provider"]="openai",
                ["Ai:ApiKey"]=key,
                ["Ai:Model"]="gpt-4.1-mini"
            }).Build();
        return new StudioAiPlanner(new HttpClient(handler),config,repository,new EtlPipeline());
    }

    [Fact]
    public async Task HostedProviderSendsSchemaAndPromptNotSourceRowsAndNeverSaves()
    {
        var source=Source();
        string answer=JsonSerializer.Serialize(new {
            choices=new[]{new {message=new {content=
                "{\"steps\":[{\"operation\":\"Trim\",\"column\":null,\"from\":null,\"to\":null}]}"}}}
        });
        var handler=new FakeHttpHandler(answer);
        var repo=new FakeRepository(source);
        var planner=Planner(source,handler,repo);
        Assert.True(planner.IsConfigured);
        Assert.Equal("hosted-openai",planner.Mode);
        var result=await planner.ProposeAsync(new StudioPrompt(source.Id,"trim spaces"),
            CancellationToken.None);
        var fields=JsonSerializer.Serialize(result);
        Assert.Contains("requiresApproval",fields);
        Assert.Contains("true",fields);
        Assert.Contains("https://api.openai.com/v1/chat/completions",handler.Url);
        Assert.Equal("unit-test-api-key",handler.Bearer);
        Assert.Contains("order_id",handler.Body);
        Assert.Contains("trim spaces",handler.Body);
        Assert.DoesNotContain("SECRET_CUSTOMER",handler.Body);
        Assert.Equal(0,repo.Saves);
        Assert.Equal(1,handler.Calls);
    }

    [Fact]
    public async Task InvalidAiColumnIsRejectedWithoutSavingAnyData()
    {
        var source=Source();
        string answer=JsonSerializer.Serialize(new {
            choices=new[]{new {message=new {content=
                "{\"steps\":[{\"operation\":\"DropMissing\",\"column\":\"not_a_column\"}]}"}}}
        });
        var handler=new FakeHttpHandler(answer);
        var repo=new FakeRepository(source);
        var planner=Planner(source,handler,repo);
        await Assert.ThrowsAsync<AiPlanValidationException>(()=>
            planner.ProposeAsync(new StudioPrompt(source.Id,"remove missing fields"),
                CancellationToken.None));
        Assert.Equal(0,repo.Saves);
    }

    [Fact]
    public void NoHostedKeyMeansProviderIsDisabled()
    {
        var source=Source();
        var planner=Planner(source,new FakeHttpHandler("{}"),new FakeRepository(source),"");
        Assert.False(planner.IsConfigured);
        Assert.Equal("not-configured",planner.Mode);
    }
}
