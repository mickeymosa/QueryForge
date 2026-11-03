using DuckDB.NET.Data;
using QueryForge.PipelineCore;

namespace QueryForge.Api;

public sealed record DemoPipelinePreview(
    IReadOnlyList<PipelineColumn> Columns,
    IReadOnlyList<string> OrderedNodeIds,
    int RowCountReturned,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    string ExecutionEngine,
    bool SourceIsSynthetic);

// Restricted proof of execution: the only allowed dataset is a shipped synthetic sample.
// This is NOT an arbitrary SQL endpoint and is NOT a public multi-tenant query service.
public sealed class DemoPipelineService
{
    private readonly PipelineCompiler compiler = new();
    private readonly IReadOnlyDictionary<string, PipelineSource> approved;
    private static readonly SemaphoreSlim ExecutionSlot = new(1, 1);

    public DemoPipelineService(IWebHostEnvironment environment)
    {
        string sample = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath, "wwwroot", "samples", "pipeline_orders.csv"));
        approved = new Dictionary<string, PipelineSource>(StringComparer.Ordinal)
        {
            ["demo_orders"] = new(
                "demo_orders", sample,
                [
                    new("order_id", PipelineValueType.Text),
                    new("customer", PipelineValueType.Text),
                    new("amount", PipelineValueType.Decimal),
                    new("currency", PipelineValueType.Text),
                    new("paid", PipelineValueType.Text)
                ])
        };
    }

    public CompiledPipeline Compile(PipelineSpec spec, int limit = 25) =>
        compiler.Compile(spec, approved, limit);

    public DemoPipelinePreview Preview(PipelineSpec spec, int limit = 25)
    {
        var plan = Compile(spec, limit);
        if (!ExecutionSlot.Wait(0))
            throw new ResourceBusyException();
        try
        {
            // A dedicated execution worker with OS-level limits is still necessary
            // before executing pipelines against untrusted or user-uploaded data.
            using var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();
            using (var settings = connection.CreateCommand())
            {
                settings.CommandText = "SET memory_limit='128MB'";
                settings.ExecuteNonQuery();
                settings.CommandText = "SET threads=1";
                settings.ExecuteNonQuery();
            }
            using var command = connection.CreateCommand();
            command.CommandText = plan.Sql;
            foreach (var parameter in plan.Parameters)
                command.Parameters.Add(new DuckDBParameter(parameter.Name, parameter.Value));
            using var reader = command.ExecuteReader();
            var rows = new List<Dictionary<string, object?>>();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (int i = 0; i < reader.FieldCount; i++)
                    row.Add(reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i));
                rows.Add(row);
                if (rows.Count > plan.PreviewLimit)
                    throw new InvalidOperationException("Query exceeded compiled preview limit.");
            }
            return new(plan.OutputSchema, plan.OrderedNodeIds, rows.Count, rows,
                "DuckDB.NET demo (in-process, bounded preview)", true);
        }
        finally
        {
            ExecutionSlot.Release();
        }
    }
}

public static class PipelineDemoEndpoints
{
    public static IEndpointRouteBuilder MapPipelineDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/pipelines/compile", (PipelineSpec spec, int? limit, DemoPipelineService runner) =>
        {
            try
            {
                var result = runner.Compile(spec, limit ?? 25);
                // Compiler output is for technical inspection only.
                // Production endpoints must not expose internal filesystem paths.
                return Results.Ok(new
                {
                    outputSchema = result.OutputSchema,
                    orderedNodeIds = result.OrderedNodeIds,
                    result.PreviewLimit,
                    parameterCount = result.Parameters.Count,
                    note = "Compilation only; no dataset was read or modified."
                });
            }
            catch (PipelineValidationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });

        app.MapPost("/api/pipelines/demo/preview", (PipelineSpec spec, int? limit, DemoPipelineService runner) =>
        {
            try
            {
                return Results.Ok(runner.Preview(spec, limit ?? 25));
            }
            catch (ResourceBusyException error)
            {
                return Results.Json(new { error = error.Message }, statusCode: 429);
            }
            catch (PipelineValidationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });
        return app;
    }
}
