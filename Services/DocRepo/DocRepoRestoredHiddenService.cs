using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;

namespace ProjectManagement.Services.DocRepo;

// SECTION: Contracts
public sealed record DocRepoRestoredHiddenRow(
    Guid Id,
    string Subject,
    string Office,
    string Category,
    DateTimeOffset RestoredAtUtc,
    string RestoredBy,
    DateTimeOffset? DeactivatedBeforeAtUtc,
    string? DeactivatedBeforeBy);

public enum DocRepoReactivateOutcome
{
    Reactivated,
    NotListed,
    Forbidden
}

public sealed record DocRepoReactivateManyResult(int Reactivated, int NotListed, bool Forbidden = false);

// SECTION: Restored but still hidden
/// <summary>
/// Repository documents that were restored from the trash before second-review PR 2 and are
/// still hidden. Until then, restoring cleared the delete details but left
/// <c>IsActive = false</c>, so readers and search still could not see the document.
///
/// A document is listed when it is not deleted, is inactive, has a <c>RestoredFromTrash</c>
/// audit entry (the trash page has written one for every restore, before and since PR 2), and
/// nobody deactivated it after its latest restore. A deactivation after the restore is a
/// deliberate choice, so that document stays off the list. Restores since PR 2 make the
/// document active, so they only show up here if it was later hidden by some other route.
///
/// Nothing is reactivated automatically: an administrator reviews each one. Both the list and
/// Reactivate need <see cref="Policies.Documents.Purge"/>, the Repository trash's policy.
/// Reactivating saves the change and an <c>Activated</c> audit entry together; the Search V2
/// trigger on "Documents" queues the reindex on save.
/// </summary>
public sealed class DocRepoRestoredHiddenService
{
    public const string RestoredEvent = "RestoredFromTrash";
    public const string DeactivatedEvent = "Deactivated";
    public const string ActivatedEvent = "Activated";

    /// <summary>Written in the audit details so the reactivation can be traced to this list.</summary>
    public const string AuditSource = "Restored but still hidden";

    private readonly ApplicationDbContext _db;
    private readonly IDocRepoAuditService _audit;
    private readonly IAuthorizationService _authorization;
    private readonly IClock _clock;

