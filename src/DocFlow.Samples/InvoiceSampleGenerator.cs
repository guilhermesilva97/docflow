using System.Globalization;
using DocFlow.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DocFlow.Samples;

public enum SampleTemplate
{
    Classic,
    Modern,
    Compact,
}

public enum SampleScenario
{
    Valid,
    InvalidCnpj,
    WrongTotal,
}

public sealed record InvoiceSample(
    InvoiceData Truth,
    SampleTemplate Template,
    SampleScenario Scenario,
    string FileName,
    byte[] Pdf);

public sealed class InvoiceSampleGenerator
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] Companies =
    [
        "Aurora Tecnologia Ltda",
        "Vértice Consultoria S.A.",
        "Horizonte Serviços Digitais ME",
        "Cobalto Engenharia Ltda",
        "Lumen Logística Ltda",
        "Seta Sistemas de Gestão Ltda",
        "Pampa Cloud Hospedagem Ltda",
        "Ipê Treinamentos Corporativos ME",
    ];

    private static readonly string[] Services =
    [
        "Consultoria em arquitetura de software",
        "Licença anual de plataforma",
        "Suporte técnico mensal",
        "Treinamento de equipe",
        "Desenvolvimento de integração via API",
        "Hospedagem em nuvem",
        "Auditoria de segurança",
        "Migração de banco de dados",
    ];

    private static readonly string[] Months =
    [
        "janeiro", "fevereiro", "março", "abril", "maio", "junho",
        "julho", "agosto", "setembro", "outubro", "novembro", "dezembro",
    ];

    private readonly Random _random;
    private readonly DateOnly _today;

    static InvoiceSampleGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public InvoiceSampleGenerator(int seed, DateOnly today)
    {
        _random = new Random(seed);
        _today = today;
    }

    public InvoiceSample Generate(SampleTemplate template, SampleScenario scenario)
    {
        var truth = CreateInvoice();
        var printed = scenario switch
        {
            SampleScenario.InvalidCnpj => truth with { IssuerCnpj = CorruptCnpj(truth.IssuerCnpj!) },
            SampleScenario.WrongTotal => truth with { Total = truth.Total + 100m },
            _ => truth,
        };

        var pdf = template switch
        {
            SampleTemplate.Modern => RenderModern(printed),
            SampleTemplate.Compact => RenderCompact(printed),
            _ => RenderClassic(printed),
        };

        var fileName = $"nf-{printed.Number}-{template.ToString().ToLowerInvariant()}.pdf";
        return new InvoiceSample(printed, template, scenario, fileName, pdf);
    }

    public IEnumerable<InvoiceSample> GenerateSet(int perTemplate)
    {
        foreach (var template in Enum.GetValues<SampleTemplate>())
        {
            for (var i = 0; i < perTemplate; i++)
            {
                yield return Generate(template, SampleScenario.Valid);
            }
        }
    }

    private InvoiceData CreateInvoice()
    {
        var issuer = Pick(Companies);
        var recipient = Pick(Companies.Where(c => c != issuer).ToArray());
        var issued = _today.AddDays(-_random.Next(1, 120));
        var items = Enumerable.Range(0, _random.Next(1, 5))
            .Select(_ =>
            {
                var quantity = (decimal)_random.Next(1, 12);
                var unitPrice = Math.Round((decimal)(_random.NextDouble() * 2400 + 80), 2);
                return new InvoiceItem(Pick(Services), quantity, unitPrice, quantity * unitPrice);
            })
            .ToList();

        return new InvoiceData(
            issuer,
            NewCnpj(),
            recipient,
            NewCnpj(),
            _random.Next(1, 99999).ToString("D6", CultureInfo.InvariantCulture),
            issued,
            issued.AddDays(30),
            items,
            items.Sum(i => i.Total));
    }

    private static byte[] RenderClassic(InvoiceData invoice) =>
        QuestPDF.Fluent.Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(11));
            page.Header().Text("NOTA FISCAL DE SERVIÇOS").Bold().FontSize(18);
            page.Content().PaddingTop(16).Column(column =>
            {
                column.Spacing(6);
                column.Item().Text($"Número: {invoice.Number}");
                column.Item().Text($"Data de emissão: {invoice.IssueDate:dd/MM/yyyy}");
                column.Item().Text($"Vencimento: {invoice.DueDate:dd/MM/yyyy}");
                column.Item().Text($"Emitente: {invoice.IssuerName}");
                column.Item().Text($"CNPJ do emitente: {Cnpj.Format(invoice.IssuerCnpj!)}");
                column.Item().Text($"Destinatário: {invoice.RecipientName}");
                column.Item().Text($"CNPJ do destinatário: {Cnpj.Format(invoice.RecipientCnpj!)}");
                column.Item().PaddingTop(10).Text("Itens").Bold();
                foreach (var (item, index) in invoice.Items.Select((item, index) => (item, index + 1)))
                {
                    column.Item().Text(
                        $"{index}. {item.Description} | {item.Quantity:0} x {Brl(item.UnitPrice)} = {Brl(item.Total)}");
                }

                column.Item().PaddingTop(10).Text($"Valor total: {Brl(invoice.Total!.Value)}").Bold();
            });
        })).GeneratePdf();

    private static byte[] RenderModern(InvoiceData invoice) =>
        QuestPDF.Fluent.Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(10));
            page.Header().Row(row =>
            {
                row.RelativeItem().Text($"FATURA / NOTA Nº {invoice.Number?.TrimStart('0')}").Bold().FontSize(16);
                row.ConstantItem(200).AlignRight().Text($"Emitida em {LongDate(invoice.IssueDate!.Value)}");
            });
            page.Content().PaddingTop(20).Column(column =>
            {
                column.Spacing(8);
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("FORNECEDOR").FontSize(8);
                        c.Item().Text(invoice.IssuerName!).Bold();
                        c.Item().Text($"Inscrição CNPJ {Cnpj.Digits(invoice.IssuerCnpj)}");
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("CLIENTE").FontSize(8);
                        c.Item().Text(invoice.RecipientName!).Bold();
                        c.Item().Text($"Inscrição CNPJ {Cnpj.Digits(invoice.RecipientCnpj)}");
                    });
                });
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(5);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                    });
                    table.Header(header =>
                    {
                        header.Cell().Text("Serviço").Bold();
                        header.Cell().AlignRight().Text("Qtd").Bold();
                        header.Cell().AlignRight().Text("Unitário").Bold();
                        header.Cell().AlignRight().Text("Subtotal").Bold();
                    });
                    foreach (var item in invoice.Items)
                    {
                        table.Cell().Text(item.Description);
                        table.Cell().AlignRight().Text(item.Quantity.ToString("0", PtBr));
                        table.Cell().AlignRight().Text(item.UnitPrice.ToString("N2", PtBr));
                        table.Cell().AlignRight().Text(item.Total.ToString("N2", PtBr));
                    }
                });
                column.Item().AlignRight().Text($"TOTAL A PAGAR {invoice.Total!.Value.ToString("N2", PtBr)}").Bold().FontSize(13);
                column.Item().Text($"Pagamento até {LongDate(invoice.DueDate!.Value)}");
            });
        })).GeneratePdf();

    private static byte[] RenderCompact(InvoiceData invoice) =>
        QuestPDF.Fluent.Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A5.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(x => x.FontSize(9));
            page.Content().Column(column =>
            {
                column.Spacing(4);
                column.Item().Text(
                    $"NF {invoice.Number} · emitida {invoice.IssueDate:yyyy-MM-dd} · vence {invoice.DueDate:yyyy-MM-dd}").Bold();
                column.Item().Text($"Prestador: {invoice.IssuerName} (CNPJ {Cnpj.Format(invoice.IssuerCnpj!)})");
                column.Item().Text($"Tomador: {invoice.RecipientName} (CNPJ {Cnpj.Format(invoice.RecipientCnpj!)})");
                foreach (var item in invoice.Items)
                {
                    column.Item().Text(string.Create(
                        CultureInfo.InvariantCulture,
                        $"- {item.Quantity:0}x {item.Description} @ {item.UnitPrice:0.00} = {item.Total:0.00}"));
                }

                column.Item().Text(string.Create(CultureInfo.InvariantCulture, $"Total geral BRL {invoice.Total:0.00}")).Bold();
            });
        })).GeneratePdf();

    private string NewCnpj() =>
        Cnpj.WithCheckDigits($"{_random.Next(10_000_000, 99_999_999)}0001");

    private static string CorruptCnpj(string cnpj)
    {
        var digits = Cnpj.Digits(cnpj);
        var last = (char)('0' + (digits[^1] - '0' + 1) % 10);
        return digits[..^1] + last;
    }

    private T Pick<T>(IReadOnlyList<T> values) => values[_random.Next(values.Count)];

    private static string Brl(decimal value) => $"R$ {value.ToString("N2", PtBr)}";

    private static string LongDate(DateOnly date) => $"{date.Day} de {Months[date.Month - 1]} de {date.Year}";
}
