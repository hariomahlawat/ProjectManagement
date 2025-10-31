using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ProjectManagement.Services.Projects;

public interface IProjectImportService
{
    Task<ProjectImportResult> ImportLegacyProjectsAsync(
        int projectCategoryId,
        int technicalCategoryId,
        IFormFile file,
        string importedByUserName);
}

public sealed record ProjectImportResult(bool Success, string? ErrorMessage, int RowsImported);
