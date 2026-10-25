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
public static class DocRepoSourceLinks
{
    public const string SourceRemovedEvent = "SourceRemoved";

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
}
