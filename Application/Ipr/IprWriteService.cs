using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Application.Ipr;

public sealed class IprWriteService : IIprWriteService
{
    private const string ConcurrencyConflictMessage = "The record was modified by another user. Please reload and try again.";
    private const string RowVersionRequiredMessage = "Row version must be supplied for this operation.";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IprAttachmentStorage _storage;
    private readonly IprAttachmentOptions _options;
    private readonly ILogger<IprWriteService>? _logger;

    public IprWriteService(
        ApplicationDbContext db,
        IClock clock,
        IprAttachmentStorage storage,
        IOptions<IprAttachmentOptions> options,
        ILogger<IprWriteService>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    public async Task<IprRecord> CreateAsync(IprRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var normalized = NormalizeRecord(record);
        await EnsureUniqueFilingNumberAsync(normalized.IprFilingNumber, normalized.Type, null, cancellationToken);
        ValidateStatus(normalized.Status, normalized.FiledAtUtc, normalized.GrantedAtUtc);

        normalized.ClearAttachments();
        normalized.Entity.Id = 0;
        normalized.Entity.RowVersion = Array.Empty<byte>();

        _db.IprRecords.Add(normalized.Entity);
        await _db.SaveChangesAsync(cancellationToken);

        return normalized.Entity;
    }

    public async Task<IprRecord?> UpdateAsync(IprRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var existing = await _db.IprRecords.FirstOrDefaultAsync(x => x.Id == record.Id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (record.RowVersion is not { Length: > 0 })
        {
            throw new InvalidOperationException(RowVersionRequiredMessage);
        }

        var normalized = NormalizeRecord(record);
        await EnsureUniqueFilingNumberAsync(normalized.IprFilingNumber, normalized.Type, record.Id, cancellationToken);
        ValidateStatus(normalized.Status, normalized.FiledAtUtc, normalized.GrantedAtUtc);

        _db.Entry(existing).Property(x => x.RowVersion).OriginalValue = record.RowVersion;

        existing.IprFilingNumber = normalized.IprFilingNumber;
        existing.Title = normalized.Title;
        existing.Notes = normalized.Notes;
        existing.Type = normalized.Type;
        existing.Status = normalized.Status;
        existing.FiledBy = normalized.FiledBy;
        existing.FiledAtUtc = normalized.FiledAtUtc;
        existing.GrantedAtUtc = normalized.GrantedAtUtc;
        existing.ProjectId = normalized.ProjectId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger?.LogWarning(ex, "Concurrency conflict when updating IPR record {RecordId}.", record.Id);
            throw new InvalidOperationException(ConcurrencyConflictMessage, ex);
        }

        return existing;
    }

    public async Task<bool> DeleteAsync(int id, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        if (rowVersion is not { Length: > 0 })
        {
            throw new InvalidOperationException(RowVersionRequiredMessage);
        }

        var record = await _db.IprRecords
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (record is null)
        {
            return false;
        }

        var attachmentKeys = record.Attachments.Select(a => a.StorageKey).ToList();
        _db.Entry(record).Property(x => x.RowVersion).OriginalValue = rowVersion;
        _db.IprRecords.Remove(record);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger?.LogWarning(ex, "Concurrency conflict when deleting IPR record {RecordId}.", id);
            throw new InvalidOperationException(ConcurrencyConflictMessage, ex);
        }

        foreach (var key in attachmentKeys)
        {
            _storage.Delete(key);
        }

        return true;
    }

    public async Task<IprAttachment> AddAttachmentAsync(
        int iprRecordId,
        Stream content,
        string originalFileName,
        string? contentType,
        string uploadedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(uploadedByUserId))
        {
            throw new ArgumentException("Uploaded by user id is required.", nameof(uploadedByUserId));
        }

        uploadedByUserId = uploadedByUserId.Trim();

        var record = await _db.IprRecords.FirstOrDefaultAsync(x => x.Id == iprRecordId, cancellationToken)
            ?? throw new InvalidOperationException($"IPR record {iprRecordId} was not found.");

        var normalizedContentType = NormalizeContentType(contentType);
        EnsureContentTypeAllowed(normalizedContentType);

        var storageResult = await _storage.SaveAsync(iprRecordId, content, originalFileName, _options.MaxFileSizeBytes, cancellationToken);

        var attachment = new IprAttachment
        {
            IprRecordId = record.Id,
            StorageKey = storageResult.StorageKey,
            OriginalFileName = storageResult.FileName,
            ContentType = normalizedContentType,
            FileSize = storageResult.FileSize,
            UploadedByUserId = uploadedByUserId,
            UploadedAtUtc = _clock.UtcNow,
        };

        _db.IprAttachments.Add(attachment);
        await _db.SaveChangesAsync(cancellationToken);

