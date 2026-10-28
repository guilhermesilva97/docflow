using System.Globalization;
using System.Text.RegularExpressions;
using DocFlow.Application;
using DocFlow.Domain;

namespace DocFlow.Infrastructure.Extraction;

public sealed partial class HeuristicInvoiceExtractor : IInvoiceExtractor
{
    private const int ExpectedFields = 9;
    private const double MaxConfidence = 0.9;

    public string Name => "heuristic";

    public Task<ExtractionResult> ExtractAsync(string text, CancellationToken cancellationToken)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var cnpjs = CnpjPattern().Matches(text).Select(m => Cnpj.Digits(m.Value)).Distinct().ToArray();

        var invoice = new InvoiceData(
            IssuerName: Labeled(lines, "Emitente"),
            IssuerCnpj: Digits(Labeled(lines, "CNPJ do emitente")) ?? cnpjs.ElementAtOrDefault(0),
            RecipientName: Labeled(lines, "Destinatário"),
            RecipientCnpj: Digits(Labeled(lines, "CNPJ do destinatário")) ?? cnpjs.ElementAtOrDefault(1),
            Number: Labeled(lines, "Número"),
            IssueDate: ParseDate(Labeled(lines, "Data de emissão")),
            DueDate: ParseDate(Labeled(lines, "Vencimento")),
            Items: [.. lines.Select(ParseItem).OfType<InvoiceItem>()],
            Total: ParseMoney(Labeled(lines, "Valor total")));

        var found = new object?[]
        {
            invoice.IssuerName, invoice.IssuerCnpj, invoice.RecipientName, invoice.RecipientCnpj,
            invoice.Number, invoice.IssueDate, invoice.DueDate, invoice.Total,
            invoice.Items.Count > 0 ? invoice.Items : null,
        }.Count(value => value is not null);

        if (found == 0)
        {
            throw new ExtractionFailedException("Nenhum campo reconhecido pelo extrator heurístico.");
        }

        return Task.FromResult(new ExtractionResult(invoice, MaxConfidence * found / ExpectedFields, Name));
    }

    private static string? Labeled(IEnumerable<string> lines, string label)
    {
        var prefix = $"{label}:";
        return lines
            .Where(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(line => line[prefix.Length..].Trim())
            .FirstOrDefault(value => value.Length > 0);
    }

    private static string? Digits(string? value) => value is null ? null : Cnpj.Digits(value);

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static decimal? ParseMoney(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var match = BrlAmountPattern().Match(value);
        return match.Success ? ParseBrl(match.Value) : null;
    }

    private static InvoiceItem? ParseItem(string line)
    {
        var match = ItemPattern().Match(line);
        if (!match.Success)
        {
            return null;
        }

        return new InvoiceItem(
            match.Groups["description"].Value.Trim(),
            ParseBrl(match.Groups["quantity"].Value),
            ParseBrl(match.Groups["unit"].Value),
            ParseBrl(match.Groups["total"].Value));
    }

    private static decimal ParseBrl(string value) =>
        decimal.Parse(value.Replace(".", string.Empty).Replace(',', '.'), CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b")]
    private static partial Regex CnpjPattern();

    [GeneratedRegex(@"\d{1,3}(?:\.\d{3})*,\d{2}")]
    private static partial Regex BrlAmountPattern();

    [GeneratedRegex(@"^\d+\.\s*(?<description>.+?)\s*\|\s*(?<quantity>\d+(?:,\d+)?)\s*x\s*R\$\s*(?<unit>[\d.]+,\d{2})\s*=\s*R\$\s*(?<total>[\d.]+,\d{2})$")]
    private static partial Regex ItemPattern();
}
