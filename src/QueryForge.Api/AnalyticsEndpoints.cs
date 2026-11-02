namespace QueryForge.Api;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/datasets/{id:guid}/insights", async (
            Guid id, IDatasetRepository datasets, IDataInsightsService service, CancellationToken ct) =>
        {
            var dataset = await datasets.GetAsync(id, ct);
            return dataset is null ? Results.NotFound() : Results.Ok(service.GetInsights(dataset));
        });

        app.MapPost("/api/datasets/{id:guid}/charts/preview", async (
            Guid id, ChartRequest request, IDatasetRepository datasets,
            IDataInsightsService service, CancellationToken ct) =>
        {
            var dataset = await datasets.GetAsync(id, ct);
            if (dataset is null) return Results.NotFound();
            try
            {
                return Results.Ok(service.BuildChart(dataset, request));
            }
            catch (ChartValidationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (OverflowException)
            {
                return Results.BadRequest(new
                {
                    error = "These numeric values exceed the supported decimal aggregation range."
                });
            }
        });

        // A read-only, deterministic advisor: no LLM connection or auto-executed ETL.
        app.MapGet("/api/datasets/{id:guid}/advisor/report", async (
            Guid id, IDatasetRepository datasets, IDataInsightsService service, CancellationToken ct) =>
        {
            var dataset = await datasets.GetAsync(id, ct);
            return dataset is null ? Results.NotFound() :
                Results.Ok(service.BuildAdvisoryReport(dataset));
        });
        return app;
    }
}
