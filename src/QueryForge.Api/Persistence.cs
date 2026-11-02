using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace QueryForge.Api;

public sealed class DatasetEntity
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTimeOffset UploadedUtc { get; set; }
    public int RowCount { get; set; }
    public string HeadersJson { get; set; } = "[]";
    public string RowsJson { get; set; } = "[]";
    public string ReportJson { get; set; } = "{}";

    public string[] Headers => JsonSerializer.Deserialize<string[]>(HeadersJson) ?? [];
    public List<Dictionary<string, string>> Rows =>
        JsonSerializer.Deserialize<List<Dictionary<string, string>>>(RowsJson) ?? [];
    public QualityReport Report =>
        JsonSerializer.Deserialize<QualityReport>(ReportJson)
        ?? throw new InvalidDataException("Stored report is unreadable.");
}

public sealed class QueryForgeDb(DbContextOptions<QueryForgeDb> options) : DbContext(options)
{
    public DbSet<DatasetEntity> Datasets => Set<DatasetEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dataset = modelBuilder.Entity<DatasetEntity>();
        dataset.ToTable("datasets");
        dataset.HasKey(x => x.Id);
        dataset.Property(x => x.FileName).HasMaxLength(140).IsRequired();
        dataset.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        dataset.Property(x => x.HeadersJson).HasColumnType("text");
        dataset.Property(x => x.RowsJson).HasColumnType("text");
        dataset.Property(x => x.ReportJson).HasColumnType("text");
        dataset.HasIndex(x => x.UploadedUtc);
        dataset.Ignore(x => x.Headers);
        dataset.Ignore(x => x.Rows);
        dataset.Ignore(x => x.Report);
    }
}

public interface IDatasetRepository
{
    Task AddAsync(DatasetEntity dataset, CancellationToken token);
    Task<DatasetEntity?> GetAsync(Guid id, CancellationToken token);
    Task<List<DatasetEntity>> ListAsync(CancellationToken token);
}

public sealed class EfDatasetRepository(QueryForgeDb db) : IDatasetRepository
{
    public async Task AddAsync(DatasetEntity dataset, CancellationToken token)
    {
        db.Datasets.Add(dataset);
        await db.SaveChangesAsync(token);
    }

    public Task<DatasetEntity?> GetAsync(Guid id, CancellationToken token) =>
        db.Datasets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, token);

    public Task<List<DatasetEntity>> ListAsync(CancellationToken token) =>
        db.Datasets.AsNoTracking().OrderByDescending(x => x.UploadedUtc)
            .Take(100).ToListAsync(token);
}
