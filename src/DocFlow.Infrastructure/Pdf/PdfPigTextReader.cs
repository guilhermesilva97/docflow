using System.Text;
using DocFlow.Application;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocFlow.Infrastructure.Pdf;

public sealed class PdfPigTextReader : IPdfTextReader
{
    public string ReadText(Stream pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var text = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            text.AppendLine(ContentOrderTextExtractor.GetText(page));
        }

        return text.ToString();
    }
}
