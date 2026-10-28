using DocFlow.Domain;
using DocFlow.Infrastructure.Messaging;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;

namespace DocFlow.Infrastructure.Persistence;

public sealed class DocFlowDbContext(DbContextOptions<DocFlowDbContext> options) : SagaDbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    protected override IEnumerable<ISagaClassMap> Configurations => [new DocumentStateMap()];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
        modelBuilder.ApplyConfiguration(new DocumentConfiguration());
    }
}
