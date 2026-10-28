using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DocFlow.Audit.Worker;

public sealed class AuditEntry
{
    public long Id { get; set; }
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public int Partition { get; set; }
    public long Offset { get; set; }
}

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entry = modelBuilder.Entity<AuditEntry>();
        entry.ToTable("AuditEntries");
        entry.Property(e => e.FileName).HasMaxLength(260);
        entry.Property(e => e.Status).HasMaxLength(20);
        entry.Property(e => e.Detail).HasMaxLength(500);
        entry.HasIndex(e => new { e.Partition, e.Offset }).IsUnique();
        entry.HasIndex(e => e.DocumentId);
    }
}

public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlServer("Server=localhost;Database=DocFlowAudit;TrustServerCertificate=True")
            .Options);
}
