using DocFlow.Domain;

namespace DocFlow.Application;

public interface IDocumentRepository
{
    Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Document>> ListAsync(DocumentStatus? status, int take, CancellationToken cancellationToken);

    Task<bool> ExistsApprovedWithKeyAsync(string duplicateKey, Guid exceptId, CancellationToken cancellationToken);

    void Add(Document document);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IDocumentStorage
{
    Task SaveAsync(string blobName, Stream content, CancellationToken cancellationToken);

    Task<Stream> OpenAsync(string blobName, CancellationToken cancellationToken);
}

public interface IMessagePublisher
{
    Task PublishAsync<T>(T message, CancellationToken cancellationToken)
        where T : class;
}

public interface IPdfTextReader
{
    string ReadText(Stream pdf);
}

public interface IInvoiceExtractor
{
    string Name { get; }

    Task<ExtractionResult> ExtractAsync(string text, CancellationToken cancellationToken);
}

public sealed class ExtractionFailedException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class DocumentNotFoundException(Guid id) : Exception($"Documento {id} não encontrado.");

public sealed class WorkflowOptions
{
    public const string Section = "Workflow";

    public double ConfidenceThreshold { get; set; } = 0.8;
}
