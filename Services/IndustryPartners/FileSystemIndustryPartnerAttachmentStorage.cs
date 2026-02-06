using System;
using System.IO;
using ProjectManagement.Application.Security;
using ProjectManagement.Services.Storage;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.IndustryPartners;

public sealed class FileSystemIndustryPartnerAttachmentStorage : IIndustryPartnerAttachmentStorage
{
    private const string RootFolder = "industry-partners";
    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly IFileSecurityValidator _validator;

    public FileSystemIndustryPartnerAttachmentStorage(IUploadRootProvider uploadRootProvider, IFileSecurityValidator validator)
    {
        _uploadRootProvider = uploadRootProvider;
        _validator = validator;
    }

    public async Task<string> SaveAsync(int partnerId, string originalFileName, Stream content, CancellationToken cancellationToken = default)
    {
        var safeFileName = FileNameSanitizer.Sanitize(originalFileName);
        var ext = Path.GetExtension(safeFileName);
        var storedName = $"{Guid.NewGuid():N}{ext}";
        var relativeKey = $"{RootFolder}/{partnerId}/{storedName}";
        _validator.ValidateRelativePath(relativeKey);

        var absolutePath = Path.Combine(_uploadRootProvider.RootPath, relativeKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        if (content.CanSeek) content.Seek(0, SeekOrigin.Begin);
        await using var destination = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await content.CopyToAsync(destination, cancellationToken);

        return relativeKey;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        _validator.ValidateRelativePath(storageKey);
        var absolutePath = Path.Combine(_uploadRootProvider.RootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
        Stream stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        _validator.ValidateRelativePath(storageKey);
        var absolutePath = Path.Combine(_uploadRootProvider.RootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(absolutePath)) File.Delete(absolutePath);
        return Task.CompletedTask;
    }
}
