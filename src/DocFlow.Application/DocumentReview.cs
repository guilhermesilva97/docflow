using DocFlow.Contracts;
using DocFlow.Domain;

namespace DocFlow.Application;

public sealed class DocumentReview(
    IDocumentRepository repository,
    IMessagePublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<Document> ApproveAsync(
        Guid id,
        InvoiceData reviewed,
        string reviewer,
        CancellationToken cancellationToken)
    {
        var document = await LoadAsync(id, cancellationToken);
        var isDuplicate = reviewed.DuplicateKey is { } key
            && await repository.ExistsApprovedWithKeyAsync(key, id, cancellationToken);

        var now = clock.GetUtcNow();
        document.Approve(reviewed, reviewer, DateOnly.FromDateTime(now.UtcDateTime), isDuplicate, now);
        await PublishAndSaveAsync(document, cancellationToken);
        return document;
    }

    public async Task<Document> RejectAsync(Guid id, string reason, string reviewer, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(id, cancellationToken);
        document.Reject(reason, reviewer, clock.GetUtcNow());
        await PublishAndSaveAsync(document, cancellationToken);
        return document;
    }

    private async Task PublishAndSaveAsync(Document document, CancellationToken cancellationToken)
    {
        await publisher.PublishAsync(document.ToStatusChanged(), cancellationToken);
        await publisher.PublishAsync(new DocumentReviewed(document.Id, document.Status.ToString()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Document> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await repository.GetAsync(id, cancellationToken) ?? throw new DocumentNotFoundException(id);
}
