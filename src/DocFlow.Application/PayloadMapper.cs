using DocFlow.Contracts;
using DocFlow.Domain;

namespace DocFlow.Application;

public static class PayloadMapper
{
    public static InvoicePayload ToPayload(this InvoiceData invoice) => new(
        invoice.IssuerName,
        invoice.IssuerCnpj,
        invoice.RecipientName,
        invoice.RecipientCnpj,
        invoice.Number,
        invoice.IssueDate,
        invoice.DueDate,
        [.. invoice.Items.Select(i => new InvoiceItemPayload(i.Description, i.Quantity, i.UnitPrice, i.Total))],
        invoice.Total);

    public static InvoiceData ToDomain(this InvoicePayload payload) => new(
        payload.IssuerName,
        payload.IssuerCnpj,
        payload.RecipientName,
        payload.RecipientCnpj,
        payload.Number,
        payload.IssueDate,
        payload.DueDate,
        [.. payload.Items.Select(i => new InvoiceItem(i.Description, i.Quantity, i.UnitPrice, i.Total))],
        payload.Total);

    public static IReadOnlyList<IssuePayload> ToPayload(this IEnumerable<ValidationIssue> issues) =>
        [.. issues.Select(i => new IssuePayload(i.Code, i.Message))];

    public static IReadOnlyList<ValidationIssue> ToDomain(this IEnumerable<IssuePayload> issues) =>
        [.. issues.Select(i => new ValidationIssue(i.Code, i.Message))];

    public static DocumentStatusChanged ToStatusChanged(this Document document) => new(
        document.Id,
        document.FileName,
        document.Status.ToString(),
        document.LastChange.Detail,
        document.LastChange.At);
}
