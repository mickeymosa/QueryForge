namespace QueryForge.Api;

public static class PivotEndpoints
{
    public static IEndpointRouteBuilder MapPivotEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/datasets/{id:guid}/pivot/preview", async (
            Guid id, PivotRequest request, IDatasetRepository datasets,
            PivotService pivots, CancellationToken ct) =>
        {
            var source = await datasets.GetAsync(id, ct);
            if (source is null) return Results.NotFound();
            try
            {
                return Results.Ok(pivots.Build(source, request));
            }
            catch (PivotValidationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
            catch (OverflowException)
            {
                return Results.BadRequest(new
                {
                    error = "Numeric values exceed the supported decimal aggregation range."
                });
            }
        });
        return app;
    }
}
