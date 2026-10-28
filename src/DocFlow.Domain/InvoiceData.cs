namespace DocFlow.Domain;

public sealed record InvoiceItem(string Description, decimal Quantity, decimal UnitPrice, decimal Total);

public sealed record InvoiceData(
    string? IssuerName,
    string? IssuerCnpj,
    string? RecipientName,
    string? RecipientCnpj,
    string? Number,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    IReadOnlyList<InvoiceItem> Items,
    decimal? Total)
{
    public static readonly InvoiceData Empty = new(null, null, null, null, null, null, null, [], null);

    public string? DuplicateKey =>
        string.IsNullOrWhiteSpace(IssuerCnpj) || string.IsNullOrWhiteSpace(Number)
            ? null
            : $"{Cnpj.Digits(IssuerCnpj)}|{Number.Trim().TrimStart('0')}";
}

public sealed record ExtractionResult(InvoiceData Invoice, double Confidence, string Method);

public sealed record ValidationIssue(string Code, string Message);

public sealed record DocumentHistoryEntry(DocumentStatus Status, DateTimeOffset At, string Detail);
