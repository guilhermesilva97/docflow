using DocFlow.Domain;

namespace DocFlow.Evals;

public static class FieldScorer
{
    public static readonly string[] Fields =
    [
        "issuer_name", "issuer_cnpj", "recipient_name", "recipient_cnpj",
        "number", "issue_date", "due_date", "total", "items",
    ];

    public static IReadOnlyDictionary<string, bool> Score(InvoiceData truth, InvoiceData? actual)
    {
        if (actual is null)
        {
            return Fields.ToDictionary(f => f, _ => false);
        }

        return new Dictionary<string, bool>
        {
            ["issuer_name"] = SameText(truth.IssuerName, actual.IssuerName),
            ["issuer_cnpj"] = Cnpj.Digits(truth.IssuerCnpj) == Cnpj.Digits(actual.IssuerCnpj),
            ["recipient_name"] = SameText(truth.RecipientName, actual.RecipientName),
            ["recipient_cnpj"] = Cnpj.Digits(truth.RecipientCnpj) == Cnpj.Digits(actual.RecipientCnpj),
            ["number"] = (truth.Number ?? string.Empty).TrimStart('0') == (actual.Number ?? string.Empty).Trim().TrimStart('0'),
            ["issue_date"] = truth.IssueDate == actual.IssueDate,
            ["due_date"] = truth.DueDate == actual.DueDate,
            ["total"] = SameAmount(truth.Total, actual.Total),
            ["items"] = truth.Items.Count == actual.Items.Count
                && SameAmount(truth.Items.Sum(i => i.Total), actual.Items.Sum(i => i.Total)),
        };
    }

    private static bool SameText(string? expected, string? actual) =>
        string.Equals(expected?.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameAmount(decimal? expected, decimal? actual) =>
        expected is { } e && actual is { } a && Math.Abs(e - a) <= 0.01m;
}
