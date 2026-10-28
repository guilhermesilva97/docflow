namespace DocFlow.Domain;

public class DomainException(string message) : Exception(message);

public sealed class InvalidTransitionException(DocumentStatus from, DocumentStatus to)
    : DomainException($"Transição inválida: {from} -> {to}.");

public sealed class InvoiceRejectedException(IReadOnlyList<ValidationIssue> issues)
    : DomainException("A nota não passa nas regras de validação.")
{
    public IReadOnlyList<ValidationIssue> Issues { get; } = issues;
}

public sealed class DuplicateInvoiceException(string key)
    : DomainException($"Já existe uma nota aprovada com o mesmo emitente e número ({key}).");
