using DocFlow.Domain;
using Microsoft.Extensions.Options;

namespace DocFlow.Application;

public sealed class DocumentWorkflow(
    IDocumentRepository repository,
    IMessagePublisher publisher,
    IOptions<WorkflowOptions> options,
    TimeProvider clock)
{
    public Task StartExtractionAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, d => d.StartExtraction(clock.GetUtcNow()), cancellationToken);

    public Task RecordExtractionAsync(Guid id, ExtractionResult result, CancellationToken cancellationToken) =>
        ChangeAsync(
            id,
            d =>
            {
                d.RecordExtraction(result, clock.GetUtcNow());
                d.StartValidation(clock.GetUtcNow());
            },
            cancellationToken);

    public Task FailExtractionAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        ChangeAsync(id, d => d.FailExtraction(reason, clock.GetUtcNow()), cancellationToken);

    public async Task<DocumentStatus> CompleteValidationAsync(
        Guid id,
        IReadOnlyList<ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        var document = await LoadAsync(id, cancellationToken);
        var isDuplicate = document.DuplicateKey is { } key
            && await repository.ExistsApprovedWithKeyAsync(key, id, cancellationToken);

        document.CompleteValidation(issues, options.Value.ConfidenceThreshold, isDuplicate, clock.GetUtcNow());
        await publisher.PublishAsync(document.ToStatusChanged(), cancellationToken);
        return document.Status;
    }

    private async Task ChangeAsync(Guid id, Action<Document> change, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(id, cancellationToken);
        change(document);
        await publisher.PublishAsync(document.ToStatusChanged(), cancellationToken);
    }

    private async Task<Document> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await repository.GetAsync(id, cancellationToken) ?? throw new DocumentNotFoundException(id);
}
