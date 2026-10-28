using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DocFlow.Application;
using DocFlow.Contracts;
using DocFlow.Domain;
using DocFlow.Samples;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DocFlow.Api;

public sealed record DocumentSummary(
    Guid Id,
    string FileName,
    DocumentStatus Status,
    DateTimeOffset ReceivedAt,
    DateTimeOffset UpdatedAt,
    string? IssuerName,
    decimal? Total,
    double? Confidence);

public sealed record DocumentDetail(
    Guid Id,
    string FileName,
    DocumentStatus Status,
    DateTimeOffset ReceivedAt,
    DateTimeOffset UpdatedAt,
    InvoiceData? Invoice,
    double? Confidence,
    string? ExtractionMethod,
    string? ReviewedBy,
    IReadOnlyList<ValidationIssue> Issues,
    IReadOnlyList<DocumentHistoryEntry> History);

public sealed record ApproveRequest(InvoiceData Invoice, string Reviewer);

public sealed record RejectRequest(string Reason, string Reviewer);

public sealed record SampleRequest(SampleTemplate? Template, SampleScenario? Scenario);

public static class DocumentEndpoints
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var documents = app.MapGroup("/api/documents");

        documents.MapPost("/", UploadAsync).DisableAntiforgery();
        documents.MapGet("/", ListAsync);
        documents.MapGet("/events", (DocumentEventBroadcaster broadcaster, CancellationToken cancellationToken) =>
            TypedResults.ServerSentEvents(LiveEventsAsync(broadcaster, cancellationToken)));
        documents.MapGet("/{id:guid}", GetAsync);
        documents.MapGet("/{id:guid}/file", DownloadAsync);
        documents.MapPost("/{id:guid}/approve", ApproveAsync);
        documents.MapPost("/{id:guid}/reject", RejectAsync);

        app.MapPost("/api/samples", GenerateSampleAsync);
    }

    private static async IAsyncEnumerable<SseItem<DocumentStatusChanged?>> LiveEventsAsync(
        DocumentEventBroadcaster broadcaster,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new SseItem<DocumentStatusChanged?>(null, "ready");
        await foreach (var change in broadcaster.SubscribeAsync(cancellationToken))
        {
            yield return new SseItem<DocumentStatusChanged?>(change, "status");
        }
    }

    private static async Task<Results<Accepted<object>, BadRequest<string>>> UploadAsync(
        IFormFile file,
        DocumentIngestion ingestion,
        CancellationToken cancellationToken)
    {
        if (file.Length is 0 or > MaxUploadBytes)
        {
            return TypedResults.BadRequest("O arquivo deve ter entre 1 byte e 10 MB.");
        }

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.BadRequest("Apenas arquivos PDF são aceitos.");
        }

        await using var content = file.OpenReadStream();
        var id = await ingestion.ReceiveAsync(content, file.FileName, cancellationToken);
        return TypedResults.Accepted($"/api/documents/{id}", (object)new { id });
    }

    private static async Task<Accepted<object>> GenerateSampleAsync(
        SampleRequest? request,
        DocumentIngestion ingestion,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var generator = new InvoiceSampleGenerator(Random.Shared.Next(), today);
        var template = request?.Template ?? Random.Shared.GetItems(Enum.GetValues<SampleTemplate>(), 1)[0];
        var sample = generator.Generate(template, request?.Scenario ?? SampleScenario.Valid);

        using var content = new MemoryStream(sample.Pdf);
        var id = await ingestion.ReceiveAsync(content, sample.FileName, cancellationToken);
        return TypedResults.Accepted($"/api/documents/{id}", (object)new { id, sample.FileName, sample.Template, sample.Scenario });
    }

    private static async Task<Ok<IEnumerable<DocumentSummary>>> ListAsync(
        DocumentStatus? status,
        IDocumentRepository repository,
        CancellationToken cancellationToken)
    {
        var documents = await repository.ListAsync(status, 100, cancellationToken);
        return TypedResults.Ok(documents.Select(d => new DocumentSummary(
            d.Id,
            d.FileName,
            d.Status,
            d.ReceivedAt,
            d.UpdatedAt,
            d.Invoice?.IssuerName,
            d.Invoice?.Total,
            d.Confidence)));
    }

    private static async Task<Results<Ok<DocumentDetail>, NotFound>> GetAsync(
        Guid id,
        IDocumentRepository repository,
        CancellationToken cancellationToken)
    {
        var document = await repository.GetAsync(id, cancellationToken);
        return document is null ? TypedResults.NotFound() : TypedResults.Ok(ToDetail(document));
    }

    private static async Task<Results<FileStreamHttpResult, NotFound>> DownloadAsync(
        Guid id,
        IDocumentRepository repository,
        IDocumentStorage storage,
        CancellationToken cancellationToken)
    {
        var document = await repository.GetAsync(id, cancellationToken);
        if (document is null)
        {
            return TypedResults.NotFound();
        }

        var content = await storage.OpenAsync(document.BlobName, cancellationToken);
        return TypedResults.File(content, "application/pdf");
    }

    private static async Task<Results<Ok<DocumentDetail>, NotFound, ValidationProblem, Conflict<string>>> ApproveAsync(
        Guid id,
        ApproveRequest request,
        DocumentReview review,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await review.ApproveAsync(id, request.Invoice, request.Reviewer, cancellationToken);
            return TypedResults.Ok(ToDetail(document));
        }
        catch (DocumentNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (InvoiceRejectedException error)
        {
            return TypedResults.ValidationProblem(
                error.Issues.GroupBy(i => i.Code).ToDictionary(g => g.Key, g => g.Select(i => i.Message).ToArray()),
                title: error.Message);
        }
        catch (DomainException error)
        {
            return TypedResults.Conflict(error.Message);
        }
    }

    private static async Task<Results<Ok<DocumentDetail>, NotFound, Conflict<string>>> RejectAsync(
        Guid id,
        RejectRequest request,
        DocumentReview review,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await review.RejectAsync(id, request.Reason, request.Reviewer, cancellationToken);
            return TypedResults.Ok(ToDetail(document));
        }
        catch (DocumentNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (DomainException error)
        {
            return TypedResults.Conflict(error.Message);
        }
    }

    private static DocumentDetail ToDetail(Document document) => new(
        document.Id,
        document.FileName,
        document.Status,
        document.ReceivedAt,
        document.UpdatedAt,
        document.Invoice,
        document.Confidence,
        document.ExtractionMethod,
        document.ReviewedBy,
        document.Issues,
        document.History);
}
