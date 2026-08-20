using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Models;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaPersonUserLinkInfo(
    Guid LinkId,
    Guid PersonId,
    string PersonDisplayName,
    string UserId,
    string UserName,
    string UserDisplayName,
    string UserRank,
    DateTimeOffset LinkedAtUtc,
    string LinkedByUserId,
    bool HasPortrait,
    bool UserIsActive = true);

public sealed record MediaUserPhotoIdentityLink(
    Guid PersonId,
    string PersonDisplayName,
    bool HasPortrait);

public sealed record MediaPrismUserOption(
    string UserId,
    string UserName,
    string DisplayName,
    string Rank,
    bool AlreadyLinked,
    Guid? LinkedPersonId,
    string? LinkedPersonName);

public interface IMediaPersonUserLinkService
{
    Task<MediaPersonUserLinkInfo?> GetForPersonAsync(Guid personId, CancellationToken cancellationToken);
    Task<MediaPersonUserLinkInfo?> GetForUserAsync(string userId, CancellationToken cancellationToken);
    Task<MediaUserPhotoIdentityLink?> GetPhotoIdentityForUserAsync(string userId, CancellationToken cancellationToken);
    Task<MediaUserPhotoIdentityLink?> TryGetPhotoIdentityForUserAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MediaPrismUserOption>> SearchUsersAsync(string? query, int limit, CancellationToken cancellationToken);
    Task<MediaPersonUserLinkInfo> LinkAsync(Guid personId, string userId, string linkedByUserId, CancellationToken cancellationToken);
    Task UnlinkAsync(Guid personId, string unlinkedByUserId, string reason, CancellationToken cancellationToken);
}

/// <summary>
/// Governs explicit PRISM account ↔ Media Person linkage. The media database intentionally
/// stores the application user id without a cross-context FK; every mutation validates the
/// account against ApplicationDbContext first and is audited in MediaIdentityAudits.
/// </summary>
public sealed class MediaPersonUserLinkService : IMediaPersonUserLinkService
{
    private readonly MediaLibraryDbContext _media;
    private readonly ApplicationDbContext _app;
    private readonly ILogger<MediaPersonUserLinkService> _logger;

