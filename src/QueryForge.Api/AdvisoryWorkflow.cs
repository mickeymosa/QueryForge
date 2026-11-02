namespace QueryForge.Api;

public sealed record AdvisoryStepPreview(
    string Title,
    string Reason,
    string Risk,
    int RowsBefore,
    int RowsAfter,
    int RowsRemoved,
    int IssuesBefore,
    int IssuesAfter,
    string PreviewFingerprint,
    IReadOnlyList<TransformStep> Steps);

public sealed record AdvisoryWorkflow(
    Guid DatasetId,
    string DatasetSha256,
    string Planner,
    bool UsesExternalAi,
    bool HumanApprovalRequired,
    IReadOnlyList<AdvisoryStepPreview> ReadOnlyPreviews,
    IReadOnlyList<string> SafetyNotes);

// A safe orchestration boundary: read -> recommend -> preview -> require human approval.
// A future model provider may PROPOSE operations but must never directly execute/apply them.
public sealed class ReadOnlyEtlAdvisor(
    IDataInsightsService insights,
    IQualityProfiler profiler,
    EtlPipeline pipeline)
{
    public AdvisoryWorkflow PlanAndPreview(DatasetEntity source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var report = insights.BuildAdvisoryReport(source);
        var plans = new List<AdvisoryStepPreview>();
        foreach (var suggestion in report.SuggestedTransformations)
        {
            // Some recommendations (e.g. mixed currencies) require a human decision,
            // so no automatic recipe is proposed.
            if (suggestion.Steps.Count == 0) continue;

            var request = new EtlRequest(
                "Advisor suggestion: " + suggestion.Title, suggestion.Steps);
            var execution = pipeline.Execute(source, request);
            var after = profiler.Analyze(execution.Headers, execution.Rows);
            plans.Add(new(
                suggestion.Title,
                suggestion.Reason,
                suggestion.Risk,
                source.RowCount,
                execution.Rows.Count,
                source.RowCount - execution.Rows.Count,
                source.Report.Issues.Count,
                after.Issues.Count,
                execution.PreviewFingerprint,
                suggestion.Steps));
        }
        return new(source.Id, source.Sha256,
            "QueryForge deterministic read-only workflow planner",
            false, true, plans,
            [
                "No transformation was applied and no derived dataset was stored.",
                "Each preview is evaluated separately against the original source, not as one combined pipeline.",
                "Dropped-row counts must be reviewed; fewer issues after deletion does not necessarily mean better source data.",
                "AI models may eventually propose allowlisted plans, but the model integration is not connected in v0.4.",
                "The user must configure, preview and approve ETL in Data Studio before saving any derived dataset."
            ]);
    }
}

public static class AdvisoryWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapAdvisoryWorkflowEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/datasets/{id:guid}/advisor/workflow", async (
            Guid id,
            IDatasetRepository repository,
            ReadOnlyEtlAdvisor planner,
            CancellationToken token) =>
        {
            var source = await repository.GetAsync(id, token);
            if (source is null) return Results.NotFound();
            try
            {
                return Results.Ok(planner.PlanAndPreview(source));
            }
            catch (EtlValidationException e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        });
        return app;
    }
}
