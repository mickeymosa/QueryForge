using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QueryForge.Api;

public sealed class CsvImportService(IQualityProfiler profiler, IDatasetRepository repository)
{
    public const long MaxUploadBytes = 1_048_576;
    public const int MaxRows = 3_000;
    public const int MaxColumns = 40;

    public async Task<DatasetEntity> ImportAsync(IFormFile file, CancellationToken token)
    {
        if (file.Length is <= 0 or > MaxUploadBytes)
            throw new InvalidDataException("CSV must be between 1 byte and 1 MiB.");
        if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only .csv files are supported.");

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, token);
        if (buffer.Length > MaxUploadBytes)
            throw new InvalidDataException("CSV exceeds maximum upload size.");
        byte[] bytes = buffer.ToArray();
        string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        // UTF-8 BOM is supported; invalid byte sequences are rejected.
        using var reader = new StreamReader(new MemoryStream(bytes),
            new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.None // Preserve source values; ETL trimming is explicit and previewed.
        });
        if (!await csv.ReadAsync())
            throw new InvalidDataException("CSV is empty.");

        csv.ReadHeader();
        string[] headers = csv.HeaderRecord ?? [];
        if (headers.Length == 0 || headers.Length > MaxColumns ||
            headers.Any(string.IsNullOrWhiteSpace) ||
            headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new InvalidDataException(
                "CSV must have 1–40 non-empty, unique column headers.");

        var rows = new List<Dictionary<string, string>>();
        while (await csv.ReadAsync())
        {
            token.ThrowIfCancellationRequested();
            if (rows.Count >= MaxRows)
                throw new InvalidDataException("CSV exceeds the 3,000 row limit.");

            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int c = 0; c < headers.Length; c++)
                row.Add(headers[c], csv.GetField(c) ?? "");
            rows.Add(row);
        }

        var report = profiler.Analyze(headers, rows);
        // File name is metadata only; never use it as a disk path.
        string name = Regex.Replace(file.FileName.Replace('\\', '/').Split('/').Last(),
            "[^a-zA-Z0-9._-]", "_");
        if (name.Length > 140) name = name[^140..];

        var dataset = new DatasetEntity
        {
            Id = Guid.NewGuid(),
            FileName = name,
            Sha256 = sha,
            UploadedUtc = DateTimeOffset.UtcNow,
            RowCount = rows.Count,
            HeadersJson = JsonSerializer.Serialize(headers),
            RowsJson = JsonSerializer.Serialize(rows),
            ReportJson = JsonSerializer.Serialize(report)
        };
        await repository.AddAsync(dataset, token);
        return dataset;
    }
}
