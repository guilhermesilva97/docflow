using DocFlow.Application;
using DocFlow.Contracts;
using DocFlow.Domain;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace DocFlow.Infrastructure.Messaging;

public sealed class ExtractDocumentConsumer(
    IDocumentStorage storage,
    IPdfTextReader pdfReader,
    IInvoiceExtractor extractor,
    ILogger<ExtractDocumentConsumer> logger) : IConsumer<ExtractDocument>
{
    public async Task Consume(ConsumeContext<ExtractDocument> context)
    {
        var message = context.Message;
        await using var pdf = await storage.OpenAsync(message.BlobName, context.CancellationToken);

        try
        {
            var text = ReadText(pdf);
            var result = await extractor.ExtractAsync(text, context.CancellationToken);
            logger.LogInformation(
                "Documento {DocumentId} extraído via {Method} com confiança {Confidence:P0}",
                message.DocumentId,
                result.Method,
                result.Confidence);
            await context.Publish(new DocumentExtracted(
                message.DocumentId,
                result.Invoice.ToPayload(),
                result.Confidence,
                result.Method));
        }
        catch (ExtractionFailedException error)
        {
            logger.LogWarning("Extração do documento {DocumentId} falhou: {Reason}", message.DocumentId, error.Message);
            await context.Publish(new DocumentExtractionFailed(message.DocumentId, error.Message));
        }
    }

    private string ReadText(Stream pdf)
    {
        string text;
        try
        {
            text = pdfReader.ReadText(pdf);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new ExtractionFailedException("O arquivo não é um PDF legível.", error);
        }

        return string.IsNullOrWhiteSpace(text)
            ? throw new ExtractionFailedException("O PDF não tem camada de texto (provavelmente é uma imagem digitalizada).")
            : text;
    }
}

public sealed class ValidateDocumentConsumer(TimeProvider clock) : IConsumer<ValidateDocument>
{
    public Task Consume(ConsumeContext<ValidateDocument> context)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var issues = InvoiceValidator.Validate(context.Message.Invoice.ToDomain(), today);
        return context.Publish(new DocumentValidated(context.Message.DocumentId, issues.ToPayload()));
    }
}

public sealed class KafkaAuditForwarder(ITopicProducer<DocumentStatusChanged> producer) : IConsumer<DocumentStatusChanged>
{
    public Task Consume(ConsumeContext<DocumentStatusChanged> context) =>
        producer.Produce(context.Message, context.CancellationToken);
}

public sealed class MassTransitPublisher(IPublishEndpoint publishEndpoint) : IMessagePublisher
{
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken)
        where T : class => publishEndpoint.Publish(message, cancellationToken);
}
