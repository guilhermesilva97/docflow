using DocFlow.Application;
using DocFlow.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocFlow.Infrastructure.Persistence;

public sealed class DocumentRepository(DocFlowDbContext context) : IDocumentRepository, IUnitOfWork
{
    public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        context.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Document>> ListAsync(
        DocumentStatus? status,
        int take,
        CancellationToken cancellationToken)
    {
        var query = context.Documents.AsNoTracking();
        if (status is { } filter)
        {
            query = query.Where(d => d.Status == filter);
        }

        return await query.OrderByDescending(d => d.ReceivedAt).Take(take).ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsApprovedWithKeyAsync(string duplicateKey, Guid exceptId, CancellationToken cancellationToken) =>
        context.Documents.AnyAsync(
            d => d.DuplicateKey == duplicateKey && d.Status == DocumentStatus.Approved && d.Id != exceptId,
            cancellationToken);

    public void Add(Document document) => context.Documents.Add(document);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
