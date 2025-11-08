using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Document OCR runner contract
public interface IDocumentOcrRunner
{
    Task<OcrRunResult> RunAsync(Document document, CancellationToken ct = default);
}

// SECTION: Document OCR result model
public sealed class OcrRunResult
{
    public bool Success { get; init; }
    public string? Text { get; init; }
    public string? Error { get; init; }

    public static OcrRunResult Ok(string text) => new() { Success = true, Text = text };

    public static OcrRunResult Fail(string error) => new() { Success = false, Error = error };
}
