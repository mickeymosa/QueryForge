using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace QueryForge.Api;

// Bounded v2 ingest: small batch in RAM, one row per relational record.
// The legacy JSON dataset remains read-only/size-restricted until converted.
public sealed record StreamDatasetSummary(
    Guid Id, string FileName, string Sha256, int RowCount,
    IReadOnlyList<string> Headers, IReadOnlyList<ColumnProfile> Columns,
    int MissingValues, int DuplicateIds, DateTimeOffset UploadedUtc);

public sealed record StreamDatasetPage(
    Guid DatasetId, int Offset, int Limit, int TotalRows,
    IReadOnlyList<Dictionary<string,string>> Rows,
    bool HasMore);

public sealed class ResourceBusyException() : Exception("Import capacity is busy. Retry after the active import finishes.");

public sealed class StreamDatasetService(NpgsqlDataSource dataSource)
{
    public const long MaxFileBytes = 25L * 1024 * 1024;
    public const int MaxDataRows = 30_000;
    public const int MaxColumns = 80;
    private const int BatchSize = 200;
    private const int MaxCellChars = 16_384;
    private const int MaxRowChars = 65_536;
    private static readonly SemaphoreSlim ImportSlots = new(1, 1);

    // Compatibility migration: works against existing v0.4 volumes. Explicit,
    // versioned EF migrations are still a mandatory subsequent release gate.
    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using var db = await dataSource.OpenConnectionAsync(ct);
        const string ddl = """
            CREATE TABLE IF NOT EXISTS stream_datasets (
                id uuid PRIMARY KEY,
                file_name varchar(140) NOT NULL,
                sha256 char(64) NOT NULL,
                uploaded_utc timestamptz NOT NULL,
                row_count integer NOT NULL CHECK (row_count >= 0),
                headers_json jsonb NOT NULL,
                profile_json jsonb NOT NULL
            );
            CREATE TABLE IF NOT EXISTS stream_dataset_rows (
                dataset_id uuid NOT NULL REFERENCES stream_datasets(id) ON DELETE CASCADE,
                row_number integer NOT NULL CHECK (row_number > 0),
                values_json jsonb NOT NULL,
                PRIMARY KEY (dataset_id, row_number)
            );
            CREATE INDEX IF NOT EXISTS ix_stream_dataset_uploaded
                ON stream_datasets(uploaded_utc DESC);
            """;
        await using var cmd = new NpgsqlCommand(ddl, db);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<StreamDatasetSummary> ImportAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length is <= 0 or > MaxFileBytes)
            throw new InvalidDataException("CSV must be between 1 byte and 25 MiB.");
        if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only .csv files are accepted.");
        // Fail fast instead of accepting unbounded queued imports.
        if (!await ImportSlots.WaitAsync(0, ct)) throw new ResourceBusyException();
        try
        {
            return await ImportReservedAsync(file, ct);
        }
        finally
        {
            ImportSlots.Release();
        }
    }

    private async Task<StreamDatasetSummary> ImportReservedAsync(IFormFile file, CancellationToken ct)
    {
        // SHA-256 is computed in streaming fashion; OpenReadStream is reopened for CSV parsing.
        string sha;
        await using (var hashStream = file.OpenReadStream())
        {
            sha = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, ct))
                .ToLowerInvariant();
        }

        await using var db = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await db.BeginTransactionAsync(ct);
        var id = Guid.NewGuid();
        string fileName = Regex.Replace(file.FileName.Replace('\\','/').Split('/').Last(),
            "[^a-zA-Z0-9._-]", "_");
        if (fileName.Length > 140) fileName = fileName[^140..];
        DateTimeOffset uploaded = DateTimeOffset.UtcNow;

        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.None,
            IgnoreBlankLines = true
        });
        if (!await csv.ReadAsync()) throw new InvalidDataException("CSV has no header.");
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];
        if (headers.Length is < 1 or > MaxColumns ||
            headers.Any(string.IsNullOrWhiteSpace) ||
            headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new InvalidDataException("CSV headers must be unique and between 1 and 80 columns.");

        // Insert metadata first so child rows have a valid foreign key.
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO stream_datasets
              (id,file_name,sha256,uploaded_utc,row_count,headers_json,profile_json)
            VALUES (@id,@name,@sha,@uploaded,0,@headers,'{}'::jsonb)
            """, db, transaction))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("name", fileName);
            insert.Parameters.AddWithValue("sha", sha);
            insert.Parameters.AddWithValue("uploaded", uploaded);
            insert.Parameters.AddWithValue("headers", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(headers));
            await insert.ExecuteNonQueryAsync(ct);
        }

        var filled = new int[headers.Length];
        var missing = new int[headers.Length];
        string? idColumn = headers.FirstOrDefault(h =>
            h.Equals("id",StringComparison.OrdinalIgnoreCase) ||
            h.Equals("order_id",StringComparison.OrdinalIgnoreCase) ||
            h.Equals("orderid",StringComparison.OrdinalIgnoreCase));
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int duplicates = 0;
        int count = 0;
        var batch = new List<(int Row, string ValuesJson)>(BatchSize);
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            if (++count > MaxDataRows) throw new InvalidDataException("CSV exceeds 30,000 data rows.");

            var actualFields = csv.Parser.Record;
            if (actualFields is null || actualFields.Length != headers.Length)
                throw new InvalidDataException($"CSV record {count} has the wrong field count.");

            var row = new Dictionary<string,string>(headers.Length,StringComparer.Ordinal);
            int rowCharacters = 0;
            for (int column = 0; column < headers.Length; column++)
            {
                string cell = csv.GetField(column) ?? "";
                if (cell.Length > MaxCellChars)
                    throw new InvalidDataException($"CSV record {count} contains a cell exceeding 16,384 characters.");
                rowCharacters += cell.Length;
                if (rowCharacters > MaxRowChars)
                    throw new InvalidDataException($"CSV record {count} exceeds the 65,536-character row limit.");
                row[headers[column]] = cell;
                if (string.IsNullOrWhiteSpace(cell)) missing[column]++;
                else filled[column]++;
            }
            if (idColumn is not null)
            {
                string key = row[idColumn].Trim();
                // Fixed-width digest keeps duplicate detection bounded even for long keys.
                if (key.Length > 0)
                {
                    var digest = Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(key.ToUpperInvariant())));
                    if (!seenIds.Add(digest)) duplicates++;
                }
            }
            batch.Add((count, JsonSerializer.Serialize(row)));
            if (batch.Count == BatchSize)
            {
                await WriteBatchAsync(db, transaction, id, batch, ct);
                batch.Clear();
            }
        }
        if (batch.Count > 0) await WriteBatchAsync(db, transaction, id, batch, ct);

        var profiles = headers.Select((h, i) =>
            new ColumnProfile(h, filled[i], missing[i])).ToArray();
        await using (var complete = new NpgsqlCommand("""
            UPDATE stream_datasets SET row_count=@count,profile_json=@profile WHERE id=@id
            """, db, transaction))
        {
            complete.Parameters.AddWithValue("count", count);
            complete.Parameters.AddWithValue("profile", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(new { columns = profiles, duplicateIds = duplicates }));
            complete.Parameters.AddWithValue("id", id);
            await complete.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new(id,fileName,sha,count,headers,profiles,missing.Sum(),duplicates,uploaded);
    }

    private static async Task WriteBatchAsync(
        NpgsqlConnection db, NpgsqlTransaction tx, Guid id,
        List<(int Row, string ValuesJson)> rows, CancellationToken ct)
    {
        // Command batches constrain object count and bound each DB transaction operation.
        using var batch = new NpgsqlBatch(db, tx);
        foreach (var item in rows)
        {
            var cmd = new NpgsqlBatchCommand("""
                INSERT INTO stream_dataset_rows(dataset_id,row_number,values_json)
                VALUES (@id,@row,@values)
                """);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("row", item.Row);
            cmd.Parameters.AddWithValue("values", NpgsqlDbType.Jsonb, item.ValuesJson);
            batch.BatchCommands.Add(cmd);
        }
        await batch.ExecuteNonQueryAsync(ct);
    }

    public async Task<StreamDatasetSummary?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT file_name,sha256,uploaded_utc,row_count,headers_json::text,profile_json::text
            FROM stream_datasets WHERE id=@id
            """, db);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var headers = JsonSerializer.Deserialize<string[]>(reader.GetString(4)) ?? [];
        using var profile = JsonDocument.Parse(reader.GetString(5));
        var root = profile.RootElement;
        var columns = root.TryGetProperty("columns", out var cols)
            ? JsonSerializer.Deserialize<ColumnProfile[]>(cols.GetRawText()) ?? [] : [];
        int duplicates = root.TryGetProperty("duplicateIds", out var dup) ? dup.GetInt32() : 0;
        return new(id,reader.GetString(0),reader.GetString(1),
            reader.GetInt32(3),headers,columns,columns.Sum(x=>x.Missing),
            duplicates,reader.GetFieldValue<DateTimeOffset>(2));
    }

    public async Task<StreamDatasetPage?> GetPageAsync(
        Guid id,int offset,int limit,CancellationToken ct)
    {
        var summary = await GetAsync(id,ct);
        if (summary is null) return null;
        if (offset < 0 || limit is < 1 or > 100)
            throw new ArgumentException("Offset must be >= 0; limit must be 1–100.");
        var rows = new List<Dictionary<string,string>>(limit);
        await using var db = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT values_json::text FROM stream_dataset_rows
            WHERE dataset_id=@id AND row_number > @offset
            ORDER BY row_number LIMIT @limit
            """, db);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("offset", offset);
        cmd.Parameters.AddWithValue("limit", limit);
        await using var result = await cmd.ExecuteReaderAsync(ct);
        while (await result.ReadAsync(ct))
            rows.Add(JsonSerializer.Deserialize<Dictionary<string,string>>(result.GetString(0)) ?? []);
        return new(id,offset,limit,summary.RowCount,rows,
            offset + rows.Count < summary.RowCount);
    }
}