    public DocRepoRestoredHiddenService(
        ApplicationDbContext db,
        IDocRepoAuditService audit,
        IAuthorizationService authorization,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<bool> CanManageAsync(ClaimsPrincipal user) => IsAllowedAsync(user);

    /// <summary>How many documents the list would show, for the link on the Repository trash.</summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        => (await FindAsync(null, cancellationToken)).Count;

    /// <exception cref="UnauthorizedAccessException">The user may not manage the trash.</exception>
    public async Task<IReadOnlyList<DocRepoRestoredHiddenRow>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (!await IsAllowedAsync(user))
        {
            throw new UnauthorizedAccessException("Only repository administrators can see this list.");
        }

        var found = await FindAsync(null, cancellationToken);
        if (found.Count == 0)
        {
            return Array.Empty<DocRepoRestoredHiddenRow>();
        }

        var ids = found.Keys.ToList();
        var documents = await _db.Documents.AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .Select(d => new
            {
                d.Id,
                d.Subject,
                Office = d.OfficeCategory != null ? d.OfficeCategory.Name : "",
                Category = d.DocumentCategory != null ? d.DocumentCategory.Name : ""
            })
            .ToListAsync(cancellationToken);

        var actorIds = found.Values
            .SelectMany(match => new[] { match.Restored.ActorUserId, match.DeactivatedBefore?.ActorUserId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct()
            .ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToListAsync(cancellationToken);
        var nameById = names.ToDictionary(
            u => u.Id,
            u => !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName! : u.UserName ?? u.Id,
            StringComparer.Ordinal);
        string NameOf(string id) => nameById.TryGetValue(id, out var name) ? name : id;

        return documents
            .Select(d =>
            {
                var match = found[d.Id];
                return new DocRepoRestoredHiddenRow(
                    d.Id,
                    d.Subject,
                    d.Office,
                    d.Category,
                    match.Restored.OccurredAtUtc,
                    NameOf(match.Restored.ActorUserId),
                    match.DeactivatedBefore?.OccurredAtUtc,
                    match.DeactivatedBefore is null ? null : NameOf(match.DeactivatedBefore.ActorUserId));
            })
            .OrderByDescending(row => row.RestoredAtUtc)
            .ThenBy(row => row.Subject, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Makes one listed document visible again, with an audit entry saved alongside.</summary>
    public async Task<DocRepoReactivateOutcome> ReactivateAsync(ClaimsPrincipal user, Guid documentId, CancellationToken cancellationToken = default)
    {
        var result = await ReactivateManyAsync(user, new[] { documentId }, cancellationToken);
        return result.Forbidden
            ? DocRepoReactivateOutcome.Forbidden
            : result.Reactivated == 1 ? DocRepoReactivateOutcome.Reactivated : DocRepoReactivateOutcome.NotListed;
    }

    /// <summary>
    /// Makes the chosen documents visible again. Only documents that are on the list right now
    /// change; any other id (already reactivated, deleted again, deactivated on purpose) is
    /// counted as not listed and left alone. All changes and their audit entries are saved
    /// together, so a repeated request finds nothing left to do.
    /// </summary>
    public async Task<DocRepoReactivateManyResult> ReactivateManyAsync(
        ClaimsPrincipal user,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (!await IsAllowedAsync(user))
        {
            return new DocRepoReactivateManyResult(0, 0, Forbidden: true);
        }

        var requested = documentIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (requested.Count == 0)
        {
            return new DocRepoReactivateManyResult(0, 0);
        }

        var listed = await FindAsync(requested, cancellationToken);
        if (listed.Count == 0)
        {
            return new DocRepoReactivateManyResult(0, requested.Count);
        }

        var ids = listed.Keys.ToList();
        var documents = await _db.Documents
            .Where(d => ids.Contains(d.Id) && !d.IsDeleted && !d.IsActive)
            .ToListAsync(cancellationToken);

        var actorId = ActorId(user);
        var now = _clock.UtcNow.UtcDateTime;
        foreach (var document in documents)
        {
            document.IsActive = true;
            document.UpdatedAtUtc = now;
            document.UpdatedByUserId = actorId;
            _audit.Stage(document.Id, actorId, ActivatedEvent, new
            {
                document.Id,
                Source = AuditSource,
                RestoredAtUtc = listed[document.Id].Restored.OccurredAtUtc
            });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Changed (purged, deleted again) by someone else while this ran; nothing is saved.
            _db.ChangeTracker.Clear();
            return new DocRepoReactivateManyResult(0, requested.Count);
        }

        return new DocRepoReactivateManyResult(documents.Count, requested.Count - documents.Count);
    }

    // SECTION: The rule
    private sealed record AuditMark(Guid DocumentId, string EventType, string ActorUserId, DateTimeOffset OccurredAtUtc, long Id);

    private sealed record Match(AuditMark Restored, AuditMark? DeactivatedBefore);

    /// <summary>
    /// The listed documents (optionally only among <paramref name="onlyIds"/>), each with its
    /// latest restore and the latest deactivation before it, if any.
    /// </summary>
    private async Task<Dictionary<Guid, Match>> FindAsync(IReadOnlyCollection<Guid>? onlyIds, CancellationToken cancellationToken)
    {
        var candidates = _db.Documents.AsNoTracking()
            .Where(d => !d.IsDeleted && !d.IsActive)
            .Where(d => _db.DocRepoAudits.Any(a => a.DocumentId == d.Id && a.EventType == RestoredEvent));
        if (onlyIds is not null)
        {
            candidates = candidates.Where(d => onlyIds.Contains(d.Id));
        }

        var candidateIds = await candidates.Select(d => d.Id).ToListAsync(cancellationToken);
        if (candidateIds.Count == 0)
        {
            return new Dictionary<Guid, Match>();
        }

        var marks = await _db.DocRepoAudits.AsNoTracking()
            .Where(a => a.DocumentId != null && candidateIds.Contains(a.DocumentId.Value)
                && (a.EventType == RestoredEvent || a.EventType == DeactivatedEvent || a.EventType == ActivatedEvent))
            .Select(a => new AuditMark(a.DocumentId!.Value, a.EventType, a.ActorUserId, a.OccurredAtUtc, a.Id))
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, Match>();
        foreach (var group in marks.GroupBy(mark => mark.DocumentId))
        {
            // Same-time rows keep the order they were written in.
            var ordered = group.OrderBy(mark => mark.OccurredAtUtc).ThenBy(mark => mark.Id).ToList();
            var restoreIndex = ordered.FindLastIndex(mark => mark.EventType == RestoredEvent);
            if (restoreIndex < 0)
            {
                continue;
            }

            if (ordered.Skip(restoreIndex + 1).Any(mark => mark.EventType == DeactivatedEvent))
            {
                continue; // Hidden on purpose after the restore.
            }

            // Made inactive on purpose before it was deleted: the last switch before the restore
            // was a deactivation, not a reactivation.
            var lastSwitch = ordered.Take(restoreIndex).LastOrDefault(mark => mark.EventType is DeactivatedEvent or ActivatedEvent);
            var deactivatedBefore = lastSwitch?.EventType == DeactivatedEvent ? lastSwitch : null;
            result[group.Key] = new Match(ordered[restoreIndex], deactivatedBefore);
        }

        return result;
    }

    private async Task<bool> IsAllowedAsync(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return (await _authorization.AuthorizeAsync(user, Policies.Documents.Purge)).Succeeded;
    }

    private static string ActorId(ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.Identity?.Name ?? "system";
}
