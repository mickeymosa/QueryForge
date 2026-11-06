using System.Text;
using System.Text.RegularExpressions;

namespace QueryForge.Api;

/// <summary>Safe bounded CSV export for editable/saved datasets, not the v2 large-file store.</summary>
public static class DatasetCsvExport
{
    public static byte[] Render(DatasetEntity dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var buffer = new StringBuilder("\uFEFF"); // Excel-friendly UTF-8 BOM
        WriteLine(buffer, dataset.Headers);
        foreach (var row in dataset.Rows)
            WriteLine(buffer, dataset.Headers.Select(header => row.GetValueOrDefault(header, "")));
        return Encoding.UTF8.GetBytes(buffer.ToString());
    }

    public static string FileName(DatasetEntity dataset)
    {
        string name = Path.GetFileNameWithoutExtension(dataset.FileName);
        name = Regex.Replace(name, "[^A-Za-z0-9_-]", "_").Trim('_');
        if (name.Length > 64) name = name[..64];
        return (name.Length == 0 ? "queryforge_dataset" : name) + ".csv";
    }

    private static void WriteLine(StringBuilder buffer, IEnumerable<string> values)
    {
        bool first = true;
        foreach (string raw in values)
        {
            if (!first) buffer.Append(',');
            first = false;
            string value = raw ?? "";
            string trimmed = value.TrimStart(' ', '\t', '\r', '\n');
            // A spreadsheet may interpret these as executable formulas. Prefix with an
            // apostrophe before CSV-quoting; never export untrusted values as formulas.
            if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]))
                value = "'" + value;
            buffer.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
        }
        buffer.Append("\r\n");
    }
}

public static class DatasetCsvExportEndpoints
{
    public static IEndpointRouteBuilder MapDatasetCsvExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/datasets/{id:guid}/export.csv", async (
            Guid id, IDatasetRepository repository, CancellationToken ct) =>
        {
            var source = await repository.GetAsync(id, ct);
            if (source is null) return Results.NotFound();
            return Results.File(DatasetCsvExport.Render(source),
                "text/csv; charset=utf-8", DatasetCsvExport.FileName(source));
        });
        return app;
    }
}
