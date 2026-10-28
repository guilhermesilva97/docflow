using DocFlow.Application;
using DocFlow.Contracts;
using DocFlow.Infrastructure.Extraction;
using DocFlow.Infrastructure.Messaging;
using DocFlow.Infrastructure.Pdf;
using DocFlow.Samples;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DocFlow.UnitTests.Messaging;

public class WorkerConsumerTests
{
    private static async Task<(ServiceProvider Provider, ITestHarness Harness)> StartAsync(IDocumentStorage storage)
    {
        var provider = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FixedClock(TestData.Now))
            .AddSingleton(storage)
            .AddSingleton<IPdfTextReader, PdfPigTextReader>()
            .AddSingleton<IInvoiceExtractor, HeuristicInvoiceExtractor>()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<ExtractDocumentConsumer>();
                x.AddConsumer<ValidateDocumentConsumer>();
            })
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return (provider, harness);
    }

    [Fact]
    public async Task Extraction_publishes_fields_read_from_the_pdf()
    {
        var sample = new InvoiceSampleGenerator(7, TestData.Today).Generate(SampleTemplate.Classic, SampleScenario.Valid);
        var (provider, harness) = await StartAsync(new SingleBlob(sample.Pdf));
        await using var _ = provider;
        var id = Guid.NewGuid();

        await harness.Bus.Publish(new ExtractDocument(id, "nf.pdf"));

        Assert.True(await harness.Published.Any<DocumentExtracted>(m => m.Context.Message.DocumentId == id));
        var message = harness.Published.Select<DocumentExtracted>().First().Context.Message;
        Assert.Equal(sample.Truth.Total, message.Invoice.Total);
        Assert.Equal("heuristic", message.Method);
    }

    [Fact]
    public async Task Unreadable_file_becomes_a_controlled_extraction_failure()
    {
        var (provider, harness) = await StartAsync(new SingleBlob("não sou um pdf"u8.ToArray()));
        await using var _ = provider;

        await harness.Bus.Publish(new ExtractDocument(Guid.NewGuid(), "x.pdf"));

        Assert.True(await harness.Published.Any<DocumentExtractionFailed>());
        Assert.False(await harness.Published.Any<Fault<ExtractDocument>>());
    }

    [Fact]
    public async Task Validation_publishes_the_issues_found()
    {
        var (provider, harness) = await StartAsync(new SingleBlob([]));
        await using var _ = provider;
        var invoice = TestData.ValidInvoice() with { Total = 10m };

        await harness.Bus.Publish(new ValidateDocument(Guid.NewGuid(), invoice.ToPayload()));

        Assert.True(await harness.Published.Any<DocumentValidated>());
        var message = harness.Published.Select<DocumentValidated>().First().Context.Message;
        Assert.Contains(message.Issues, i => i.Code == "total_mismatch");
    }

    private sealed class SingleBlob(byte[] content) : IDocumentStorage
    {
        public Task SaveAsync(string blobName, Stream content, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Stream> OpenAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(content));
    }
}
