using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

// SECTION: Project document OCR runner contract
public interface IProjectDocumentOcrRunner
{
    Task<ProjectDocumentOcrResult> RunAsync(ProjectDocument document, CancellationToken cancellationToken = default);
}

// SECTION: Project document OCR runner result
public sealed class ProjectDocumentOcrResult
{
    public bool Success { get; init; }
    public string? Text { get; init; }
    public string? Error { get; init; }

    public static ProjectDocumentOcrResult SuccessResult(string text) => new()
    {
        Success = true,
        Text = text
    };

    public static ProjectDocumentOcrResult Failure(string error) => new()
    {
        Success = false,
        Error = error
    };
}
