using CsvHelper;
using CsvHelper.Configuration;
using ExcelDataReader;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Text;

namespace QueryForge.Api;

/// <summary>
/// Adapts a small first-sheet XLSX into the existing, validated CSV/ETL path.
/// The converted CSV is still subject to all legacy CSV caps and validation.
/// This is intentionally not the large-file v2 pipeline.
/// </summary>
public sealed class StudioImportService(CsvImportService csvImport)
{
    public async Task<DatasetEntity> ImportAsync(IFormFile file, CancellationToken token)
    {
        string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension == ".csv")
            return await csvImport.ImportAsync(file, token);
        if (extension != ".xlsx")
            throw new InvalidDataException("Choose a .csv or .xlsx file. XLS and macro-enabled workbooks are not supported.");
        if (file.Length is <= 0 or > CsvImportService.MaxUploadBytes)
            throw new InvalidDataException("XLSX files for editable Studio workflows must be 2 MiB or smaller.");

        await using var input = file.OpenReadStream();
        // ExcelDataReader's configuration references Windows-1252 even for OpenXML.
        // Linux .NET containers need the provider explicitly registered.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using IExcelDataReader sheet = ExcelReaderFactory.CreateOpenXmlReader(input);
        if (!sheet.Read())
            throw new InvalidDataException("The first worksheet is empty; the first row must contain headers.");
        if (sheet.FieldCount is < 1 or > CsvImportService.MaxColumns)
            throw new InvalidDataException("The first worksheet must have between 1 and 40 columns.");

        var headers = Enumerable.Range(0, sheet.FieldCount)
            .Select(i => Convert.ToString(sheet.GetValue(i), CultureInfo.InvariantCulture)?.Trim() ?? "")
            .ToArray();
        if (headers.Any(string.IsNullOrWhiteSpace) ||
            headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new InvalidDataException("First-row Excel headers must be non-empty and unique.");

        // Bounded conversion: no whole-workbook DataSet; stop at 3,000 records.
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = new CsvWriter(text, new CsvConfiguration(CultureInfo.InvariantCulture)))
        {
            foreach (var h in headers) writer.WriteField(h);
            writer.NextRecord();

            int rows = 0;
            while (sheet.Read())
            {
                token.ThrowIfCancellationRequested();
                if (++rows > CsvImportService.MaxRows)
                    throw new InvalidDataException("XLSX exceeds the 3,000-row editable workspace limit.");

                for (int i = 0; i < headers.Length; i++)
                {
                    object? value = sheet.GetValue(i);
                    string cell = value is IFormattable formatted
                        ? formatted.ToString(null, CultureInfo.InvariantCulture)
                        : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                    if (cell.Length > 16_384)
                        throw new InvalidDataException("A worksheet cell exceeds 16,384 characters.");
                    writer.WriteField(cell);
                }
                writer.NextRecord();
                if (text.GetStringBuilder().Length > CsvImportService.MaxUploadBytes)
                    throw new InvalidDataException("The converted worksheet exceeds the 2 MiB editable workspace limit.");
            }
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text.ToString());
        if (bytes.Length > CsvImportService.MaxUploadBytes)
            throw new InvalidDataException("The converted worksheet exceeds the 2 MiB editable workspace limit.");
        using var converted = new MemoryStream(bytes, writable: false);
        string baseName = Path.GetFileNameWithoutExtension(file.FileName);
        var upload = new FormFile(converted, 0, bytes.Length, "file", baseName + ".csv");
        // ImportService validates duplicate names/quality, stores a new dataset, and bounds memory.
        return await csvImport.ImportAsync(upload, token);
    }
}

public static class StudioImportEndpoints
{
    public static IEndpointRouteBuilder MapStudioImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/studio/import", async (
            IFormFile file, StudioImportService service, CancellationToken ct) =>
        {
            try
            {
                var dataset = await service.ImportAsync(file, ct);
                return Results.Created($"/api/datasets/{dataset.Id}", new
                {
                    dataset.Id, dataset.FileName, dataset.Sha256,
                    dataset.UploadedUtc, dataset.RowCount, dataset.Headers,
                    quality = dataset.Report,
                    fileMode = "editable-small-data"
                });
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (CsvHelperException)
            {
                return Results.BadRequest(new { error = "CSV parsing failed. Check quotes and delimiters." });
            }
            catch (ExcelDataReader.Exceptions.ExcelReaderException)
            {
                return Results.BadRequest(new { error = "Excel workbook could not be parsed. Use an unencrypted .xlsx file." });
            }
        }).DisableAntiforgery(); // LOCAL-ONLY: authentication/CSRF needed before external hosting.

        return app;
    }
}
