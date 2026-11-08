using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services;

namespace ProjectManagement.Services.DocRepo;

public enum DocRepoPurgeOutcome
{
    Purged,
    NotFound,
    FileError
}

public sealed record DocRepoPurgeResult(DocRepoPurgeOutcome Outcome, string? Error = null);

/// <summary>
/// One automatic purge run: how many documents were removed, and how many could not be (their
/// file could not be moved, or their row could not be deleted). Those stay in the bin, with their
/// file in place, and the next run tries them again.
/// </summary>
public sealed record DocRepoTrashPurgeRun(int Purged, int Failed);

// SECTION: Trash purge
// Permanently deletes trashed repository documents: the row, then the file (see PurgeAsync). Used by
// the Trash page (one document, by an admin) and by DocRepoTrashPurgeWorker (documents whose
// DeletedAtUtc is older than DocRepo:TrashRetentionDays).
public sealed class DocRepoTrashPurgeService
{
    public const string SystemActor = "system";
    private const int BatchSize = 100;

    private readonly ApplicationDbContext _db;
    private readonly IDocStorage _storage;
    private readonly IDocRepoAuditService _audit;
    private readonly IClock _clock;
    private readonly IOptionsMonitor<DocRepoOptions> _options;
    private readonly ILogger<DocRepoTrashPurgeService>? _logger;
    private readonly IDocRepoCoverService? _covers;

    public DocRepoTrashPurgeService(
        ApplicationDbContext db,
        IDocStorage storage,
        IDocRepoAuditService audit,
        IClock clock,
        IOptionsMonitor<DocRepoOptions> options,
        ILogger<DocRepoTrashPurgeService>? logger = null,
        IDocRepoCoverService? covers = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _covers = covers;
    }

    /// <summary>
    /// Days a document stays in the trash; 0 or less turns automatic purging off. Read live, like
    /// the worker, so a configuration change applies without a restart.
    /// </summary>
    public int RetentionDays => _options.CurrentValue.TrashRetentionDays;

    /// <summary>When a trashed document will be purged automatically, or null if it never will.</summary>
    public DateTime? PurgeDueUtc(DateTime? deletedAtUtc)
        => RetentionDays > 0 && deletedAtUtc.HasValue ? deletedAtUtc.Value.AddDays(RetentionDays) : null;

