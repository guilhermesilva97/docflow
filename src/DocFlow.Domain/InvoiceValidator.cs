using System.Globalization;

namespace DocFlow.Domain;

public static class InvoiceValidator
{
    private const decimal Tolerance = 0.01m;
    private const int MaxAgeInYears = 5;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static IReadOnlyList<ValidationIssue> Validate(InvoiceData invoice, DateOnly today)
    {
        var issues = new List<ValidationIssue>();

        Require(issues, invoice.IssuerName, "issuer_name_missing", "Nome do emitente não encontrado.");
        Require(issues, invoice.Number, "number_missing", "Número da nota não encontrado.");

        if (string.IsNullOrWhiteSpace(invoice.IssuerCnpj))
        {
            issues.Add(new("issuer_cnpj_missing", "CNPJ do emitente não encontrado."));
        }
        else if (!Cnpj.IsValid(invoice.IssuerCnpj))
        {
            issues.Add(new("issuer_cnpj_invalid", $"CNPJ do emitente {invoice.IssuerCnpj} tem dígito verificador inválido."));
        }

        if (!string.IsNullOrWhiteSpace(invoice.RecipientCnpj) && !Cnpj.IsValid(invoice.RecipientCnpj))
        {
            issues.Add(new("recipient_cnpj_invalid", $"CNPJ do destinatário {invoice.RecipientCnpj} tem dígito verificador inválido."));
        }

        ValidateDates(invoice, today, issues);
        ValidateAmounts(invoice, issues);

        return issues;
    }

    private static void ValidateDates(InvoiceData invoice, DateOnly today, List<ValidationIssue> issues)
    {
        if (invoice.IssueDate is not { } issued)
        {
            issues.Add(new("issue_date_missing", "Data de emissão não encontrada."));
            return;
        }

        if (issued > today)
        {
            issues.Add(new("issue_date_future", $"Data de emissão {issued:dd/MM/yyyy} está no futuro."));
        }

        if (issued < today.AddYears(-MaxAgeInYears))
        {
            issues.Add(new("issue_date_too_old", $"Data de emissão {issued:dd/MM/yyyy} tem mais de {MaxAgeInYears} anos."));
        }

        if (invoice.DueDate is { } due && due < issued)
        {
            issues.Add(new("due_date_before_issue", "Vencimento anterior à data de emissão."));
        }
    }

    private static void ValidateAmounts(InvoiceData invoice, List<ValidationIssue> issues)
    {
        if (invoice.Total is not { } total)
        {
            issues.Add(new("total_missing", "Valor total não encontrado."));
            return;
        }

        if (invoice.Items.Count == 0)
        {
            issues.Add(new("items_missing", "Nenhum item encontrado."));
            return;
        }

        foreach (var (item, index) in invoice.Items.Select((item, index) => (item, index + 1)))
        {
            if (item.Quantity <= 0)
            {
                issues.Add(new("item_quantity_invalid", $"Item {index}: quantidade deve ser maior que zero."));
            }
            else if (Math.Abs(item.Quantity * item.UnitPrice - item.Total) > Tolerance)
            {
                issues.Add(new("item_total_mismatch", string.Create(PtBr, $"Item {index}: {item.Quantity} x {item.UnitPrice:N2} não resulta em {item.Total:N2}.")));
            }
        }

        var itemsSum = invoice.Items.Sum(i => i.Total);
        if (Math.Abs(itemsSum - total) > Tolerance)
        {
            issues.Add(new("total_mismatch", string.Create(PtBr, $"Soma dos itens ({itemsSum:N2}) difere do total da nota ({total:N2}).")));
        }
    }

    private static void Require(List<ValidationIssue> issues, string? value, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new(code, message));
        }
    }
}
