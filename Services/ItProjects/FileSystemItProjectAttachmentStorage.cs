using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Application.Security;
using ProjectManagement.Services.Storage;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.ItProjects;

/// <summary>
/// Files under <c>{upload root}/it-projects/{projectId}/{guid}{ext}</c>, the Industry partners
/// pattern. Every key is checked by <see cref="IFileSecurityValidator"/> before it touches the
/// file system, and a stored file that fails the configured content check is removed again.
/// </summary>
public sealed class FileSystemItProjectAttachmentStorage : IItProjectAttachmentStorage
{
    public const string RootFolder = "it-projects";
    public const string RejectedMessage = "The file failed the configured security checks.";

    private readonly IUploadRootProvider _uploadRootProvider;
    private readonly IFileSecurityValidator _validator;

    public FileSystemItProjectAttachmentStorage(IUploadRootProvider uploadRootProvider, IFileSecurityValidator validator)
    {
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<string> SaveAsync(int itProjectId, string originalFileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var ext = Path.GetExtension(FileNameSanitizer.Sanitize(originalFileName)).ToLowerInvariant();
        var relativeKey = $"{RootFolder}/{itProjectId}/{Guid.NewGuid():N}{ext}";
        _validator.ValidateRelativePath(relativeKey);

        var absolutePath = AbsolutePath(relativeKey);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        if (content.CanSeek)
        {
            content.Seek(0, SeekOrigin.Begin);
        }

        try
        {
            await using (var destination = new FileStream(absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(destination, cancellationToken);
            }

            if (!await _validator.IsSafeAsync(absolutePath, contentType: string.Empty, cancellationToken))
            {
                throw new ItProjectAttachmentRejectedException(RejectedMessage);
            }
        }
        catch
        {
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            throw;
        }

        return relativeKey;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        _validator.ValidateRelativePath(storageKey);
        Stream stream = new FileStream(AbsolutePath(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        _validator.ValidateRelativePath(storageKey);
        var absolutePath = AbsolutePath(storageKey);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    private string AbsolutePath(string relativeKey)
        => Path.Combine(_uploadRootProvider.RootPath, relativeKey.Replace('/', Path.DirectorySeparatorChar));
}