    public MediaPersonUserLinkService(
        MediaLibraryDbContext media,
        ApplicationDbContext app,
        ILogger<MediaPersonUserLinkService> logger)
    {
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaPersonUserLinkInfo?> GetForPersonAsync(Guid personId, CancellationToken cancellationToken)
    {
        if (personId == Guid.Empty) return null;
        var row = await AllActiveLinksQuery()
            .Where(link => link.MediaPersonId == personId)
            .Select(link => new LinkProjection
            {
                LinkId = link.Id,
                PersonId = link.MediaPersonId,
                PersonDisplayName = link.MediaPerson.DisplayName,
                UserId = link.UserId,
                LinkedAtUtc = link.LinkedAtUtc,
                LinkedByUserId = link.LinkedByUserId,
                HasPortrait = link.MediaPerson.RepresentativeFaceId.HasValue
                              && link.MediaPerson.FaceAssignments.Any(assignment =>
                                  assignment.RemovedAtUtc == null
                                  && assignment.MediaFaceId == link.MediaPerson.RepresentativeFaceId.Value
                                  && !assignment.MediaFace.IsSuppressed
                                  && assignment.MediaFace.ReviewThumbnailPath != null)
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : await EnrichAsync(row, cancellationToken);
    }

    public async Task<MediaPersonUserLinkInfo?> GetForUserAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        var normalized = userId.Trim();
        var row = await AllActiveLinksQuery()
            .Where(link => link.UserId == normalized)
            .Select(link => new LinkProjection
            {
                LinkId = link.Id,
                PersonId = link.MediaPersonId,
                PersonDisplayName = link.MediaPerson.DisplayName,
                UserId = link.UserId,
                LinkedAtUtc = link.LinkedAtUtc,
                LinkedByUserId = link.LinkedByUserId,
                HasPortrait = link.MediaPerson.RepresentativeFaceId.HasValue
                              && link.MediaPerson.FaceAssignments.Any(assignment =>
                                  assignment.RemovedAtUtc == null
                                  && assignment.MediaFaceId == link.MediaPerson.RepresentativeFaceId.Value
                                  && !assignment.MediaFace.IsSuppressed
                                  && assignment.MediaFace.ReviewThumbnailPath != null)
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : await EnrichAsync(row, cancellationToken);
    }

    public async Task<MediaUserPhotoIdentityLink?> GetPhotoIdentityForUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        var normalized = userId.Trim();
        return await UsablePhotoLinksQuery()
            .Where(link => link.UserId == normalized)
            .Select(link => new MediaUserPhotoIdentityLink(
                link.MediaPersonId,
                link.MediaPerson.DisplayName,
                link.MediaPerson.RepresentativeFaceId.HasValue
                && link.MediaPerson.FaceAssignments.Any(assignment =>
                    assignment.RemovedAtUtc == null
                    && assignment.MediaFaceId == link.MediaPerson.RepresentativeFaceId.Value
                    && !assignment.MediaFace.IsSuppressed
                    && assignment.MediaFace.ReviewThumbnailPath != null)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<MediaUserPhotoIdentityLink?> TryGetPhotoIdentityForUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetPhotoIdentityForUserAsync(userId, cancellationToken);
        }
        catch (DbException exception)
        {
            // Shell/profile integration is optional enrichment. A Photos database outage
            // must not prevent navigation or access to core account settings.
            _logger.LogDebug(exception, "Photos identity enrichment is unavailable for PRISM user {UserId}.", userId);
            return null;
        }
    }

    public async Task<IReadOnlyList<MediaPrismUserOption>> SearchUsersAsync(
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit, 1, 30);
        var search = query?.Trim();
        var users = _app.Users.AsNoTracking()
            .Where(user => user.AccountKind == UserAccountKind.Human
                           && !user.IsDisabled
                           && !user.PendingDeletion);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            users = users.Where(user =>
                EF.Functions.ILike(user.FullName, pattern)
                || EF.Functions.ILike(user.UserName ?? string.Empty, pattern)
                || EF.Functions.ILike(user.Rank, pattern));
        }

        var userRows = await users
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.UserName)
            .Select(user => new
            {
                user.Id,
                user.UserName,
                user.FullName,
                user.Rank
            })
            .Take(take)
            .ToListAsync(cancellationToken);

        var ids = userRows.Select(user => user.Id).ToArray();
        var linkRows = await AllActiveLinksQuery()
            .Where(link => ids.Contains(link.UserId))
            .Select(link => new
            {
                link.UserId,
                link.MediaPersonId,
                link.MediaPerson.DisplayName
            })
            .ToListAsync(cancellationToken);
        var links = linkRows.ToDictionary(link => link.UserId, StringComparer.Ordinal);

        return userRows.Select(user =>
        {
            links.TryGetValue(user.Id, out var link);
            return new MediaPrismUserOption(
                user.Id,
                user.UserName ?? string.Empty,
                DisplayUser(user.FullName, user.UserName),
                user.Rank ?? string.Empty,
                link is not null,
                link?.MediaPersonId,
                link?.DisplayName);
        }).ToArray();
    }

    public async Task<MediaPersonUserLinkInfo> LinkAsync(
        Guid personId,
        string userId,
        string linkedByUserId,
        CancellationToken cancellationToken)
    {
        ValidateActor(linkedByUserId);
        if (personId == Guid.Empty) throw new ArgumentException("A media person is required.", nameof(personId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A PRISM user is required.", nameof(userId));
        var normalizedUserId = userId.Trim();

        var person = await _media.Persons
            .SingleOrDefaultAsync(item => item.Id == personId, cancellationToken)
            ?? throw new KeyNotFoundException("The selected media person no longer exists.");
        if (person.IsHidden || person.Status != MediaPersonStatus.Confirmed)
        {
            throw new InvalidOperationException("Only an active, confirmed media person can be linked to a PRISM user.");
        }

        var user = await _app.Users.AsNoTracking()
            .Where(item => item.Id == normalizedUserId
                           && item.AccountKind == UserAccountKind.Human
                           && !item.IsDisabled
                           && !item.PendingDeletion)
            .Select(item => new { item.Id, item.UserName, item.FullName, item.Rank })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("The selected PRISM user is no longer active.");

        var active = await _media.PersonUserLinks
            .Where(link => link.UnlinkedAtUtc == null
                           && (link.MediaPersonId == personId || link.UserId == normalizedUserId))
            .ToListAsync(cancellationToken);
        var exact = active.SingleOrDefault(link => link.MediaPersonId == personId && link.UserId == normalizedUserId);
        if (exact is not null)
        {
            return await GetForPersonAsync(person.Id, cancellationToken)
                   ?? throw new InvalidOperationException("The existing PRISM user link could not be reloaded.");
        }
        if (active.Any(link => link.MediaPersonId == personId))
        {
            throw new InvalidOperationException("This media person is already linked to another PRISM user. Unlink that account first.");
        }
        if (active.Any(link => link.UserId == normalizedUserId))
        {
            throw new InvalidOperationException("This PRISM user is already linked to another media person. Unlink that identity first.");
        }

        var now = DateTimeOffset.UtcNow;
        var link = new MediaPersonUserLink
        {
            Id = Guid.NewGuid(),
            MediaPersonId = person.Id,
            UserId = user.Id,
            LinkedByUserId = linkedByUserId.Trim(),
            LinkedAtUtc = now,
            ConcurrencyToken = Guid.NewGuid()
        };
        _media.PersonUserLinks.Add(link);
        _media.IdentityAudits.Add(new MediaIdentityAudit
        {
            PersonId = person.Id,
            NewPersonId = person.Id,
            Action = "PrismUserLinked",
            PerformedByUserId = linkedByUserId.Trim(),
            Notes = $"Linked media identity '{person.DisplayName}' to PRISM user '{DisplayUser(user.FullName, user.UserName)}'.",
            MetadataJson = JsonSerializer.Serialize(new
            {
                UserId = user.Id,
                user.UserName,
                user.FullName,
                user.Rank,
                LinkId = link.Id
            }),
            PerformedAtUtc = now
        });

        try
        {
            await _media.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException(
                "The person or PRISM user was linked by another reviewer while this page was open. Refresh and review the current linkage.",
                exception);
        }

        return await GetForPersonAsync(person.Id, cancellationToken)
               ?? throw new InvalidOperationException("The PRISM user link was saved but could not be reloaded.");
    }

    public async Task UnlinkAsync(
        Guid personId,
        string unlinkedByUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        ValidateActor(unlinkedByUserId);
        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length < 3)
        {
            throw new ArgumentException("Record a brief reason for unlinking the PRISM account.", nameof(reason));
        }
        if (normalizedReason.Length > 1024)
        {
            throw new ArgumentException("The unlink reason is too long.", nameof(reason));
        }

        var link = await _media.PersonUserLinks
            .Include(item => item.MediaPerson)
            .SingleOrDefaultAsync(item => item.MediaPersonId == personId && item.UnlinkedAtUtc == null, cancellationToken)
            ?? throw new KeyNotFoundException("This media person is not currently linked to a PRISM user.");

        var now = DateTimeOffset.UtcNow;
        link.UnlinkedAtUtc = now;
        link.UnlinkedByUserId = unlinkedByUserId.Trim();
        link.UnlinkReason = normalizedReason;
        link.ConcurrencyToken = Guid.NewGuid();
        _media.IdentityAudits.Add(new MediaIdentityAudit
        {
            PersonId = link.MediaPersonId,
            PreviousPersonId = link.MediaPersonId,
            Action = "PrismUserUnlinked",
            PerformedByUserId = unlinkedByUserId.Trim(),
            Notes = $"Unlinked PRISM user '{link.UserId}' from media identity '{link.MediaPerson.DisplayName}'. Reason: {normalizedReason}",
            MetadataJson = JsonSerializer.Serialize(new { link.UserId, LinkId = link.Id, Reason = normalizedReason }),
            PerformedAtUtc = now
        });
        try
        {
            await _media.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException(
                "The PRISM account linkage changed while this page was open. Refresh before unlinking it.",
                exception);
        }
    }

    private IQueryable<MediaPersonUserLink> AllActiveLinksQuery()
        => _media.PersonUserLinks.AsNoTracking()
            .Where(link => link.UnlinkedAtUtc == null);

    private IQueryable<MediaPersonUserLink> UsablePhotoLinksQuery()
        => AllActiveLinksQuery()
            .Where(link => link.MediaPerson.Status == MediaPersonStatus.Confirmed
                           && !link.MediaPerson.IsHidden);

    private async Task<MediaPersonUserLinkInfo?> EnrichAsync(LinkProjection row, CancellationToken cancellationToken)
    {
        var user = await _app.Users.AsNoTracking()
            .Where(item => item.Id == row.UserId
                           && item.AccountKind == UserAccountKind.Human
                           && !item.IsDisabled
                           && !item.PendingDeletion)
            .Select(item => new { item.UserName, item.FullName, item.Rank })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return new MediaPersonUserLinkInfo(
                row.LinkId,
                row.PersonId,
                row.PersonDisplayName,
                row.UserId,
                row.UserId,
                row.UserId,
                string.Empty,
                row.LinkedAtUtc,
                row.LinkedByUserId,
                row.HasPortrait,
                UserIsActive: false);
        }
        return new MediaPersonUserLinkInfo(
            row.LinkId,
            row.PersonId,
            row.PersonDisplayName,
            row.UserId,
            user.UserName ?? string.Empty,
            DisplayUser(user.FullName, user.UserName),
            user.Rank ?? string.Empty,
            row.LinkedAtUtc,
            row.LinkedByUserId,
            row.HasPortrait,
            UserIsActive: true);
    }

    private static string DisplayUser(string? fullName, string? userName)
        => !string.IsNullOrWhiteSpace(fullName)
            ? fullName.Trim()
            : !string.IsNullOrWhiteSpace(userName)
                ? userName.Trim()
                : "PRISM user";

    private static void ValidateActor(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("An authenticated reviewer is required.", nameof(userId));
        }
    }

    private sealed class LinkProjection
    {
        public Guid LinkId { get; init; }
        public Guid PersonId { get; init; }
        public string PersonDisplayName { get; init; } = string.Empty;
        public string UserId { get; init; } = string.Empty;
        public DateTimeOffset LinkedAtUtc { get; init; }
        public string LinkedByUserId { get; init; } = string.Empty;
        public bool HasPortrait { get; init; }
    }
}
