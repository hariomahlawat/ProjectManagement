using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Security;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Services;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Application.Ffc;

public class FfcAttachmentStorage(
    ApplicationDbContext db,
    IFileSecurityValidator validator,
    IUploadRootProvider uploadRootProvider,
    IUploadPathResolver pathResolver,
    IUserContext userContext,
    IOptions<FfcAttachmentOptions> options) : IFfcAttachmentStorage
{
    // SECTION: Dependencies
    private readonly ApplicationDbContext _db = db;
    private readonly IFileSecurityValidator _validator = validator;
    private readonly IUserContext _userContext = userContext;
    private readonly IUploadPathResolver _pathResolver = pathResolver;
    private readonly FfcAttachmentOptions _options = options.Value;
    private readonly string _storageRoot = ResolveStorageRoot(options.Value, uploadRootProvider);

    // SECTION: Constants
    private const string AuthorizationError = "Only Admin or HoD roles can manage attachments.";

    private static readonly HashSet<string> AllowedContent = new(StringComparer.OrdinalIgnoreCase)
        { "application/pdf", "image/jpeg", "image/png", "image/webp" };

    public async Task<(bool Success, string? ErrorMessage, FfcAttachment? Attachment)> SaveAsync(long recordId, IFormFile file, FfcAttachmentKind kind, string? caption)
    {
        // SECTION: Authorisation guard
        if (!IsAdminOrHod(_userContext.User))
        {
            return (false, AuthorizationError, null);
        }

        if (!AllowedContent.Contains(file.ContentType))
            return (false, "Only PDF/JPEG/PNG/WEBP allowed.", null);

        if (_options.MaxFileSizeBytes > 0 && file.Length > _options.MaxFileSizeBytes)
        {
            return (false, $"File exceeds maximum size of {FileSizeFormatter.FormatFileSize(_options.MaxFileSizeBytes)}.", null);
        }

        // SECTION: Persist temp copy for scanning
        var tmpPath = Path.GetTempFileName();
        await using (var fs = File.Create(tmpPath))
        {
            await file.CopyToAsync(fs);
        }

        if (!await _validator.IsSafeAsync(tmpPath, file.ContentType))
        {
            File.Delete(tmpPath);
            return (false, "File failed security checks.", null);
        }

        // SECTION: Promote scanned file into storage
        var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        Directory.CreateDirectory(_storageRoot);
        var finalPath = Path.Combine(_storageRoot, fileName);
        File.Move(tmpPath, finalPath, true);

        var storageKey = _pathResolver.ToRelative(finalPath);

        var attachment = new FfcAttachment
        {
            FfcRecordId = recordId,
            Kind = kind,
            FilePath = storageKey,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            UploadedAt = DateTimeOffset.UtcNow
        };

        _db.FfcAttachments.Add(attachment);
        await _db.SaveChangesAsync();

        return (true, null, attachment);
    }

    // SECTION: Deletion
    public async Task DeleteAsync(FfcAttachment attachment)
    {
        if (!IsAdminOrHod(_userContext.User))
        {
            throw new FfcAttachmentAuthorizationException(AuthorizationError);
        }

        _db.FfcAttachments.Remove(attachment);
        await _db.SaveChangesAsync();

        var absolutePath = ResolveAbsolutePath(attachment.FilePath);
        if (!string.IsNullOrWhiteSpace(absolutePath) && File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }
    }

    private static bool IsAdminOrHod(ClaimsPrincipal principal)
    {
        return principal.IsInRole("Admin") || principal.IsInRole("HoD");
    }

    // SECTION: Helpers
    private static string ResolveStorageRoot(FfcAttachmentOptions options, IUploadRootProvider uploadRootProvider)
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
            var relative = storageKey.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(_storageRoot, relative);
        }
    }

}
