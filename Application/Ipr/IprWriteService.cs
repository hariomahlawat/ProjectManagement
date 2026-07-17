using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Utilities;

namespace ProjectManagement.Application.Ipr;

public sealed class IprWriteService : IIprWriteService
{
    private const string ConcurrencyConflictMessage = "The record was modified by another user. Please reload and try again.";
    private const string RowVersionRequiredMessage = "Row version must be supplied for this operation.";
    private const string FilingNumberUniqueConstraintName = "UX_IprRecords_FilingNumber_Type";

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IprAttachmentStorage _storage;
    private readonly IprAttachmentOptions _options;
    private readonly IDocRepoIngestionService _docRepoIngestionService;
    private readonly ILogger<IprWriteService>? _logger;

    public IprWriteService(
    ApplicationDbContext db,
    IClock clock,
    IprAttachmentStorage storage,
    IOptions<IprAttachmentOptions> options,
    IDocRepoIngestionService docRepoIngestionService,
    ILogger<IprWriteService>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _docRepoIngestionService = docRepoIngestionService ?? throw new ArgumentNullException(nameof(docRepoIngestionService));
        _logger = logger;
    }


    public async Task<IprRecord> CreateAsync(IprRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var normalized = NormalizeRecord(record);
        await EnsureProjectAvailableAsync(normalized.ProjectId, cancellationToken);
        await EnsureUniqueFilingNumberAsync(normalized.IprFilingNumber, normalized.Type, null, cancellationToken);
        ValidateStatus(normalized.Status, normalized.FiledAtUtc, normalized.GrantedAtUtc);

        normalized.ClearAttachments();
        normalized.Entity.Id = 0;
        normalized.Entity.RowVersion = Array.Empty<byte>();

        _db.IprRecords.Add(normalized.Entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsFilingNumberUniqueConstraintViolation(ex))
        {
            throw new IprValidationException(
                IprValidationCode.DuplicateFilingNumber,
                "An IPR record with the same filing number and type already exists.",
                ex);
        }

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
        await EnsureProjectAvailableAsync(normalized.ProjectId, cancellationToken);
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
        catch (DbUpdateException ex) when (IsFilingNumberUniqueConstraintViolation(ex))
        {
            throw new IprValidationException(
                IprValidationCode.DuplicateFilingNumber,
                "An IPR record with the same filing number and type already exists.",
                ex);
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

        var storageResult = await _storage.SaveAsync(
            iprRecordId,
            content,
            originalFileName,
            normalizedContentType,
            _options.MaxFileSizeBytes,
            cancellationToken);

        var attachment = new IprAttachment
        {
            IprRecordId = record.Id,
            StorageKey = storageResult.StorageKey,
            OriginalFileName = storageResult.FileName,
            ContentType = storageResult.ContentType,
            FileSize = storageResult.FileSize,
            UploadedByUserId = uploadedByUserId,
            UploadedAtUtc = _clock.UtcNow,
        };

        _db.IprAttachments.Add(attachment);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _storage.Delete(storageResult.StorageKey);
            throw;
        }

        if (string.Equals(storageResult.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await using var pdfStream = await _storage.OpenReadAsync(storageResult.StorageKey, cancellationToken);
                await _docRepoIngestionService.IngestExternalPdfAsync(
                    pdfStream,
                    attachment.OriginalFileName,
                    "IPR",
                    attachment.Id.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to ingest IPR attachment {AttachmentId} into the document repository.", attachment.Id);
            }
        }

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
        var canonical = filingNumber.ToUpperInvariant();
        var exists = await _db.IprRecords
            .AsNoTracking()
            .AnyAsync(
                x => x.IprFilingNumber.ToUpper() == canonical &&
                     x.Type == type &&
                     (!excludeId.HasValue || x.Id != excludeId.Value),
                cancellationToken);

        if (exists)
        {
            throw new IprValidationException(
                IprValidationCode.DuplicateFilingNumber,
                "An IPR record with the same filing number and type already exists.");
        }
    }

    private static bool IsFilingNumberUniqueConstraintViolation(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgres)
        {
            return postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
                string.Equals(postgres.ConstraintName, FilingNumberUniqueConstraintName, StringComparison.Ordinal);
        }

        if (exception.InnerException is DbException dbException)
        {
            if (string.Equals(dbException.GetType().Name, "SqlException", StringComparison.Ordinal))
            {
                var numberProperty = dbException.GetType().GetProperty("Number");
                if (numberProperty?.GetValue(dbException) is int sqlNumber && (sqlNumber == 2601 || sqlNumber == 2627))
                {
                    if (!string.IsNullOrEmpty(dbException.Message) &&
                        dbException.Message.IndexOf(FilingNumberUniqueConstraintName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            var sqlStateProperty = dbException.GetType().GetProperty("SqlState");
            if (sqlStateProperty?.GetValue(dbException) is string sqlState &&
                string.Equals(sqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal))
            {
                var constraintProperty = dbException.GetType().GetProperty("ConstraintName");
                if (constraintProperty?.GetValue(dbException) is string constraintName &&
                    string.Equals(constraintName, FilingNumberUniqueConstraintName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(dbException.Message) &&
                dbException.Message.IndexOf(FilingNumberUniqueConstraintName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(dbException.Source) &&
                dbException.Source.IndexOf(FilingNumberUniqueConstraintName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        var message = exception.InnerException?.Message ?? exception.Message;
        return message.IndexOf(FilingNumberUniqueConstraintName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private async Task EnsureProjectAvailableAsync(int? projectId, CancellationToken cancellationToken)
    {
        if (!projectId.HasValue)
        {
            return;
        }

        var exists = await _db.Projects
            .AsNoTracking()
            .AnyAsync(project => project.Id == projectId.Value && !project.IsDeleted, cancellationToken);

        if (!exists)
        {
            throw new IprValidationException(
                IprValidationCode.ProjectNotAvailable,
                "The selected project is no longer available. Select another project.");
        }
    }

    private void ValidateStatus(IprStatus status, DateTimeOffset? filedAtUtc, DateTimeOffset? grantedAtUtc)
    {
        var todayIst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, TimeZoneHelper.GetIst()).DateTime);
        var filedDate = filedAtUtc.HasValue
            ? DateOnly.FromDateTime(filedAtUtc.Value.UtcDateTime)
            : (DateOnly?)null;
        var grantedDate = grantedAtUtc.HasValue
            ? DateOnly.FromDateTime(grantedAtUtc.Value.UtcDateTime)
            : (DateOnly?)null;

        if (!filedDate.HasValue)
        {
            throw new IprValidationException(
                IprValidationCode.FiledDateRequired,
                "Filed date is required.");
        }

        if (filedDate.Value > todayIst)
        {
            throw new IprValidationException(
                IprValidationCode.FiledDateInFuture,
                "Filed date cannot be in the future.");
        }

        if (status == IprStatus.Granted && !grantedDate.HasValue)
        {
            throw new IprValidationException(
                IprValidationCode.GrantDateRequired,
                "Protection date is required once the record is protected.");
        }

        if (grantedDate.HasValue && grantedDate.Value > todayIst)
        {
            throw new IprValidationException(
                IprValidationCode.GrantDateInFuture,
                "Protection date cannot be in the future.");
        }

        if (grantedDate.HasValue && grantedDate.Value < filedDate.Value)
        {
            throw new IprValidationException(
                IprValidationCode.GrantDateBeforeFilingDate,
                "Protection date cannot be earlier than the filing date.");
        }
    }

    private void EnsureContentTypeAllowed(string contentType)
    {
        if (string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_options.AllowedContentTypes is { Count: > 0 } allowed &&
            !allowed.Any(item => string.Equals(item, contentType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Only PDF attachments are allowed.");
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
            Title = NormalizeTitle(source.Title),
            Notes = string.IsNullOrWhiteSpace(source.Notes) ? null : source.Notes.Trim(),
            Type = source.Type,
            Status = source.Status == IprStatus.FilingUnderProcess ? IprStatus.Filed : source.Status,
            FiledBy = string.IsNullOrWhiteSpace(source.FiledBy) ? null : source.FiledBy.Trim(),
            FiledAtUtc = source.FiledAtUtc?.ToUniversalTime(),
            GrantedAtUtc = source.Status == IprStatus.Granted
                ? source.GrantedAtUtc?.ToUniversalTime()
                : null,
            ProjectId = source.ProjectId > 0 ? source.ProjectId : null,
            RowVersion = source.RowVersion ?? Array.Empty<byte>(),
        };

        return new NormalizedRecord(entity);
    }

    private static string NormalizeFilingNumber(string? filingNumber)
    {
        if (string.IsNullOrWhiteSpace(filingNumber))
        {
            throw new IprValidationException(
                IprValidationCode.FilingNumberRequired,
                "Filing number is required.");
        }

        var segments = filingNumber
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return string.Join(' ', segments).ToUpperInvariant();
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new IprValidationException(
                IprValidationCode.TitleRequired,
                "Title is required.");
        }

        return title.Trim();
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
