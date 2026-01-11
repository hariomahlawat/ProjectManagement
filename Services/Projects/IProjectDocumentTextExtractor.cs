using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

// SECTION: Project document text extraction contract
public interface IProjectDocumentTextExtractor
{
    Task<ProjectDocumentTextExtractionResult> ExtractAsync(ProjectDocument document, CancellationToken cancellationToken = default);
}

// SECTION: Project document text extraction outcome enumeration
public enum ProjectDocumentTextExtractionStatus
{
    NotApplicable = 0,
    Extracted = 1,
    Failed = 2
}

// SECTION: Project document text extraction result
public sealed record ProjectDocumentTextExtractionResult
{
    public ProjectDocumentTextExtractionStatus Status { get; init; }

    public string? ExtractedText { get; init; }

    public string? PdfDerivativeStorageKey { get; init; }

    public string? Error { get; init; }

    public string? ConversionError { get; init; }

    public static ProjectDocumentTextExtractionResult NotApplicable() => new()
    {
        Status = ProjectDocumentTextExtractionStatus.NotApplicable
    };

    public static ProjectDocumentTextExtractionResult Failed(string error) => new()
    {
        Status = ProjectDocumentTextExtractionStatus.Failed,
        Error = error
    };

    public static ProjectDocumentTextExtractionResult Extracted(string? text, string? pdfDerivativeStorageKey, string? conversionError) => new()
    {
        Status = ProjectDocumentTextExtractionStatus.Extracted,
        ExtractedText = text,
        PdfDerivativeStorageKey = pdfDerivativeStorageKey,
        ConversionError = conversionError
    };
}
