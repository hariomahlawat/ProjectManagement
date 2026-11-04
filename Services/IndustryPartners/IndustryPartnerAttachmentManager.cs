using System;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models.IndustryPartners;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Services.IndustryPartners;

public sealed class IndustryPartnerAttachmentManager : IIndustryPartnerAttachmentManager
{
    private readonly ApplicationDbContext _db;
    private readonly IIndustryPartnerAttachmentStorage _storage;
    private readonly IndustryPartnerAttachmentValidator _validator;
    private readonly IFileScanner _scanner;
    private readonly ILogger<IndustryPartnerAttachmentManager> _logger;

    public IndustryPartnerAttachmentManager(ApplicationDbContext db,
        IIndustryPartnerAttachmentStorage storage,
        IndustryPartnerAttachmentValidator validator,
        IFileScanner scanner,
        ILogger<IndustryPartnerAttachmentManager>? logger = null)
    {
        _db = db;
        _storage = storage;
        _validator = validator;
        _scanner = scanner;
        _logger = logger ?? NullLogger<IndustryPartnerAttachmentManager>.Instance;
    }

    public async Task<Guid> UploadAsync(int partnerId, IFormFile file, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var partner = await _db.IndustryPartners.FirstOrDefaultAsync(x => x.Id == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Industry partner not found.");

        // Some browsers send a full client path; only the file name is kept.
        var originalFileName = Path.GetFileName((file.FileName ?? string.Empty).Replace('\\', '/')).Trim();
        _validator.Validate(originalFileName, file.ContentType, file.Length);
        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        _validator.ValidateContent(file.ContentType, memory);
        await _scanner.ScanOrThrowAsync(memory, cancellationToken);
        memory.Position = 0;
        var sha = Convert.ToHexString(SHA256.HashData(memory)).ToLowerInvariant();
        memory.Position = 0;

        var key = await _storage.SaveAsync(partnerId, originalFileName, memory, cancellationToken);

        var attachment = new IndustryPartnerAttachment
        {
            IndustryPartnerId = partner.Id,
            OriginalFileName = originalFileName,
            StorageKey = key,
            ContentType = file.ContentType,
            SizeBytes = memory.Length,
            Sha256 = sha,
            UploadedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system",
            UploadedUtc = DateTimeOffset.UtcNow
        };

        _db.IndustryPartnerAttachments.Add(attachment);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Without its database row the stored file would be orphaned.
            _db.Entry(attachment).State = EntityState.Detached;
            await TryDeleteStoredFileAsync(key, partnerId);
            throw;
        }

        return attachment.Id;
    }

    public async Task<(Stream Stream, string FileName, string ContentType)> DownloadAsync(int partnerId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await _db.IndustryPartnerAttachments
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.IndustryPartnerId == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Attachment not found.");

        var stream = await _storage.OpenReadAsync(attachment.StorageKey, cancellationToken);
        return (stream, attachment.OriginalFileName, attachment.ContentType);
    }

    public async Task DeleteAsync(int partnerId, Guid attachmentId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var attachment = await _db.IndustryPartnerAttachments
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.IndustryPartnerId == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Attachment not found.");
        _db.IndustryPartnerAttachments.Remove(attachment);
        await _db.SaveChangesAsync(cancellationToken);

        // The record is already removed; a leftover file is logged, not reported as a failure.
        await TryDeleteStoredFileAsync(attachment.StorageKey, partnerId);
    }

    private async Task TryDeleteStoredFileAsync(string storageKey, int partnerId)
    {
        try
        {
            await _storage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(
                exception,
                "Industry partner {PartnerId} attachment file {StorageKey} could not be removed.",
                partnerId,
                storageKey);
        }
    }
}
