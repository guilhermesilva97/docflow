namespace DocFlow.Contracts;

public static class Queues
{
    public const string ExtractDocument = "extract-document";
    public const string ValidateDocument = "validate-document";
    public const string KafkaAuditForwarder = "kafka-audit-forwarder";
}

public static class Topics
{
    public const string DocumentEvents = "docflow.document-events";
}

public sealed record InvoiceItemPayload(string Description, decimal Quantity, decimal UnitPrice, decimal Total);

public sealed record InvoicePayload(
    string? IssuerName,
    string? IssuerCnpj,
    string? RecipientName,
    string? RecipientCnpj,
    string? Number,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    IReadOnlyList<InvoiceItemPayload> Items,
    decimal? Total);

public sealed record IssuePayload(string Code, string Message);

public sealed record DocumentReceived(Guid DocumentId, string BlobName, string FileName);

public sealed record ExtractDocument(Guid DocumentId, string BlobName);

public sealed record DocumentExtracted(Guid DocumentId, InvoicePayload Invoice, double Confidence, string Method);

public sealed record DocumentExtractionFailed(Guid DocumentId, string Reason);

public sealed record ValidateDocument(Guid DocumentId, InvoicePayload Invoice);

public sealed record DocumentValidated(Guid DocumentId, IReadOnlyList<IssuePayload> Issues);

public sealed record DocumentReviewed(Guid DocumentId, string Status);

public sealed record DocumentStatusChanged(Guid DocumentId, string FileName, string Status, string Detail, DateTimeOffset OccurredAt);
