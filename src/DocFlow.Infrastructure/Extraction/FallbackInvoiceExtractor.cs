using DocFlow.Application;
using DocFlow.Domain;
using Microsoft.Extensions.Logging;

namespace DocFlow.Infrastructure.Extraction;

public sealed class FallbackInvoiceExtractor(
    IInvoiceExtractor primary,
    IInvoiceExtractor fallback,
    ILogger<FallbackInvoiceExtractor> logger) : IInvoiceExtractor
{
    public string Name => $"{primary.Name}+{fallback.Name}";

    public async Task<ExtractionResult> ExtractAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            return await primary.ExtractAsync(text, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Extrator {Primary} falhou; usando {Fallback}", primary.Name, fallback.Name);
            var result = await fallback.ExtractAsync(text, cancellationToken);
            return result with { Method = $"{fallback.Name} (fallback)" };
        }
    }
}
