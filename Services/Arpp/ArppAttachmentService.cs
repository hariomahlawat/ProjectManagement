using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Arpp;

public sealed class ArppAttachmentService : IArppAttachmentService
{
    private readonly ApplicationDbContext _db;
    private readonly IArppAttachmentStorage _storage;
    private readonly IDocRepoIngestionService _docRepoIngestionService;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ArppAttachmentOptions _options;
    private readonly ILogger<ArppAttachmentService> _logger;

    public ArppAttachmentService(
        ApplicationDbContext db,
        IArppAttachmentStorage storage,
        IDocRepoIngestionService docRepoIngestionService,
        IAuditService audit,
        IClock clock,
        IOptions<ArppAttachmentOptions> options,
        ILogger<ArppAttachmentService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _docRepoIngestionService = docRepoIngestionService ?? throw new ArgumentNullException(nameof(docRepoIngestionService));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ArppAttachmentCommandResult> UploadOrReplaceAsync(
        long issueId,
        IFormFile? file,
        string userId,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        if (issueId <= 0)
        {
            return ArppAttachmentCommandResult.Failed("A valid ARPP issue is required.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return ArppAttachmentCommandResult.Failed("The current user could not be identified.");
        }

        if (file is null || file.Length <= 0)
        {
            return ArppAttachmentCommandResult.Failed(
                "Select the issued HQ PDF.",
                "UploadFile",
                "Select a non-empty PDF file.");
        }

        var issue = await _db.ArppIssues
            .Include(candidate => candidate.Attachment)
            .SingleOrDefaultAsync(candidate => candidate.Id == issueId, cancellationToken);

        if (issue is null)
        {
            return ArppAttachmentCommandResult.Failed("The ARPP issue was not found.");
        }

        if (issue.IsVerified)
        {
            return ArppAttachmentCommandResult.Failed(
                "This ARPP issue is verified and locked. Unlock it with a recorded reason before replacing the issued PDF.");
        }

        ArppStoredAttachment stored;
        try
        {
            await using var source = file.OpenReadStream();
            stored = await _storage.SaveAsync(
                issueId,
                file.FileName,
                file.ContentType,
                file.Length,
                source,
                cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return ArppAttachmentCommandResult.Failed(
                "The issued document could not be uploaded.",
                "UploadFile",
                exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to store the ARPP attachment for issue {IssueId}.",
                issueId);
            return ArppAttachmentCommandResult.Failed(
                "The issued document could not be stored. No attachment was changed.",
                "UploadFile",
                "Try again or contact the administrator.");
        }

        var existing = issue.Attachment;
        if (existing is not null &&
            string.Equals(existing.Sha256, stored.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            await _storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return ArppAttachmentCommandResult.Succeeded(
                "The selected PDF is already attached to this ARPP issue.");
        }

        var oldStorageKey = existing?.StorageKey;
        var now = _clock.UtcNow.ToUniversalTime();
        var action = existing is null ? "Arpp.AttachmentUploaded" : "Arpp.AttachmentReplaced";

        if (existing is null)
        {
            existing = new ArppAttachment
            {
                ArppIssueId = issueId
            };
            issue.Attachment = existing;
            _db.ArppAttachments.Add(existing);
        }

        existing.StorageKey = stored.StorageKey;
        existing.OriginalFileName = stored.OriginalFileName;
        existing.ContentType = stored.ContentType;
        existing.SizeBytes = stored.SizeBytes;
        existing.Sha256 = stored.Sha256;
        existing.UploadedByUserId = userId;
        existing.UploadedAtUtc = now;
        issue.UpdatedAtUtc = now;
        issue.UpdatedByUserId = userId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return ArppAttachmentCommandResult.Failed(
                "The issued PDF was changed by another user. Reload the page before trying again.");
        }
        catch (Exception exception)
        {
            await _storage.DeleteAsync(stored.StorageKey, cancellationToken);
            _logger.LogError(
                exception,
                "Failed to persist the ARPP attachment for issue {IssueId}.",
                issueId);
            return ArppAttachmentCommandResult.Failed(
                "The issued PDF could not be recorded. The existing attachment was retained.");
        }

        if (!string.IsNullOrWhiteSpace(oldStorageKey) &&
            !string.Equals(oldStorageKey, stored.StorageKey, StringComparison.Ordinal) &&
            !await IsPublishedStorageKeyAsync(oldStorageKey, cancellationToken))
        {
            await _storage.DeleteAsync(oldStorageKey, cancellationToken);
        }

        string? warning = null;
        if (_options.IngestIntoDocumentRepository)
        {
            try
            {
                var documentStream = await _storage.OpenReadAsync(
                    stored.StorageKey,
                    cancellationToken);
                if (documentStream is null)
                {
                    warning = "The PDF was attached, but document indexing could not start because the stored file was unavailable.";
                }
                else
                {
                    await using (documentStream)
                    {
                        await _docRepoIngestionService.IngestExternalPdfAsync(
                            documentStream,
                            stored.OriginalFileName,
                            "ARPP",
                            existing.Id.ToString(CultureInfo.InvariantCulture),
                            cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                warning = "The PDF was attached, but document-repository indexing could not be completed. The file remains available here.";
                _logger.LogError(
                    exception,
                    "Failed to ingest ARPP attachment {AttachmentId} into the document repository.",
                    existing.Id);
            }
        }

        await TryAuditAsync(
            action,
            issue,
            existing,
            userId,
            userName);

        return ArppAttachmentCommandResult.Succeeded(
            oldStorageKey is null ? "Issued HQ PDF attached." : "Issued HQ PDF replaced.",
            warning);
    }

    public async Task<ArppAttachmentCommandResult> DeleteAsync(
        long issueId,
        long attachmentId,
        string userId,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        if (issueId <= 0 || attachmentId <= 0)
        {
            return ArppAttachmentCommandResult.Failed("A valid ARPP attachment is required.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return ArppAttachmentCommandResult.Failed("The current user could not be identified.");
        }

        var attachment = await _db.ArppAttachments
            .Include(candidate => candidate.Issue)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == attachmentId && candidate.ArppIssueId == issueId,
                cancellationToken);

        if (attachment is null)
        {
            return ArppAttachmentCommandResult.Failed("The issued PDF was not found.");
        }

        if (attachment.Issue.IsVerified)
        {
            return ArppAttachmentCommandResult.Failed(
                "This ARPP issue is verified and locked. Unlock it with a recorded reason before removing the issued PDF.");
        }

        var storageKey = attachment.StorageKey;
        var issue = attachment.Issue;
        var now = _clock.UtcNow.ToUniversalTime();
        issue.UpdatedAtUtc = now;
        issue.UpdatedByUserId = userId;
        var auditData = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["IssueId"] = issue.Id.ToString(CultureInfo.InvariantCulture),
            ["AttachmentId"] = attachment.Id.ToString(CultureInfo.InvariantCulture),
            ["FileName"] = attachment.OriginalFileName,
            ["SizeBytes"] = attachment.SizeBytes.ToString(CultureInfo.InvariantCulture),
            ["Sha256"] = attachment.Sha256
        };

        _db.ArppAttachments.Remove(attachment);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ArppAttachmentCommandResult.Failed(
                "The issued PDF was changed by another user. Reload the page before trying again.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to remove ARPP attachment {AttachmentId} from issue {IssueId}.",
                attachmentId,
                issueId);
            return ArppAttachmentCommandResult.Failed(
                "The issued PDF could not be removed. The existing attachment was retained.");
        }

        // A published snapshot may still expose the previously verified PDF while the
        // management workspace is being corrected. Keep that immutable source available.
        if (!await IsPublishedStorageKeyAsync(storageKey, cancellationToken))
        {
            await _storage.DeleteAsync(storageKey, cancellationToken);
        }

        try
        {
            await _audit.LogAsync(
                action: "Arpp.AttachmentDeleted",
                message: $"Removed the issued HQ PDF from {issue.Name}.",
                userId: userId,
                userName: userName,
                data: auditData);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to write the ARPP attachment deletion audit for issue {IssueId}.",
                issueId);
        }

        return ArppAttachmentCommandResult.Succeeded("Issued HQ PDF removed.");
    }

    public async Task<ArppAttachmentDownload?> OpenDownloadAsync(
        long issueId,
        CancellationToken cancellationToken = default)
    {
        if (issueId <= 0)
        {
            return null;
        }

        var attachment = await _db.ArppAttachments
            .AsNoTracking()
            .Where(candidate => candidate.ArppIssueId == issueId)
            .Select(candidate => new
            {
                candidate.StorageKey,
                candidate.OriginalFileName,
                candidate.ContentType,
                candidate.SizeBytes
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (attachment is null)
        {
            return null;
        }

        var stream = await _storage.OpenReadAsync(attachment.StorageKey, cancellationToken);
        if (stream is null)
        {
            _logger.LogError(
                "ARPP attachment metadata exists for issue {IssueId}, but the stored file '{StorageKey}' is missing.",
                issueId,
                attachment.StorageKey);
            return null;
        }

        return new ArppAttachmentDownload(
            stream,
            attachment.ContentType,
            attachment.OriginalFileName,
            attachment.SizeBytes);
    }


    private Task<bool> IsPublishedStorageKeyAsync(
        string storageKey,
        CancellationToken cancellationToken)
        => _db.ArppPublishedIssues
            .AsNoTracking()
            .AnyAsync(snapshot => snapshot.AttachmentStorageKey == storageKey, cancellationToken);

    private async Task TryAuditAsync(
        string action,
        ArppIssue issue,
        ArppAttachment attachment,
        string userId,
        string? userName)
    {
        try
        {
            await _audit.LogAsync(
                action: action,
                message: $"Attached {attachment.OriginalFileName} to {issue.Name}.",
                userId: userId,
                userName: userName,
                data: new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["IssueId"] = issue.Id.ToString(CultureInfo.InvariantCulture),
                    ["AttachmentId"] = attachment.Id.ToString(CultureInfo.InvariantCulture),
                    ["FinancialYear"] = FinancialYearHelper.Format(issue.FinancialYearStart),
                    ["FileName"] = attachment.OriginalFileName,
                    ["SizeBytes"] = attachment.SizeBytes.ToString(CultureInfo.InvariantCulture),
                    ["Sha256"] = attachment.Sha256
                });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to write the ARPP attachment audit for issue {IssueId}.",
                issue.Id);
        }
    }
}
