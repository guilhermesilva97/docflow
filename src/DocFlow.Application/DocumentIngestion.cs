using DocFlow.Contracts;
using DocFlow.Domain;

namespace DocFlow.Application;

public sealed class DocumentIngestion(
    IDocumentStorage storage,
    IDocumentRepository repository,
    IMessagePublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<Guid> ReceiveAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        var blobName = $"{clock.GetUtcNow():yyyy/MM/dd}/{id}.pdf";

        await storage.SaveAsync(blobName, content, cancellationToken);

        var document = Document.Receive(id, Path.GetFileName(fileName), blobName, clock.GetUtcNow());
        repository.Add(document);

        await publisher.PublishAsync(new DocumentReceived(id, blobName, document.FileName), cancellationToken);
        await publisher.PublishAsync(document.ToStatusChanged(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return id;
    }
}
