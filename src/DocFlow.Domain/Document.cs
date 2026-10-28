namespace DocFlow.Domain;

public sealed class Document
{
    private static readonly Dictionary<DocumentStatus, DocumentStatus[]> AllowedTransitions = new()
    {
        [DocumentStatus.Received] = [DocumentStatus.Extracting],
        [DocumentStatus.Extracting] = [DocumentStatus.Extracted, DocumentStatus.NeedsReview],
        [DocumentStatus.Extracted] = [DocumentStatus.Validating],
        [DocumentStatus.Validating] = [DocumentStatus.NeedsReview, DocumentStatus.Approved, DocumentStatus.Rejected],
        [DocumentStatus.NeedsReview] = [DocumentStatus.Approved, DocumentStatus.Rejected],
        [DocumentStatus.Approved] = [],
        [DocumentStatus.Rejected] = [],
    };

    private Document()
    {
    }

    public Guid Id { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string BlobName { get; private set; } = string.Empty;
    public DocumentStatus Status { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public InvoiceData? Invoice { get; private set; }
    public double? Confidence { get; private set; }
    public string? ExtractionMethod { get; private set; }
    public string? DuplicateKey { get; private set; }
    public string? ReviewedBy { get; private set; }
    public IReadOnlyList<ValidationIssue> Issues { get; private set; } = [];
    public IReadOnlyList<DocumentHistoryEntry> History { get; private set; } = [];

    public bool IsFinal => Status is DocumentStatus.Approved or DocumentStatus.Rejected;
    public DocumentHistoryEntry LastChange => History[^1];

    public static Document Receive(Guid id, string fileName, string blobName, DateTimeOffset now)
    {
        var document = new Document
        {
            Id = id,
            FileName = fileName,
            BlobName = blobName,
            Status = DocumentStatus.Received,
            ReceivedAt = now,
            UpdatedAt = now,
        };
        document.History = [new DocumentHistoryEntry(DocumentStatus.Received, now, $"Arquivo {fileName} recebido")];
        return document;
    }

    public void StartExtraction(DateTimeOffset now) =>
        TransitionTo(DocumentStatus.Extracting, "Extração iniciada", now);

    public void RecordExtraction(ExtractionResult result, DateTimeOffset now)
    {
        Invoice = result.Invoice;
        Confidence = Math.Round(result.Confidence, 2);
        ExtractionMethod = result.Method;
        DuplicateKey = result.Invoice.DuplicateKey;
        TransitionTo(DocumentStatus.Extracted, $"Campos extraídos via {result.Method} (confiança {Confidence:P0})", now);
    }

    public void FailExtraction(string reason, DateTimeOffset now)
    {
        Invoice = InvoiceData.Empty;
        Issues = [new ValidationIssue("extraction_failed", reason)];
        TransitionTo(DocumentStatus.NeedsReview, $"Extração falhou: {reason}", now);
    }

    public void StartValidation(DateTimeOffset now) =>
        TransitionTo(DocumentStatus.Validating, "Validação iniciada", now);

    public void CompleteValidation(
        IReadOnlyList<ValidationIssue> issues,
        double confidenceThreshold,
        bool isDuplicate,
        DateTimeOffset now)
    {
        Issues = issues;

        if (issues.Count > 0)
        {
            TransitionTo(DocumentStatus.NeedsReview, $"{issues.Count} problema(s) de validação", now);
        }
        else if ((Confidence ?? 0) < confidenceThreshold)
        {
            TransitionTo(DocumentStatus.NeedsReview, $"Confiança {Confidence:P0} abaixo do mínimo de {confidenceThreshold:P0}", now);
        }
        else if (isDuplicate)
        {
            TransitionTo(DocumentStatus.Rejected, "Nota duplicada: mesmo emitente e número de uma nota já aprovada", now);
        }
        else
        {
            TransitionTo(DocumentStatus.Approved, "Aprovada automaticamente", now);
        }
    }

    public void Approve(InvoiceData reviewed, string reviewer, DateOnly today, bool isDuplicate, DateTimeOffset now)
    {
        EnsureCanMoveTo(DocumentStatus.Approved);

        var issues = InvoiceValidator.Validate(reviewed, today);
        if (issues.Count > 0)
        {
            throw new InvoiceRejectedException(issues);
        }

        if (isDuplicate)
        {
            throw new DuplicateInvoiceException(reviewed.DuplicateKey ?? string.Empty);
        }

        Invoice = reviewed;
        DuplicateKey = reviewed.DuplicateKey;
        Issues = [];
        ReviewedBy = reviewer;
        TransitionTo(DocumentStatus.Approved, $"Aprovada na revisão por {reviewer}", now);
    }

    public void Reject(string reason, string reviewer, DateTimeOffset now)
    {
        ReviewedBy = reviewer;
        TransitionTo(DocumentStatus.Rejected, $"Rejeitada por {reviewer}: {reason}", now);
    }

    private void TransitionTo(DocumentStatus target, string detail, DateTimeOffset now)
    {
        EnsureCanMoveTo(target);
        Status = target;
        UpdatedAt = now;
        History = [.. History, new DocumentHistoryEntry(target, now, detail)];
    }

    private void EnsureCanMoveTo(DocumentStatus target)
    {
        if (!AllowedTransitions[Status].Contains(target))
        {
            throw new InvalidTransitionException(Status, target);
        }
    }
}
