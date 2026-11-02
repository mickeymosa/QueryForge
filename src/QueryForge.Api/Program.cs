using CsvHelper;
using Microsoft.EntityFrameworkCore;
using QueryForge.Api;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
// Form body is slightly larger than the file itself because multipart adds headers.
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = CsvImportService.MaxUploadBytes);
builder.Services.AddSingleton<IQualityProfiler, DeterministicQualityProfiler>();
builder.Services.AddSingleton<EtlPipeline>();
builder.Services.AddSingleton<IDataInsightsService, DeterministicDataInsightsService>();
builder.Services.AddSingleton<ReadOnlyEtlAdvisor>();
builder.Services.AddScoped<IDatasetRepository, EfDatasetRepository>();
builder.Services.AddScoped<CsvImportService>();
// One pooled data source per API process; small bounded batches use short-lived connections.
builder.Services.AddSingleton(NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("QueryForge")
    ?? throw new InvalidOperationException("ConnectionStrings:QueryForge is required.")));
builder.Services.AddSingleton<StreamDatasetService>();
builder.Services.AddDbContext<QueryForgeDb>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("QueryForge")
        ?? throw new InvalidOperationException("ConnectionStrings:QueryForge is required.")));

// Reject large requests at the web-server boundary, before model binding.
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = CsvImportService.MaxUploadBytes + 2 * 1024 * 1024);

var app = builder.Build();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

// Demo-only bootstrap. Replace with EF Core migrations before a production release.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QueryForgeDb>();
    await db.Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<StreamDatasetService>()
        .EnsureSchemaAsync(CancellationToken.None);
}

app.MapGet("/api/health", async (QueryForgeDb db, CancellationToken token) =>
    await db.Database.CanConnectAsync(token)
        ? Results.Ok(new { status = "ok", storage = "connected" })
        : Results.Problem("Database is unavailable", statusCode: 503));

app.MapGet("/api/datasets", async (IDatasetRepository repository, CancellationToken token) =>
{
    var list = await repository.ListAsync(token);
    return Results.Ok(list.Select(x => new
    {
        x.Id, x.FileName, x.Sha256, x.UploadedUtc, x.RowCount,
        x.Report.ColumnCount, x.Report.RowsWithIssues
    }));
});

app.MapPost("/api/datasets", async (
    IFormFile file, CsvImportService importer, CancellationToken token) =>
{
    try
    {
        var dataset = await importer.ImportAsync(file, token);
        return Results.Created($"/api/datasets/{dataset.Id}", new
        {
            dataset.Id, dataset.FileName, dataset.Sha256,
            dataset.UploadedUtc, dataset.RowCount, dataset.Headers,
            quality = dataset.Report
        });
    }
    catch (InvalidDataException error)
    {
        return Results.BadRequest(new { error = error.Message });
    }
    catch (CsvHelperException)
    {
        return Results.BadRequest(new { error = "CSV parsing failed. Check delimiters, quotes and row structure." });
    }
})
.DisableAntiforgery(); // LOCAL-ONLY demo: add auth + CSRF protection before public deployment.

app.MapGet("/api/datasets/{id:guid}", async (
    Guid id, IDatasetRepository repository, CancellationToken token) =>
{
    var dataset = await repository.GetAsync(id, token);
    return dataset is null ? Results.NotFound() : Results.Ok(new
    {
        dataset.Id, dataset.FileName, dataset.Sha256,
        dataset.UploadedUtc, dataset.RowCount, dataset.Headers,
        quality = dataset.Report
    });
});

app.MapGet("/api/datasets/{id:guid}/preview", async (
    Guid id, int? limit, IDatasetRepository repository, CancellationToken token) =>
{
    var dataset = await repository.GetAsync(id, token);
    if (dataset is null) return Results.NotFound();

    int take = Math.Clamp(limit ?? 10, 1, 50);
    return Results.Ok(new { headers = dataset.Headers, rows = dataset.Rows.Take(take) });
});

app.MapGet("/api/datasets/{id:guid}/metrics", async (
    Guid id, string? column, IDatasetRepository repository,
    IQualityProfiler profiler, CancellationToken token) =>
{
    var dataset = await repository.GetAsync(id, token);
    if (dataset is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(column) || !dataset.Headers.Contains(column, StringComparer.Ordinal))
        return Results.BadRequest(new { error = "Specify an exact existing column name." });

    try
    {
        var metric = profiler.Summarize(column, dataset.Headers, dataset.Rows, dataset.Report);
        return Results.Ok(new
        {
            metric,
            dataset.Id,
            dataset.Sha256,
            explanation = "Sum and average include parseable numeric values only from rows without quality issues. Excluded rows include nonnumeric values and every flagged row. No AI or SQL is used."
        });
    }
    catch (OverflowException)
    {
        return Results.BadRequest(new { error = "Numeric values exceed safe aggregation range." });
    }
});

// ETL is optional; users must preview an explicit, allowlisted transformation plan.
app.MapEtlEndpoints();
app.MapAnalyticsEndpoints();
app.MapAdvisoryWorkflowEndpoints();
app.MapStreamDatasetEndpoints();

app.Run();

// Tests of web endpoints can reference this entry point in a future sprint.
public partial class Program { }
