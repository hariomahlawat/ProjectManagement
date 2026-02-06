using System.IO;
using System.Security.Claims;
using System;
using Microsoft.AspNetCore.Http;

namespace ProjectManagement.Services.IndustryPartners;

public interface IIndustryPartnerAttachmentManager
{
    Task<Guid> UploadAsync(int partnerId, IFormFile file, ClaimsPrincipal user, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string FileName, string ContentType)> DownloadAsync(int partnerId, Guid attachmentId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int partnerId, Guid attachmentId, ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
