using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocFlow.Infrastructure.Messaging;

public sealed class DocumentState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public string? LastStatus { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class DocumentStateMap : SagaClassMap<DocumentState>
{
    protected override void Configure(EntityTypeBuilder<DocumentState> entity, ModelBuilder model)
    {
        entity.ToTable("DocumentWorkflows");
        entity.Property(x => x.CurrentState).HasMaxLength(40);
        entity.Property(x => x.LastStatus).HasMaxLength(20);
        entity.Property(x => x.RowVersion).IsRowVersion();
    }
}
