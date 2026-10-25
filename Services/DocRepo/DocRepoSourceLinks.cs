using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Source-module link removal
// A module that ingested its files into the document repository calls this when those files
// are deleted at the source. The changes are staged on the caller's context so they are saved
// in the same transaction as the source delete.
//
// Concurrency: ingestion links a new source to an existing document by its SHA-256. Both sides
// lock the document row (LockDocumentsAsync) before reading its links, so a document is never
// trashed while another request is linking it. Callers must hold a transaction until they save.
public static class DocRepoSourceLinks
{
    public const string SourceRemovedEvent = "SourceRemoved";
    public const string SourceRestoredEvent = "SourceRestored";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Removes the links from the given source items and moves each ingested document that no
    /// other source still links to into the repository trash (a soft delete an admin can restore
    /// or purge). Documents uploaded directly to the repository are never touched.
    /// </summary>
    /// <returns>The ids of the documents moved to the trash.</returns>
    public static async Task<IReadOnlyList<Guid>> StageRemovalAsync(
        ApplicationDbContext db,
        string sourceModule,
        IReadOnlyCollection<string> sourceItemIds,
        string actorUserId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (string.IsNullOrWhiteSpace(sourceModule))
        {
            throw new ArgumentException("Source module is required.", nameof(sourceModule));
        }

        if (sourceItemIds is not { Count: > 0 })
        {
            return Array.Empty<Guid>();
        }

        var module = sourceModule.Trim();
        var itemIds = sourceItemIds.Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();

        var documentIds = await db.DocRepoExternalLinks
            .AsNoTracking()
            .Where(link => link.SourceModule == module && itemIds.Contains(link.SourceItemId))
            .Select(link => link.DocumentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (documentIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        await LockDocumentsAsync(db, documentIds, cancellationToken);

        var links = await db.DocRepoExternalLinks
            .Include(link => link.Document)
            .ThenInclude(document => document.ExternalLinks)
            .Where(link => link.SourceModule == module && itemIds.Contains(link.SourceItemId))
            .ToListAsync(cancellationToken);

        if (links.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var removedLinkIds = links.Select(link => link.Id).ToHashSet();
        db.DocRepoExternalLinks.RemoveRange(links);

        var trashed = new List<Guid>();
        foreach (var document in links.Select(link => link.Document).DistinctBy(document => document.Id))
        {
            var stillLinked = document.ExternalLinks.Any(link => !removedLinkIds.Contains(link.Id));
            if (stillLinked || !document.IsExternal || document.IsDeleted)
            {
                continue;
            }

            document.IsDeleted = true;
            document.IsActive = false;
            document.DeletedAtUtc = now.UtcDateTime;
            document.DeletedByUserId = actorUserId;
            document.DeleteReason = reason;
            document.UpdatedAtUtc = now.UtcDateTime;
            document.UpdatedByUserId = actorUserId;

            db.DocRepoAudits.Add(new DocRepoAudit
            {
                DocumentId = document.Id,
                ActorUserId = actorUserId,
                EventType = SourceRemovedEvent,
                DetailsJson = JsonSerializer.Serialize(new { SourceModule = module, SourceItemIds = itemIds, Reason = reason }, SerializerOptions),
                OccurredAtUtc = now
            });

            trashed.Add(document.Id);
        }

        return trashed;
    }

    // Audit events that delete or bring back a repository document. The latest one tells why a
    // trashed document is in the trash.
    private static readonly string[] TrashStateEvents =
    {
        SourceRemovedEvent,
        SourceRestoredEvent,
        "SoftDeleted",
        "RestoredFromTrash",
        "Reuploaded"
    };

    /// <summary>
    /// True when the document's latest trash or restore event is a source removal, so the
    /// repository's own deletes (an approved delete request) are never undone by a re-upload.
    /// </summary>
    public static async Task<bool> WasTrashedBySourceRemovalAsync(
        ApplicationDbContext db,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var latest = await db.DocRepoAudits
            .AsNoTracking()
            .Where(audit => audit.DocumentId == documentId && TrashStateEvents.Contains(audit.EventType))
            .OrderByDescending(audit => audit.OccurredAtUtc)
            .ThenByDescending(audit => audit.Id)
            .Select(audit => audit.EventType)
            .FirstOrDefaultAsync(cancellationToken);

        return latest == SourceRemovedEvent;
    }

    /// <summary>
    /// Locks the document rows until the current transaction ends (PostgreSQL only; the
    /// providers used in tests do not run requests concurrently).
    /// </summary>
    public static async Task LockDocumentsAsync(
        ApplicationDbContext db,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken cancellationToken = default)
    {
        if (documentIds.Count == 0 || !db.Database.IsNpgsql())
        {
            return;
        }

        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Locking repository documents requires an open transaction.");
        }

        var ids = documentIds.Distinct().OrderBy(id => id).ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Documents\" WHERE \"Id\" = ANY({ids}) ORDER BY \"Id\" FOR UPDATE",
            cancellationToken);
    }

    /// <summary>
    /// Brings back a document that source removal moved to the trash, because a source links it
    /// again (the same file was uploaded again). Staged on the caller's context.
    /// </summary>
    public static void StageRestore(
        ApplicationDbContext db,
        Document document,
        string actorUserId,
        string sourceModule,
        string sourceItemId,
        DateTimeOffset now)
    {
        document.IsDeleted = false;
        document.IsActive = true;
        document.DeletedAtUtc = null;
        document.DeletedByUserId = null;
        document.DeleteReason = null;
        document.UpdatedAtUtc = now.UtcDateTime;
        document.UpdatedByUserId = actorUserId;

        db.DocRepoAudits.Add(new DocRepoAudit
        {
            DocumentId = document.Id,
            ActorUserId = actorUserId,
            EventType = SourceRestoredEvent,
            DetailsJson = JsonSerializer.Serialize(new { SourceModule = sourceModule, SourceItemId = sourceItemId }, SerializerOptions),
            OccurredAtUtc = now
        });
    }
}
