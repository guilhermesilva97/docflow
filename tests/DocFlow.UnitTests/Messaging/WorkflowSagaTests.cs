using DocFlow.Application;
using DocFlow.Contracts;
using DocFlow.Domain;
using DocFlow.Infrastructure.Messaging;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DocFlow.UnitTests.Messaging;

public sealed class WorkflowSagaTests : IAsyncLifetime
{
    private readonly InMemoryDocuments _documents = new();
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FixedClock(TestData.Now))
            .AddSingleton<IDocumentRepository>(_documents)
            .AddScoped<IMessagePublisher, MassTransitPublisher>()
            .Configure<WorkflowOptions>(o => o.ConfidenceThreshold = 0.8)
            .AddScoped<DocumentWorkflow>()
            .AddScoped<StartExtractionActivity>()
            .AddScoped<RecordExtractionActivity>()
            .AddScoped<FailExtractionActivity>()
            .AddScoped<CompleteValidationActivity>()
            .AddMassTransitTestHarness(x =>
                x.AddSagaStateMachine<DocumentStateMachine, DocumentState>().InMemoryRepository())
            .BuildServiceProvider(true);
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private ISagaStateMachineTestHarness<DocumentStateMachine, DocumentState> Saga =>
        _harness.GetSagaStateMachineHarness<DocumentStateMachine, DocumentState>();

    private Document Seed()
    {
        var document = Document.Receive(Guid.NewGuid(), "nf.pdf", "2026/nf.pdf", TestData.Now);
        _documents.Add(document);
        return document;
    }

    private async Task<Document> Received()
    {
        var document = Seed();
        await _harness.Bus.Publish(new DocumentReceived(document.Id, document.BlobName, document.FileName));
        Assert.NotNull(await Saga.Exists(document.Id, s => s.Extracting));
        return document;
    }

    private async Task Extracted(Document document, double confidence = 0.95)
    {
        await _harness.Bus.Publish(new DocumentExtracted(
            document.Id,
            TestData.ValidInvoice().ToPayload(),
            confidence,
            "llm"));
        Assert.NotNull(await Saga.Exists(document.Id, s => s.Validating));
    }

    [Fact]
    public async Task Received_document_starts_extraction()
    {
        var document = await Received();

        Assert.True(await _harness.Sent.Any<ExtractDocument>(m => m.Context.Message.DocumentId == document.Id));
        Assert.Equal(DocumentStatus.Extracting, document.Status);
        Assert.True(await _harness.Published.Any<DocumentStatusChanged>(m => m.Context.Message.Status == "Extracting"));
    }

    [Fact]
    public async Task Valid_extraction_is_validated_and_auto_approved()
    {
        var document = await Received();
        await Extracted(document);

        Assert.True(await _harness.Sent.Any<ValidateDocument>(m => m.Context.Message.DocumentId == document.Id));

        await _harness.Bus.Publish(new DocumentValidated(document.Id, []));

        Assert.Null(await Saga.NotExists(document.Id));
        Assert.Equal(DocumentStatus.Approved, document.Status);
    }

    [Fact]
    public async Task Issues_park_the_workflow_until_a_human_reviews()
    {
        var document = await Received();
        await Extracted(document);

        await _harness.Bus.Publish(new DocumentValidated(document.Id, [new IssuePayload("total_mismatch", "x")]));

        Assert.NotNull(await Saga.Exists(document.Id, s => s.AwaitingReview));
        Assert.Equal(DocumentStatus.NeedsReview, document.Status);

        await _harness.Bus.Publish(new DocumentReviewed(document.Id, "Approved"));

        Assert.Null(await Saga.NotExists(document.Id));
    }

    [Fact]
    public async Task Extraction_failure_goes_straight_to_review()
    {
        var document = await Received();

        await _harness.Bus.Publish(new DocumentExtractionFailed(document.Id, "PDF sem texto"));

        Assert.NotNull(await Saga.Exists(document.Id, s => s.AwaitingReview));
        Assert.Equal(DocumentStatus.NeedsReview, document.Status);
    }

    [Fact]
    public async Task Duplicate_extraction_event_is_ignored()
    {
        var document = await Received();
        await Extracted(document);

        await _harness.Bus.Publish(new DocumentExtracted(document.Id, TestData.ValidInvoice().ToPayload(), 0.95, "llm"));

        Assert.True(await _harness.Consumed.Any<DocumentExtracted>(m => m.Context.Message.DocumentId == document.Id));
        Assert.False(await _harness.Published.Any<Fault<DocumentExtracted>>());
        Assert.Equal(DocumentStatus.Validating, document.Status);
    }
}
