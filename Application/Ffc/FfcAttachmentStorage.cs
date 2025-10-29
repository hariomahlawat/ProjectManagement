using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Application.Security;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Application.Ffc;

public class FfcAttachmentStorage(AppDbContext db, IFileSecurityValidator validator, IWebHostEnvironment env) : IFfcAttachmentStorage
{
    private readonly AppDbContext _db = db;
    private readonly IFileSecurityValidator _validator = validator;
    private readonly IWebHostEnvironment _env = env;

    private static readonly HashSet<string> AllowedContent = new(StringComparer.OrdinalIgnoreCase)
        { "application/pdf", "image/jpeg", "image/png", "image/webp" };

    public async Task<(bool Success, string? ErrorMessage)> SaveAsync(long recordId, IFormFile file, FfcAttachmentKind kind, string? caption)
    {
        if (!AllowedContent.Contains(file.ContentType))
            return (false, "Only PDF/JPEG/PNG/WEBP allowed.");

        var tmpPath = Path.GetTempFileName();
        await using (var fs = File.Create(tmpPath))
        {
            await file.CopyToAsync(fs);
        }

        if (!await _validator.IsSafeAsync(tmpPath, file.ContentType))
        {
            File.Delete(tmpPath);
            return (false, "File failed security checks.");
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

        return (true, null);
    }

    public async Task DeleteAsync(FfcAttachment attachment)
    {
        _db.FfcAttachments.Remove(attachment);
        await _db.SaveChangesAsync();

        if (File.Exists(attachment.FilePath))
        {
            File.Delete(attachment.FilePath);
        }
    }
}
