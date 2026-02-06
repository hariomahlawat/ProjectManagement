using System;
using System.IO;

namespace ProjectManagement.Services.IndustryPartners;

public interface IIndustryPartnerAttachmentStorage
{
    Task<string> SaveAsync(int partnerId, string originalFileName, Stream content, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
