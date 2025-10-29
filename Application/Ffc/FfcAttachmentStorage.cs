using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Security;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Application.Ffc;

public class FfcAttachmentStorage(
    ApplicationDbContext db,
    IFileSecurityValidator validator,
    IWebHostEnvironment env,
    IUserContext userContext,
    IOptions<FfcAttachmentOptions> options) : IFfcAttachmentStorage
{
    private readonly ApplicationDbContext _db = db;
    private readonly IFileSecurityValidator _validator = validator;
    private readonly IWebHostEnvironment _env = env;
    private readonly IUserContext _userContext = userContext;
    private readonly FfcAttachmentOptions _options = options.Value;

    private const string AuthorizationError = "Only Admin or HoD roles can manage attachments.";

    private static readonly HashSet<string> AllowedContent = new(StringComparer.OrdinalIgnoreCase)
        { "application/pdf", "image/jpeg", "image/png", "image/webp" };

    public async Task<(bool Success, string? ErrorMessage, FfcAttachment? Attachment)> SaveAsync(long recordId, IFormFile file, FfcAttachmentKind kind, string? caption)
    {
        if (!IsAdminOrHod(_userContext.User))
        {
            return (false, AuthorizationError, null);
        }

        if (!AllowedContent.Contains(file.ContentType))
            return (false, "Only PDF/JPEG/PNG/WEBP allowed.", null);

        if (_options.MaxFileSizeBytes > 0 && file.Length > _options.MaxFileSizeBytes)
        {
            return (false, $"File exceeds maximum size of {FormatFileSize(_options.MaxFileSizeBytes)}.", null);
        }

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

        var storeRoot = Path.Combine(_env.ContentRootPath, "App_Data", "ffc");
        Directory.CreateDirectory(storeRoot);

        var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
        var finalPath = Path.Combine(storeRoot, fileName);
        File.Move(tmpPath, finalPath, true);

        var attachment = new FfcAttachment
        {
            FfcRecordId = recordId,
            Kind = kind,
            FilePath = finalPath,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            UploadedAt = DateTimeOffset.UtcNow
        };

        _db.FfcAttachments.Add(attachment);
        await _db.SaveChangesAsync();

        return (true, null, attachment);
    }

    public async Task DeleteAsync(FfcAttachment attachment)
    {
        if (!IsAdminOrHod(_userContext.User))
        {
            throw new FfcAttachmentAuthorizationException(AuthorizationError);
        }

        _db.FfcAttachments.Remove(attachment);
        await _db.SaveChangesAsync();

        if (File.Exists(attachment.FilePath))
        {
            File.Delete(attachment.FilePath);
        }
    }

    private static bool IsAdminOrHod(ClaimsPrincipal principal)
    {
        return principal.IsInRole("Admin") || principal.IsInRole("HoD");
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} B", bytes);
        }

        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Format(CultureInfo.InvariantCulture, unit == 0 ? "{0} {1}" : "{0:0.#} {1}", value, units[unit]);
    }
}
