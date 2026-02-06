using System;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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

    public IndustryPartnerAttachmentManager(ApplicationDbContext db,
        IIndustryPartnerAttachmentStorage storage,
        IndustryPartnerAttachmentValidator validator,
        IFileScanner scanner)
    {
        _db = db;
        _storage = storage;
        _validator = validator;
        _scanner = scanner;
    }

    public async Task<Guid> UploadAsync(int partnerId, IFormFile file, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var partner = await _db.IndustryPartners.FirstOrDefaultAsync(x => x.Id == partnerId, cancellationToken)
            ?? throw new KeyNotFoundException("Industry partner not found.");

        _validator.Validate(file.FileName, file.ContentType, file.Length);
        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        await _scanner.ScanOrThrowAsync(memory, cancellationToken);
        memory.Position = 0;

        var key = await _storage.SaveAsync(partnerId, file.FileName, memory, cancellationToken);
        memory.Position = 0;
        var sha = Convert.ToHexString(SHA256.HashData(memory)).ToLowerInvariant();

        var attachment = new IndustryPartnerAttachment
        {
            IndustryPartnerId = partner.Id,
            OriginalFileName = file.FileName,
            StorageKey = key,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            Sha256 = sha,
            UploadedByUserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system",
            UploadedUtc = DateTimeOffset.UtcNow
        };

        _db.IndustryPartnerAttachments.Add(attachment);
        await _db.SaveChangesAsync(cancellationToken);
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
        await _storage.DeleteAsync(attachment.StorageKey, cancellationToken);
    }
}
