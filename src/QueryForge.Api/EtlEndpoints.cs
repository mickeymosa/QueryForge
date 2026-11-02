using System.Text.Json;

namespace QueryForge.Api;

public static class EtlEndpoints
{
    public static IEndpointRouteBuilder MapEtlEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/datasets/{id:guid}/etl/preview", async (
            Guid id, EtlRequest request, IDatasetRepository repository,
            EtlPipeline pipeline, IQualityProfiler quality, CancellationToken token) =>
        {
            var dataset = await repository.GetAsync(id, token);
            if (dataset is null) return Results.NotFound();

            try
            {
                var result = pipeline.Execute(dataset, request);
                var after = quality.Analyze(result.Headers, result.Rows);
                return Results.Ok(new
                {
                    sourceDatasetId = dataset.Id,
                    sourceSha256 = dataset.Sha256,
                    originalQuality = dataset.Report,
                    proposedQuality = after,
                    result.Headers,
                    rowCountBefore = dataset.RowCount,
                    rowCountAfter = result.Rows.Count,
                    result.Audit,
                    result.DefinitionSha256,
                    result.DataSha256,
                    result.PreviewFingerprint,
                    sample = result.Rows.Take(8).Select((row, i) => new
                    {
                        originalCsvRow = result.OriginalRowNumbers[i],
                        values = row
                    })
                });
            }
            catch (EtlValidationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });

        app.MapPost("/api/datasets/{id:guid}/etl/apply", async (
            Guid id, EtlRequest request, IDatasetRepository repository,
            EtlPipeline pipeline, IQualityProfiler quality, CancellationToken token) =>
        {
            var dataset = await repository.GetAsync(id, token);
            if (dataset is null) return Results.NotFound();

            try
            {
                var result = pipeline.Execute(dataset, request);
                if (string.IsNullOrWhiteSpace(request.PreviewFingerprint) ||
                    !string.Equals(result.PreviewFingerprint, request.PreviewFingerprint,
                        StringComparison.Ordinal))
                    return Results.Conflict(new
                    {
                        error = "Preview the exact transformation settings before applying them."
                    });

                var lineage = new TransformationLineage(
                    dataset.Id, dataset.Sha256, result.DefinitionSha256,
                    request.PipelineName!.Trim(), 1, DateTimeOffset.UtcNow,
                    dataset.RowCount, result.Rows.Count, request.Steps!,
                    result.Audit, result.OriginalRowNumbers);
                QualityReport report = quality.Analyze(result.Headers, result.Rows)
                    with { Lineage = lineage };

                string name = dataset.FileName.Length > 113
                    ? dataset.FileName[..113] : dataset.FileName;
                var derived = new DatasetEntity
                {
                    Id = Guid.NewGuid(),
                    FileName = name + ".transformed.csv",
                    Sha256 = result.DataSha256,
                    UploadedUtc = DateTimeOffset.UtcNow,
                    RowCount = result.Rows.Count,
                    HeadersJson = JsonSerializer.Serialize(result.Headers),
                    RowsJson = JsonSerializer.Serialize(result.Rows),
                    ReportJson = JsonSerializer.Serialize(report)
                };
                await repository.AddAsync(derived, token);
                return Results.Created($"/api/datasets/{derived.Id}", new
                {
                    derived.Id, derived.FileName, derived.Sha256,
                    derived.RowCount, derived.Headers,
                    quality = report,
                    sourceDatasetId = dataset.Id
                });
            }
            catch (EtlValidationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });

        return app;
    }
}