    /// <param name="trashedOnOrBefore">
    /// For automatic purging: the document is purged only if it is still in the trash with a
    /// trash date on or before this cutoff, so one restored and deleted again since it was
    /// selected keeps its full retention period.
    /// </param>
    /// <remarks>
    /// A purge never loses a document that is not purged:
    /// 1. the file is moved aside to <see cref="QuarantineFolder"/> under the same storage root;
    /// 2. the row (and, through the database's cascades, its tags, text, links and so on) is
    ///    deleted only if it is still in the trash, in one transaction with the audit entry;
    /// 3. the set-aside file is deleted only after that commit.
    /// If the row is not deleted (restored meanwhile, or taken by another run) or the save fails,
    /// the file is moved back. Two overlapping runs purge a document once; the other gets NotFound.
    /// </remarks>
    public async Task<DocRepoPurgeResult> PurgeAsync(
        Guid documentId,
        string actorUserId,
        object? details = null,
        DateTime? trashedOnOrBefore = null,
        CancellationToken cancellationToken = default)
    {
        var document = await StillInTrash(documentId, trashedOnOrBefore)
            .AsNoTracking()
            .Select(d => new { d.Id, d.StoragePath })
            .FirstOrDefaultAsync(cancellationToken);
        if (document is null)
        {
            return new DocRepoPurgeResult(DocRepoPurgeOutcome.NotFound);
        }

        // SECTION: Set the file aside
        // A unique name per attempt, so overlapping runs never collide; the document id in the
        // name lets an operator match a leftover file to its document.
        string? quarantinePath = Path.Combine(
            QuarantineFolder,
            $"{document.Id:N}-{Guid.NewGuid():N}{Path.GetExtension(document.StoragePath)}");
        try
        {
            await _storage.MoveAsync(document.StoragePath, quarantinePath, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // No file (already removed, or set aside by another run): purge the metadata.
            quarantinePath = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked, read-only or not permitted: keep the row so the purge can be retried.
            return new DocRepoPurgeResult(DocRepoPurgeOutcome.FileError, ex.Message);
        }

        // SECTION: Delete the row only if it is still in the trash
        bool deleted;
        try
        {
            deleted = await DeleteRowAsync(
                document.Id, trashedOnOrBefore, actorUserId, details ?? new { document.Id }, cancellationToken);
        }
        catch (Exception ex)
        {
            // This attempt's transaction rolled back, but another run may have purged the row
            // meanwhile: the file follows the row (put back if it is there, or if unknown).
            _db.ChangeTracker.Clear();
            await SettleFileAsync(document.Id, document.StoragePath, quarantinePath);
            if (ex is DbUpdateConcurrencyException)
            {
                // Taken by another run (or restored) between the load and the save.
                return new DocRepoPurgeResult(DocRepoPurgeOutcome.NotFound);
            }

            throw;
        }

        if (!deleted)
        {
            // Restored, re-trashed after the cutoff, or purged by another run meanwhile.
            await SettleFileAsync(document.Id, document.StoragePath, quarantinePath);
            return new DocRepoPurgeResult(DocRepoPurgeOutcome.NotFound);
        }

        // SECTION: Committed; now the file can go
        await DiscardQuarantinedAsync(document.Id, quarantinePath);
        _covers?.DeleteCover(document.Id);
        return new DocRepoPurgeResult(DocRepoPurgeOutcome.Purged);
    }

    /// <summary>Folder, under the storage root, where a file waits while its row is deleted.</summary>
    public const string QuarantineFolder = "purge-quarantine";

    private IQueryable<Document> StillInTrash(Guid documentId, DateTime? trashedOnOrBefore)
    {
        var query = _db.Documents.Where(d => d.Id == documentId && d.IsDeleted);
        if (trashedOnOrBefore.HasValue)
        {
            var cutoff = trashedOnOrBefore.Value;
            query = query.Where(d => d.DeletedAtUtc != null && d.DeletedAtUtc <= cutoff);
        }

        return query;
    }

    // Deletes the row with its audit entry in one transaction, only while it is still in the
    // trash. Returns false when it no longer is, or is gone.
    private async Task<bool> DeleteRowAsync(
        Guid documentId,
        DateTime? trashedOnOrBefore,
        string actorUserId,
        object details,
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
        {
            // In-memory provider (tests): no set-based delete or transactions. A row removed by
            // someone else after this load fails SaveChanges with a concurrency error.
            var tracked = await StillInTrash(documentId, trashedOnOrBefore).FirstOrDefaultAsync(cancellationToken);
            if (tracked is null)
            {
                return false;
            }

            _db.Documents.Remove(tracked);
            _audit.Stage(documentId, actorUserId, "Purged", details);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        // The conditional DELETE locks the row: a Restore that comes later waits for this
        // transaction and then finds nothing to restore; one that committed first leaves no row
        // to delete. The database's cascades remove the dependent rows, as before.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await StillInTrash(documentId, trashedOnOrBefore).ExecuteDeleteAsync(cancellationToken);
        if (rows == 0)
        {
            return false;
        }

        _audit.Stage(documentId, actorUserId, "Purged", details);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // The row was not deleted by this attempt: put the file back if the row is still there,
    // or drop the set-aside copy if another run purged the row.
    private async Task SettleFileAsync(Guid documentId, string storagePath, string? quarantinePath)
    {
        if (quarantinePath is null)
        {
            return;
        }

        if (await RowExistsAsync(documentId))
        {
            await PutBackAsync(documentId, storagePath, quarantinePath);
        }
        else
        {
            await DiscardQuarantinedAsync(documentId, quarantinePath);
        }
    }

    // Never cancelled: once the row's fate is known, the file must follow it.
    private async Task<bool> RowExistsAsync(Guid documentId)
    {
        try
        {
            return await _db.Documents.AsNoTracking().AnyAsync(d => d.Id == documentId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Unknown: assume the row is there, so the file is put back rather than lost.
            _logger?.LogWarning(ex, "Could not check whether document {DocumentId} still exists after a purge attempt.", documentId);
            return true;
        }
    }

    private async Task PutBackAsync(Guid documentId, string storagePath, string? quarantinePath)
    {
        if (quarantinePath is null)
        {
            return;
        }

        try
        {
            await _storage.MoveAsync(quarantinePath, storagePath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Document {DocumentId} was not purged, but its file could not be moved back from {QuarantinePath} to {StoragePath}. Move it back by hand.",
                documentId,
                quarantinePath,
                storagePath);
        }
    }

    private async Task DiscardQuarantinedAsync(Guid documentId, string? quarantinePath)
    {
        if (quarantinePath is null)
        {
            return;
        }

        try
        {
            await _storage.DeleteAsync(quarantinePath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The document is gone; only its set-aside copy is left, which is safe to remove later.
            _logger?.LogWarning(ex, "Document {DocumentId} was purged, but its set-aside file {QuarantinePath} could not be deleted.", documentId, quarantinePath);
        }
    }

    /// <summary>
    /// Purges documents trashed more than RetentionDays ago, oldest first, in batches. Safe to run
    /// twice or in overlapping processes: a document another run already purged is skipped.
    /// Documents without a DeletedAtUtc are never purged automatically.
    /// </summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
        => (await RunExpiredPurgeAsync(cancellationToken)).Purged;

    /// <summary>
    /// As <see cref="PurgeExpiredAsync"/>, and also counts the documents that could not be purged.
    /// A document whose file or row cannot be removed is logged, left in the bin with its file in
    /// place, and skipped for the rest of the run, so it never stops newer due documents.
    /// </summary>
    public async Task<DocRepoTrashPurgeRun> RunExpiredPurgeAsync(CancellationToken cancellationToken = default)
    {
        // One policy per run, even if the setting changes while it runs.
        var retentionDays = RetentionDays;
        if (retentionDays <= 0)
        {
            return new DocRepoTrashPurgeRun(0, 0);
        }

        var cutoff = _clock.UtcNow.UtcDateTime.AddDays(-retentionDays);
        var purged = 0;
        // Documents that could not be purged in this run are skipped by the next batches,
        // so they never block newer due documents; the next run tries them again.
        var failed = new List<Guid>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var due = await _db.Documents
                .AsNoTracking()
                .Where(d => d.IsDeleted && d.DeletedAtUtc != null && d.DeletedAtUtc <= cutoff && !failed.Contains(d.Id))
                .OrderBy(d => d.DeletedAtUtc)
                .ThenBy(d => d.Id)
                .Select(d => new { d.Id, d.DeletedAtUtc })
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (due.Count == 0)
            {
                break;
            }

            foreach (var item in due)
            {
                DocRepoPurgeResult result;
                try
                {
                    result = await PurgeAsync(
                        item.Id,
                        SystemActor,
                        new { item.Id, Reason = "TrashRetention", RetentionDays = retentionDays, item.DeletedAtUtc },
                        cutoff,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The row could not be deleted (a database error other than a concurrent
                    // purge or restore). PurgeAsync has already put the file back and cleared
                    // this attempt's tracked changes; skip the document and carry on.
                    failed.Add(item.Id);
                    _logger?.LogError(ex, "Trash retention could not delete document {DocumentId}; it stays in the bin and will be tried again on the next run.", item.Id);
                    continue;
                }

                if (result.Outcome == DocRepoPurgeOutcome.Purged)
                {
                    purged++;
                }
                else if (result.Outcome == DocRepoPurgeOutcome.FileError)
                {
                    failed.Add(item.Id);
                    _logger?.LogWarning("Trash retention could not delete the file of document {DocumentId}: {Error}", item.Id, result.Error);
                }
            }

            if (due.Count < BatchSize)
            {
                break;
            }
        }

        return new DocRepoTrashPurgeRun(purged, failed.Count);
    }
}