        return attachment;
    }

    public async Task<bool> DeleteAttachmentAsync(int attachmentId, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        if (rowVersion is not { Length: > 0 })
        {
            throw new InvalidOperationException(RowVersionRequiredMessage);
        }

        var attachment = await _db.IprAttachments.FirstOrDefaultAsync(x => x.Id == attachmentId, cancellationToken);
        if (attachment is null)
        {
            return false;
        }

        var storageKey = attachment.StorageKey;
        _db.Entry(attachment).Property(x => x.RowVersion).OriginalValue = rowVersion;
        _db.IprAttachments.Remove(attachment);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger?.LogWarning(ex, "Concurrency conflict when deleting attachment {AttachmentId}.", attachmentId);
            throw new InvalidOperationException(ConcurrencyConflictMessage, ex);
        }

        _storage.Delete(storageKey);
        return true;
    }

    private async Task EnsureUniqueFilingNumberAsync(string filingNumber, IprType type, int? excludeId, CancellationToken cancellationToken)
    {
        var exists = await _db.IprRecords
            .AsNoTracking()
            .AnyAsync(x => x.IprFilingNumber == filingNumber && x.Type == type && (!excludeId.HasValue || x.Id != excludeId.Value), cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException("An IPR with the same filing number and type already exists.");
        }
    }

    private void ValidateStatus(IprStatus status, DateTimeOffset? filedAtUtc, DateTimeOffset? grantedAtUtc)
    {
        var now = _clock.UtcNow;
        if (filedAtUtc.HasValue && filedAtUtc.Value > now)
        {
            throw new InvalidOperationException("Filed date cannot be in the future.");
        }

        if (grantedAtUtc.HasValue && grantedAtUtc.Value > now)
        {
            throw new InvalidOperationException("Grant date cannot be in the future.");
        }

        if (status != IprStatus.FilingUnderProcess && filedAtUtc is null)
        {
            throw new InvalidOperationException("Filed date is required once the record is not under filing.");
        }

        if (status == IprStatus.Granted && grantedAtUtc is null)
        {
            throw new InvalidOperationException("Grant date is required once the record is granted.");
        }

        if (grantedAtUtc.HasValue && filedAtUtc is null)
        {
            throw new InvalidOperationException("Grant date cannot be provided without a filing date.");
        }

        if (grantedAtUtc.HasValue && filedAtUtc.HasValue && grantedAtUtc.Value < filedAtUtc.Value)
        {
            throw new InvalidOperationException("Grant date cannot be earlier than the filing date.");
        }
    }

    private void EnsureContentTypeAllowed(string contentType)
    {
        if (_options.AllowedContentTypes is { Count: > 0 } allowed && !allowed.Contains(contentType))
        {
            throw new InvalidOperationException($"Attachments of type '{contentType}' are not allowed.");
        }
    }

    private static string NormalizeContentType(string? contentType)
    {
        var normalized = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType.Trim();
        return normalized;
    }

    private static NormalizedRecord NormalizeRecord(IprRecord source)
    {
        var entity = new IprRecord
        {
            Id = source.Id,
            IprFilingNumber = NormalizeFilingNumber(source.IprFilingNumber),
            Title = string.IsNullOrWhiteSpace(source.Title) ? null : source.Title.Trim(),
            Notes = string.IsNullOrWhiteSpace(source.Notes) ? null : source.Notes.Trim(),
            Type = source.Type,
            Status = source.Status,
            FiledBy = string.IsNullOrWhiteSpace(source.FiledBy) ? null : source.FiledBy.Trim(),
            FiledAtUtc = source.FiledAtUtc?.ToUniversalTime(),
            GrantedAtUtc = source.GrantedAtUtc?.ToUniversalTime(),
            ProjectId = source.ProjectId > 0 ? source.ProjectId : null,
            RowVersion = source.RowVersion ?? Array.Empty<byte>(),
        };

        return new NormalizedRecord(entity);
    }

    private static string NormalizeFilingNumber(string? filingNumber)
    {
        if (string.IsNullOrWhiteSpace(filingNumber))
        {
            throw new InvalidOperationException("Filing number is required.");
        }

        return filingNumber.Trim();
    }

    private sealed class NormalizedRecord
    {
        public NormalizedRecord(IprRecord entity)
        {
            Entity = entity;
        }

        public IprRecord Entity { get; }

        public string IprFilingNumber => Entity.IprFilingNumber;
        public string? Title => Entity.Title;
        public string? Notes => Entity.Notes;
        public IprType Type => Entity.Type;
        public IprStatus Status => Entity.Status;
        public string? FiledBy => Entity.FiledBy;
        public DateTimeOffset? FiledAtUtc => Entity.FiledAtUtc;
        public DateTimeOffset? GrantedAtUtc => Entity.GrantedAtUtc;
        public int? ProjectId => Entity.ProjectId;

        public void ClearAttachments()
        {
            Entity.Attachments.Clear();
        }
    }
}
