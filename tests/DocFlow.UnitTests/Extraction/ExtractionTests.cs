using DocFlow.Application;
using DocFlow.Domain;
using DocFlow.Evals;
using DocFlow.Infrastructure.Extraction;
using DocFlow.Infrastructure.Pdf;
using DocFlow.Samples;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocFlow.UnitTests.Extraction;

public class ExtractionTests
{
    private static readonly InvoiceSampleGenerator Generator = new(42, TestData.Today);

    private static string TextOf(InvoiceSample sample) =>
        new PdfPigTextReader().ReadText(new MemoryStream(sample.Pdf));

    [Fact]
    public async Task Heuristic_reads_every_field_of_the_classic_template()
    {
        var sample = Generator.Generate(SampleTemplate.Classic, SampleScenario.Valid);

        var result = await new HeuristicInvoiceExtractor().ExtractAsync(TextOf(sample), CancellationToken.None);

        Assert.All(FieldScorer.Score(sample.Truth, result.Invoice), field => Assert.True(field.Value, field.Key));
        Assert.Equal(0.9, result.Confidence, 2);
        Assert.Empty(InvoiceValidator.Validate(result.Invoice, TestData.Today));
    }

    [Theory]
    [InlineData(SampleTemplate.Modern)]
    [InlineData(SampleTemplate.Compact)]
    public async Task Heuristic_is_only_partial_on_other_layouts(SampleTemplate template)
    {
        var sample = Generator.Generate(template, SampleScenario.Valid);

        var result = await new HeuristicInvoiceExtractor().ExtractAsync(TextOf(sample), CancellationToken.None);

        Assert.True(result.Confidence < 0.8, $"confiança {result.Confidence}");
        Assert.Equal(Cnpj.Digits(sample.Truth.IssuerCnpj), result.Invoice.IssuerCnpj);
    }

    [Fact]
    public async Task Heuristic_fails_when_nothing_is_recognized()
    {
        await Assert.ThrowsAsync<ExtractionFailedException>(() =>
            new HeuristicInvoiceExtractor().ExtractAsync("texto qualquer sem campos", CancellationToken.None));
    }

    [Fact]
    public async Task Fallback_is_used_when_primary_extractor_fails()
    {
        var sample = Generator.Generate(SampleTemplate.Classic, SampleScenario.Valid);
        var extractor = new FallbackInvoiceExtractor(
            new BrokenExtractor(),
            new HeuristicInvoiceExtractor(),
            NullLogger<FallbackInvoiceExtractor>.Instance);

        var result = await extractor.ExtractAsync(TextOf(sample), CancellationToken.None);

        Assert.Equal("heuristic (fallback)", result.Method);
        Assert.Equal(sample.Truth.Total, result.Invoice.Total);
    }

    [Fact]
    public void Generator_scenarios_break_the_expected_rule()
    {
        var invalidCnpj = Generator.Generate(SampleTemplate.Classic, SampleScenario.InvalidCnpj).Truth;
        var wrongTotal = Generator.Generate(SampleTemplate.Classic, SampleScenario.WrongTotal).Truth;

        Assert.Contains(InvoiceValidator.Validate(invalidCnpj, TestData.Today), i => i.Code == "issuer_cnpj_invalid");
        Assert.Contains(InvoiceValidator.Validate(wrongTotal, TestData.Today), i => i.Code == "total_mismatch");
    }

    private sealed class BrokenExtractor : IInvoiceExtractor
    {
        public string Name => "llm";

        public Task<ExtractionResult> ExtractAsync(string text, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }
}
