using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace ProjectManagement.Services.Ocr;

// SECTION: PDF text extractor implementation using PdfPig
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public async Task<string?> TryExtractAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
        {
            return null;
        }

        try
        {
            return await Task.Run(() => Extract(pdfPath), cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    // SECTION: Extraction helpers
    private static string Extract(string pdfPath)
    {
        var builder = new StringBuilder();

        using var document = PdfDocument.Open(pdfPath);
        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        return builder.ToString();
    }
}
