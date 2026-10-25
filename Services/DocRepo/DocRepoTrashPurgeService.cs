using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Services.DocRepo;

public enum DocRepoPurgeOutcome
{
    Purged,
    NotFound,
    FileError
}

public sealed record DocRepoPurgeResult(DocRepoPurgeOutcome Outcome, string? Error = null);

// SECTION: Trash purge
// Permanently deletes trashed repository documents: the file from storage, then the row. Used by
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
    private readonly DocRepoOptions _options;
    private readonly ILogger<DocRepoTrashPurgeService>? _logger;

    public DocRepoTrashPurgeService(
        ApplicationDbContext db,
        IDocStorage storage,
        IDocRepoAuditService audit,
        IClock clock,
        IOptions<DocRepoOptions> options,
        ILogger<DocRepoTrashPurgeService>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    /// <summary>Days a document stays in the trash; 0 or less turns automatic purging off.</summary>
    public int RetentionDays => _options.TrashRetentionDays;

    /// <summary>When a trashed document will be purged automatically, or null if it never will.</summary>
    public DateTime? PurgeDueUtc(DateTime? deletedAtUtc)
        => RetentionDays > 0 && deletedAtUtc.HasValue ? deletedAtUtc.Value.AddDays(RetentionDays) : null;

    public async Task<DocRepoPurgeResult> PurgeAsync(
        Guid documentId,
        string actorUserId,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.IsDeleted, cancellationToken);
        if (document is null)
        {
            return new DocRepoPurgeResult(DocRepoPurgeOutcome.NotFound);
        }

        try
        {
            await _storage.DeleteAsync(document.StoragePath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // File already removed; purge the metadata.
        }
        catch (DirectoryNotFoundException)
        {
            // Directory missing; purge the metadata.
        }
        catch (IOException ex)
        {
            return new DocRepoPurgeResult(DocRepoPurgeOutcome.FileError, ex.Message);
        }

        _db.Documents.Remove(document);
        await _audit.WriteAsync(document.Id, actorUserId, "Purged", details ?? new { document.Id }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return new DocRepoPurgeResult(DocRepoPurgeOutcome.Purged);
    }

    /// <summary>
    /// Purges documents trashed more than RetentionDays ago, oldest first, in batches. Safe to run
    /// twice or in overlapping processes: a document another run already purged is skipped.
    /// Documents without a DeletedAtUtc are never purged automatically.
    /// </summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        if (RetentionDays <= 0)
        {
            return 0;
        }

        var cutoff = _clock.UtcNow.UtcDateTime.AddDays(-RetentionDays);
        var purged = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var due = await _db.Documents
                .AsNoTracking()
                .Where(d => d.IsDeleted && d.DeletedAtUtc != null && d.DeletedAtUtc <= cutoff)
                .OrderBy(d => d.DeletedAtUtc)
                .ThenBy(d => d.Id)
                .Select(d => new { d.Id, d.DeletedAtUtc })
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (due.Count == 0)
            {
                break;
            }

            var progressed = false;
            foreach (var item in due)
            {
                var result = await PurgeAsync(
                    item.Id,
                    SystemActor,
                    new { item.Id, Reason = "TrashRetention", RetentionDays, item.DeletedAtUtc },
                    cancellationToken);

                if (result.Outcome == DocRepoPurgeOutcome.Purged)
                {
                    purged++;
                    progressed = true;
                }
                else if (result.Outcome == DocRepoPurgeOutcome.FileError)
                {
                    _logger?.LogWarning("Trash retention could not delete the file of document {DocumentId}: {Error}", item.Id, result.Error);
                }
            }

            // A batch made only of files that cannot be deleted would repeat forever; stop and
            // let the next run try again.
            if (!progressed || due.Count < BatchSize)
            {
                break;
            }
        }

        return purged;
    }
}
