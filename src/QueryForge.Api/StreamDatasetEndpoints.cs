namespace QueryForge.Api;

public static class StreamDatasetEndpoints
{
    public static IEndpointRouteBuilder MapStreamDatasetEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v2/datasets", async (
            IFormFile file, StreamDatasetService service, CancellationToken ct) =>
        {
            try
            {
                var created = await service.ImportAsync(file,ct);
                return Results.Created($"/api/v2/datasets/{created.Id}",created);
            }
            catch (ResourceBusyException e)
            {
                return Results.Json(new { error=e.Message },statusCode:429,
                    headers:new Dictionary<string,string>{{"Retry-After","5"}});
            }
            catch (InvalidDataException e)
            {
                return Results.BadRequest(new { error=e.Message });
            }
            catch (CsvHelper.CsvHelperException)
            {
                return Results.BadRequest(new { error="Malformed CSV: check quotes and field structure." });
            }
        }).DisableAntiforgery(); // Local-only prototype. Protect before public use.

        app.MapGet("/api/v2/datasets/{id:guid}", async (
            Guid id,StreamDatasetService service,CancellationToken ct) =>
            await service.GetAsync(id,ct) is { } dataset
                ? Results.Ok(dataset) : Results.NotFound());

        app.MapGet("/api/v2/datasets/{id:guid}/rows", async (
            Guid id,int? offset,int? limit,StreamDatasetService service,CancellationToken ct) =>
        {
            if (offset.GetValueOrDefault() < 0 || limit.GetValueOrDefault(25) is < 1 or > 100)
                return Results.BadRequest(new {error="Offset must be >= 0; limit must be 1–100."});
            var page=await service.GetPageAsync(id,offset ?? 0,limit ?? 25,ct);
            return page is null ? Results.NotFound() : Results.Ok(page);
        });
        return app;
    }
}
