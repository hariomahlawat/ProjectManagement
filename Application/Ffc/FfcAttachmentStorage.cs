using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Security;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Services;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Application.Ffc;

public sealed class FfcAttachmentStorage(
    ApplicationDbContext db,
    IFileSecurityValidator validator,
    IUploadRootProvider uploadRootProvider,
    IUploadPathResolver pathResolver,
    IUserContext userContext,
    IOptions<FfcAttachmentOptions> options) : IFfcAttachmentStorage
{
    private readonly ApplicationDbContext _db = db;
    private readonly IFileSecurityValidator _validator = validator;
    private readonly IUserContext _userContext = userContext;
    private readonly IUploadPathResolver _pathResolver = pathResolver;
    private readonly FfcAttachmentOptions _options = options.Value;
    private readonly string _storageRoot = ResolveStorageRoot(options.Value, uploadRootProvider);

    private const string AuthorizationError = "Only Admin or HoD roles can manage attachments.";

    private static readonly HashSet<string> AllowedContent = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public async Task<(bool Success, string? ErrorMessage, FfcAttachment? Attachment)> SaveAsync(
        long recordId,
        IFormFile file,
        FfcAttachmentKind kind,
        string? caption)
    {
        if (!IsAdminOrHod(_userContext.User))
        {
            return (false, AuthorizationError, null);
        }

        if (recordId <= 0)
        {
            return (false, "Invalid FFC record.", null);
        }

        if (file is null || file.Length <= 0)
        {
            return (false, "Select a file.", null);
        }

        if (!AllowedContent.Contains(file.ContentType))
        {
            return (false, "Only PDF/JPEG/PNG/WEBP allowed.", null);
        }

        if (_options.MaxFileSizeBytes > 0 && file.Length > _options.MaxFileSizeBytes)
        {
            return (
                false,
                $"File exceeds maximum size of {FileSizeFormatter.FormatFileSize(_options.MaxFileSizeBytes)}.",
                null);
        }

        var recordExists = await _db.FfcRecords
            .AsNoTracking()
            .AnyAsync(record => record.Id == recordId && !record.IsDeleted);

        if (!recordExists)
        {
            return (false, "The FFC record was not found or has been archived.", null);
        }

        var resolvedKind = ResolveKind(file.ContentType);
        if (!resolvedKind.HasValue)
        {
            return (false, "The file type could not be determined.", null);
        }

        // Do not trust a caller-supplied kind when the validated content type says otherwise.
        kind = resolvedKind.Value;

        var tempPath = Path.Combine(Path.GetTempPath(), $"ffc-{Guid.NewGuid():N}.upload");
        string? finalPath = null;

        try
        {
            await using (var target = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await file.CopyToAsync(target);
                await target.FlushAsync();
            }

            if (!await _validator.IsSafeAsync(tempPath, file.ContentType))
            {
                return (false, "File failed security checks.", null);
            }

            var checksum = await ComputeSha256Async(tempPath);
            var duplicateExists = await _db.FfcAttachments
                .AsNoTracking()
                .AnyAsync(attachment =>
                    attachment.FfcRecordId == recordId &&
                    attachment.ChecksumSha256 == checksum);

            if (duplicateExists)
            {
                return (false, "This file is already attached to the selected FFC record.", null);
            }

            var extension = ResolveSafeExtension(file.ContentType);
            var fileName = $"{Guid.NewGuid():N}{extension}";
            Directory.CreateDirectory(_storageRoot);
            finalPath = Path.Combine(_storageRoot, fileName);
            File.Move(tempPath, finalPath, overwrite: false);

            var attachment = new FfcAttachment
            {
                FfcRecordId = recordId,
                Kind = kind,
                FilePath = _pathResolver.ToRelative(finalPath),
                ContentType = file.ContentType.Trim().ToLowerInvariant(),
                SizeBytes = file.Length,
                ChecksumSha256 = checksum,
                Caption = NormalizeCaption(caption),
                UploadedByUserId = _userContext.UserId,
                UploadedAt = DateTimeOffset.UtcNow
            };

            _db.FfcAttachments.Add(attachment);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch
            {
                _db.Entry(attachment).State = EntityState.Detached;
                TryDelete(finalPath);
                throw;
            }

            return (true, null, attachment);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public async Task DeleteAsync(FfcAttachment attachment)
    {
        if (!IsAdminOrHod(_userContext.User))
        {
            throw new FfcAttachmentAuthorizationException(AuthorizationError);
        }

        ArgumentNullException.ThrowIfNull(attachment);

        var absolutePath = ResolveAbsolutePath(attachment.FilePath);
        string? quarantinePath = null;

        if (!string.IsNullOrWhiteSpace(absolutePath) && File.Exists(absolutePath))
        {
            quarantinePath = $"{absolutePath}.delete-{Guid.NewGuid():N}.tmp";
            File.Move(absolutePath, quarantinePath, overwrite: false);
        }

        _db.FfcAttachments.Remove(attachment);

        try
        {
            await _db.SaveChangesAsync();
            if (!string.IsNullOrWhiteSpace(quarantinePath))
            {
                TryDelete(quarantinePath);
            }
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(quarantinePath) &&
                File.Exists(quarantinePath) &&
                !string.IsNullOrWhiteSpace(absolutePath) &&
                !File.Exists(absolutePath))
            {
                File.Move(quarantinePath, absolutePath, overwrite: false);
            }

            throw;
        }
    }

    private static bool IsAdminOrHod(ClaimsPrincipal principal)
        => principal.IsInRole("Admin") || principal.IsInRole("HoD");

    private static string ResolveStorageRoot(
        FfcAttachmentOptions options,
        IUploadRootProvider uploadRootProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(uploadRootProvider);

        var folderName = string.IsNullOrWhiteSpace(options.StorageFolderName)
            ? "ffc"
            : options.StorageFolderName.Trim().Trim('/', '\\');

        var configuredRoot = options.StorageRoot;

        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            configuredRoot = string.IsNullOrEmpty(folderName)
                ? uploadRootProvider.RootPath
                : Path.Combine(uploadRootProvider.RootPath, folderName);
        }
        else if (!Path.IsPathRooted(configuredRoot))
        {
            configuredRoot = Path.Combine(uploadRootProvider.RootPath, configuredRoot);
        }

        return Path.GetFullPath(configuredRoot);
    }

    private string ResolveAbsolutePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(storageKey))
        {
            return Path.GetFullPath(storageKey);
        }

        try
        {
            return _pathResolver.ToAbsolute(storageKey);
        }
        catch
        {
            var relative = storageKey
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(_storageRoot, relative);
        }
    }

    private static FfcAttachmentKind? ResolveKind(string? contentType)
        => contentType?.Trim().ToLowerInvariant() switch
        {
            "application/pdf" => FfcAttachmentKind.Pdf,
            "image/jpeg" or "image/png" or "image/webp" => FfcAttachmentKind.Photo,
            _ => null
        };

    private static string ResolveSafeExtension(string contentType)
        => contentType.Trim().ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => string.Empty
        };

    private static string? NormalizeCaption(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return null;
        }

        var normalized = caption.Trim();
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup. Persistent failures are surfaced by the primary operation.
        }
    }
}
