using DocFlow.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DocFlow.Audit.Worker;

public sealed class AuditConsumer(AuditDbContext db, TimeProvider clock, ILogger<AuditConsumer> logger)
    : IConsumer<DocumentStatusChanged>
{
    public async Task Consume(ConsumeContext<DocumentStatusChanged> context)
    {
        var kafka = context.GetPayload<KafkaConsumeContext>();
        var alreadyRecorded = await db.Entries.AnyAsync(
            e => e.Partition == kafka.Partition && e.Offset == kafka.Offset,
            context.CancellationToken);
        if (alreadyRecorded)
        {
            return;
        }

        var message = context.Message;
        db.Entries.Add(new AuditEntry
        {
            DocumentId = message.DocumentId,
            FileName = message.FileName,
            Status = message.Status,
            Detail = message.Detail.Length > 500 ? message.Detail[..500] : message.Detail,
            OccurredAt = message.OccurredAt,
            RecordedAt = clock.GetUtcNow(),
            Partition = kafka.Partition,
            Offset = kafka.Offset,
        });
        await db.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation(
            "Auditoria: {DocumentId} -> {Status} ({Detail}) [partição {Partition}, offset {Offset}]",
            message.DocumentId,
            message.Status,
            message.Detail,
            kafka.Partition,
            kafka.Offset);
    }
}
