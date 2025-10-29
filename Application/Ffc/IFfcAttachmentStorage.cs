using Microsoft.AspNetCore.Http;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Application.Ffc;

public interface IFfcAttachmentStorage
{
    Task<(bool Success, string? ErrorMessage)> SaveAsync(long recordId, IFormFile file, FfcAttachmentKind kind, string? caption);
    Task DeleteAsync(FfcAttachment attachment);
}
