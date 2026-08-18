using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Application service for organisation-wide curated albums and editorial captions.
/// Source-derived Collections remain immutable; this service only manages the separate
/// curator-owned album layer. All mutation paths revalidate canonical media visibility.
/// </summary>
public sealed class MediaAlbumService : IMediaAlbumService
{
    public const int MaximumAlbumItems = 250;
    private const int MaximumAlbumNameLength = 160;
    private const int MaximumDescriptionLength = 1024;
    private const int MaximumCaptionLength = 1024;

    private readonly MediaLibraryDbContext _db;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly ILogger<MediaAlbumService> _logger;

    public MediaAlbumService(
        MediaLibraryDbContext db,
        IMediaAssetVisibilityPolicy visibility,
        ILogger<MediaAlbumService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MediaAlbumPage> SearchAsync(
        MediaAlbumListQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Actor);

        var query = _db.Albums.AsNoTracking().AsQueryable();
        if (!request.IncludeArchived)
        {
            query = query.Where(album => !album.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var term = request.Query.Trim().ToLower();
            query = query.Where(album => album.Name.ToLower().Contains(term)
                                         || (album.Description != null && album.Description.ToLower().Contains(term)));
        }

        var visibleAssetIds = _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);

        var totalAlbums = await query.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalAlbums / (double)pageSize));
        var pageNumber = Math.Clamp(request.PageNumber, 1, pageCount);
        var skip = (pageNumber - 1) * pageSize;

        var ordered = request.Sort?.Trim().ToLowerInvariant() switch
        {
            "oldest" => query.OrderBy(album => album.UpdatedAtUtc).ThenBy(album => album.Name),
            "name" => query.OrderBy(album => album.Name).ThenByDescending(album => album.UpdatedAtUtc),
            _ => query.OrderByDescending(album => album.UpdatedAtUtc).ThenBy(album => album.Name)
        };

        var rows = await ordered
            .Skip(skip)
            .Take(pageSize)
            .Select(album => new AlbumRow(
                album.Id,
                album.Name,
                album.Description,
                album.CreatedByUserId,
                album.CreatedAtUtc,
                album.UpdatedAtUtc,
                album.IsArchived,
                album.CoverMediaAssetId != null && visibleAssetIds.Contains(album.CoverMediaAssetId.Value)
                    ? album.CoverMediaAssetId
                    : null,
                album.Items.Count(item => visibleAssetIds.Contains(item.MediaAssetId)),
                album.Items.Count(item => visibleAssetIds.Contains(item.MediaAssetId)
                                          && item.MediaAsset.Kind == MediaAssetKind.Photo),
                album.Items.Count(item => visibleAssetIds.Contains(item.MediaAssetId)
                                          && item.MediaAsset.Kind == MediaAssetKind.Video),
                album.Items
                    .Where(item => visibleAssetIds.Contains(item.MediaAssetId)
                                   && item.MediaAsset.Kind == MediaAssetKind.Photo)
                    .OrderBy(item => item.SortOrder)
                    .ThenBy(item => item.AddedAtUtc)
                    .Select(item => (long?)item.MediaAssetId)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var albumTotals = await query
            .SelectMany(album => album.Items)
            .Where(item => visibleAssetIds.Contains(item.MediaAssetId))
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Photos = group.Count(item => item.MediaAsset.Kind == MediaAssetKind.Photo),
                Videos = group.Count(item => item.MediaAsset.Kind == MediaAssetKind.Video)
            })
            .FirstOrDefaultAsync(cancellationToken);
        var totalVisibleItems = albumTotals?.Total ?? 0;
        var totalPhotos = albumTotals?.Photos ?? 0;
        var totalVideos = albumTotals?.Videos ?? 0;

        var summaries = rows.Select(row => new MediaAlbumSummary(
                row.Id,
                row.Name,
                row.Description,
                row.ItemCount,
                row.PhotoCount,
                row.VideoCount,
                ResolveCover(row.CoverMediaAssetId, row.FallbackCoverMediaAssetId),
                row.CreatedAtUtc,
                row.UpdatedAtUtc,
                row.CreatedByUserId,
                row.IsArchived,
                CanManage(request.Actor, row.CreatedByUserId)))
            .ToList();

        return new MediaAlbumPage(
            summaries,
            totalAlbums,
            totalVisibleItems,
            totalPhotos,
            totalVideos,
            pageNumber,
            pageSize,
            pageNumber > 1,
            totalAlbums > skip + pageSize);
    }

    public async Task<IReadOnlyList<MediaAlbumOption>> GetManageableOptionsAsync(
        MediaAlbumActor actor,
        CancellationToken cancellationToken)
    {
        EnsureActor(actor);
        var visibleAssetIds = _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);

        var query = _db.Albums
            .AsNoTracking()
            .Where(album => !album.IsArchived);
        if (!actor.CanManageAnyAlbum)
        {
            query = query.Where(album => album.CreatedByUserId == actor.UserId);
        }

        return await query
            .OrderBy(album => album.Name)
            .Select(album => new MediaAlbumOption(
                album.Id,
                album.Name,
                album.Items.Count(item => visibleAssetIds.Contains(item.MediaAssetId)),
                album.CreatedByUserId == actor.UserId))
            .ToListAsync(cancellationToken);
    }

    public async Task<MediaAlbumDetails?> GetDetailsAsync(
        Guid albumId,
        MediaAlbumActor actor,
        CancellationToken cancellationToken)
    {
        EnsureActor(actor);
        if (albumId == Guid.Empty) return null;

        var album = await _db.Albums
            .AsNoTracking()
            .Where(item => item.Id == albumId)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Description,
                item.CreatedByUserId,
                item.CreatedAtUtc,
                item.UpdatedAtUtc,
                item.IsArchived,
                item.CoverMediaAssetId,
                item.ConcurrencyToken
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (album is null) return null;

        var visibleAssetIds = _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);

        var visibleItems = await _db.AlbumItems
            .AsNoTracking()
            .Where(item => item.MediaAlbumId == albumId && visibleAssetIds.Contains(item.MediaAssetId))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.AddedAtUtc)
            .ThenBy(item => item.MediaAssetId)
            .Select(item => new { item.MediaAssetId, item.MediaAsset.Kind })
            .ToListAsync(cancellationToken);

        // Capacity is a membership invariant, not a visibility invariant. Unavailable/hidden
        // assets remain album members and therefore continue to consume album capacity.
        var totalMembershipCount = await _db.AlbumItems
            .AsNoTracking()
            .CountAsync(item => item.MediaAlbumId == albumId, cancellationToken);

        return new MediaAlbumDetails(
            album.Id,
            album.Name,
            album.Description,
            album.CreatedByUserId,
            album.CreatedAtUtc,
            album.UpdatedAtUtc,
            album.IsArchived,
            album.CoverMediaAssetId,
            album.ConcurrencyToken,
            CanManage(actor, album.CreatedByUserId),
            visibleItems.Select(item => item.MediaAssetId).ToArray(),
            totalMembershipCount,
            visibleItems.Count,
            visibleItems.Count(item => item.Kind == MediaAssetKind.Photo),
            visibleItems.Count(item => item.Kind == MediaAssetKind.Video));
    }

    public async Task<MediaAlbumMutationResult> CreateAsync(
        string name,
        string? description,
        IReadOnlyCollection<long> assetIds,
        MediaAlbumActor actor,
        CancellationToken cancellationToken)
    {
        EnsureActor(actor);
        var validation = ValidateMetadata(name, description);
        if (validation is not null) return validation;

        var normalizedName = name.Trim();
        if (await ActiveNameExistsAsync(normalizedName, null, cancellationToken))
        {
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.DuplicateName,
                "An active album with this name already exists.");
        }

        var normalizedIds = NormalizeAssetIds(assetIds);
        if (normalizedIds.Count > MaximumAlbumItems)
        {
            return CapacityFailure();
        }

        var eligible = await LoadEligibleAssetsAsync(normalizedIds, cancellationToken);
        if (normalizedIds.Count > 0 && eligible.Count == 0)
        {
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.NoEligibleMedia,
                "None of the selected media is currently available for album curation.");
        }

        var now = DateTimeOffset.UtcNow;
        var album = new MediaAlbum
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            Description = NormalizeOptional(description),
            CreatedByUserId = actor.UserId,
            CreatedAtUtc = now,
            UpdatedByUserId = actor.UserId,
            UpdatedAtUtc = now,
            ConcurrencyToken = Guid.NewGuid()
        };

        long order = 10;
        foreach (var asset in eligible)
        {
            album.Items.Add(new MediaAlbumItem
            {
                MediaAlbumId = album.Id,
                MediaAssetId = asset.Id,
                SortOrder = order,
                AddedByUserId = actor.UserId,
                AddedAtUtc = now
            });
            order += 10;
        }

        album.CoverMediaAssetId = eligible
            .Where(asset => asset.Kind == MediaAssetKind.Photo)
            .Select(asset => (long?)asset.Id)
            .FirstOrDefault();

        _db.Albums.Add(album);
        AddAudit("AlbumCreated", album.Id, null, actor.UserId, new
        {
            album.Name,
            album.Description,
            AddedItems = eligible.Count
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return MediaAlbumMutationResult.Success(
                album.Id,
                eligible.Count == 0
                    ? "Album created."
                    : $"Album created with {eligible.Count} item{(eligible.Count == 1 ? string.Empty : "s")}.",
                eligible.Count);
        }
        catch (DbUpdateException exception) when (LooksLikeDuplicateName(exception))
        {
            _logger.LogInformation(exception, "Album creation rejected because the active album name already exists.");
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.DuplicateName,
                "An active album with this name already exists.");
        }
    }

    public async Task<MediaAlbumMutationResult> AddItemsAsync(
        Guid albumId,
        IReadOnlyCollection<long> assetIds,
        MediaAlbumActor actor,
        CancellationToken cancellationToken)
    {
        EnsureActor(actor);
        var album = await LoadMutableAlbumAsync(albumId, actor, cancellationToken);
        if (album.Result is not null) return album.Result;

        var entity = album.Album!;
        if (entity.IsArchived)
        {
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.InvalidRequest,
                "Restore the album before adding media.",
                entity.Id);
        }

        var normalizedIds = NormalizeAssetIds(assetIds);
        if (normalizedIds.Count == 0)
        {
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.InvalidRequest,
                "Select at least one media item.",
                entity.Id);
        }

        var existingIds = await _db.AlbumItems
            .Where(item => item.MediaAlbumId == entity.Id)
            .Select(item => item.MediaAssetId)
            .ToListAsync(cancellationToken);
        var existing = existingIds.ToHashSet();
        var newIds = normalizedIds.Where(id => !existing.Contains(id)).ToArray();
        if (newIds.Length == 0)
        {
            return MediaAlbumMutationResult.Success(entity.Id, "The selected media is already in this album.");
        }

        if (existing.Count + newIds.Length > MaximumAlbumItems)
        {
            return CapacityFailure(entity.Id);
        }

        var eligible = await LoadEligibleAssetsAsync(newIds, cancellationToken);
        if (eligible.Count == 0)
        {
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.NoEligibleMedia,
                "None of the selected media is currently available for album curation.",
                entity.Id);
        }

        var maxOrder = await _db.AlbumItems
            .Where(item => item.MediaAlbumId == entity.Id)
            .Select(item => (long?)item.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;
        var now = DateTimeOffset.UtcNow;
        var nextOrder = maxOrder + 10;
        foreach (var asset in eligible)
        {
            _db.AlbumItems.Add(new MediaAlbumItem
            {
                MediaAlbumId = entity.Id,
                MediaAssetId = asset.Id,
                SortOrder = nextOrder,
                AddedByUserId = actor.UserId,
                AddedAtUtc = now
            });
            nextOrder += 10;
        }

        if (!entity.CoverMediaAssetId.HasValue)
        {
            entity.CoverMediaAssetId = eligible
                .Where(asset => asset.Kind == MediaAssetKind.Photo)
                .Select(asset => (long?)asset.Id)
                .FirstOrDefault();
        }

        Touch(entity, actor.UserId, now);
        AddAudit("AlbumItemsAdded", entity.Id, null, actor.UserId, new { AssetIds = eligible.Select(asset => asset.Id).ToArray() });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return MediaAlbumMutationResult.Success(
                entity.Id,
                $"Added {eligible.Count} item{(eligible.Count == 1 ? string.Empty : "s")} to {entity.Name}.",
                eligible.Count);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyFailure(entity.Id);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogInformation(exception, "Album {AlbumId} changed while media was being added.", entity.Id);
            return ConcurrencyFailure(entity.Id);
        }
    }

    public async Task<MediaAlbumMutationResult> RemoveItemsAsync(
        Guid albumId,
        IReadOnlyCollection<long> assetIds,
        MediaAlbumActor actor,
        CancellationToken cancellationToken)
    {
        EnsureActor(actor);
        var album = await LoadMutableAlbumAsync(albumId, actor, cancellationToken);
        if (album.Result is not null) return album.Result;
        var entity = album.Album!;

        var ids = NormalizeAssetIds(assetIds);
        if (ids.Count == 0)
        {
            return MediaAlbumMutationResult.Failed(MediaAlbumMutationFailure.InvalidRequest, "Select media to remove.", entity.Id);
        }

        var items = await _db.AlbumItems
            .Where(item => item.MediaAlbumId == entity.Id && ids.Contains(item.MediaAssetId))
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return MediaAlbumMutationResult.Success(entity.Id, "No album items needed removal.");
        }

        _db.AlbumItems.RemoveRange(items);
        if (entity.CoverMediaAssetId.HasValue && ids.Contains(entity.CoverMediaAssetId.Value))
        {
            entity.CoverMediaAssetId = null;
        }
        var now = DateTimeOffset.UtcNow;
        Touch(entity, actor.UserId, now);
        AddAudit("AlbumItemsRemoved", entity.Id, null, actor.UserId, new { AssetIds = items.Select(item => item.MediaAssetId).ToArray() });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return MediaAlbumMutationResult.Success(
                entity.Id,
                $"Removed {items.Count} item{(items.Count == 1 ? string.Empty : "s")} from {entity.Name}.",
                items.Count);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyFailure(entity.Id);
        }
    }

    public async Task<MediaAlbumMutationResult> SetCoverAsync(
        Guid albumId,
        long assetId,
        MediaAlbumActor actor,
        CancellationToken cancellationToken)
    {
        EnsureActor(actor);
        var album = await LoadMutableAlbumAsync(albumId, actor, cancellationToken);
        if (album.Result is not null) return album.Result;
        var entity = album.Album!;

        var isMember = await _db.AlbumItems.AnyAsync(
            item => item.MediaAlbumId == entity.Id && item.MediaAssetId == assetId,
            cancellationToken);
        if (!isMember || !await IsEligiblePhotoAsync(assetId, cancellationToken))
        {
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.InvalidRequest,
                "The selected media is not an available item in this album.",
                entity.Id);
        }

        entity.CoverMediaAssetId = assetId;
        var now = DateTimeOffset.UtcNow;
        Touch(entity, actor.UserId, now);
        AddAudit("AlbumCoverChanged", entity.Id, assetId, actor.UserId, null);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return MediaAlbumMutationResult.Success(entity.Id, "Album cover updated.", 1);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyFailure(entity.Id);
        }
    }

    public async Task<MediaAlbumMutationResult> ReorderAsync(
        Guid albumId,
        IReadOnlyList<long> orderedAssetIds,
        MediaAlbumActor actor,
        CancellationToken cancellationToken)
    {
        EnsureActor(actor);
        var album = await LoadMutableAlbumAsync(albumId, actor, cancellationToken);
        if (album.Result is not null) return album.Result;
        var entity = album.Album!;

        var supplied = orderedAssetIds
            .Where(id => id > 0)
            .Distinct()
            .Take(MaximumAlbumItems)
            .ToArray();
        if (supplied.Length == 0)
        {
            return MediaAlbumMutationResult.Failed(MediaAlbumMutationFailure.InvalidRequest, "No album order was supplied.", entity.Id);
        }

        var items = await _db.AlbumItems
            .Where(item => item.MediaAlbumId == entity.Id)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.AddedAtUtc)
            .ToListAsync(cancellationToken);
        var byId = items.ToDictionary(item => item.MediaAssetId);
        if (supplied.Any(id => !byId.ContainsKey(id)))
        {
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.InvalidRequest,
                "The album changed while it was being organised. Reload and try again.",
                entity.Id);
        }

        var finalOrder = supplied
            .Concat(items.Select(item => item.MediaAssetId).Where(id => !supplied.Contains(id)))
            .ToArray();
        long order = 10;
        foreach (var id in finalOrder)
        {
            byId[id].SortOrder = order;
            order += 10;
        }

        var now = DateTimeOffset.UtcNow;
        Touch(entity, actor.UserId, now);
        AddAudit("AlbumReordered", entity.Id, null, actor.UserId, new { AssetIds = finalOrder });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return MediaAlbumMutationResult.Success(entity.Id, "Album order saved.", supplied.Length);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyFailure(entity.Id);
        }
    }

    public async Task<MediaAlbumMutationResult> UpdateMetadataAsync(
        Guid albumId,
        string name,
        string? description,
        Guid concurrencyToken,
        MediaAlbumActor actor,
        CancellationToken cancellationToken)
    {
        EnsureActor(actor);
        var validation = ValidateMetadata(name, description);
        if (validation is not null) return validation with { AlbumId = albumId };

        var album = await LoadMutableAlbumAsync(albumId, actor, cancellationToken);
        if (album.Result is not null) return album.Result;
        var entity = album.Album!;
        if (entity.ConcurrencyToken != concurrencyToken)
        {
            return ConcurrencyFailure(entity.Id);
        }

        var normalizedName = name.Trim();
        if (await ActiveNameExistsAsync(normalizedName, entity.Id, cancellationToken) && !entity.IsArchived)
        {
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.DuplicateName,
                "An active album with this name already exists.",
                entity.Id);
        }

        var oldName = entity.Name;
        entity.Name = normalizedName;
        entity.Description = NormalizeOptional(description);
        var now = DateTimeOffset.UtcNow;
        Touch(entity, actor.UserId, now);
        AddAudit("AlbumMetadataUpdated", entity.Id, null, actor.UserId, new { PreviousName = oldName, entity.Name, entity.Description });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return MediaAlbumMutationResult.Success(entity.Id, "Album details updated.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyFailure(entity.Id);
        }
        catch (DbUpdateException exception) when (LooksLikeDuplicateName(exception))
        {
            return MediaAlbumMutationResult.Failed(MediaAlbumMutationFailure.DuplicateName, "An active album with this name already exists.", entity.Id);
        }
    }

    public async Task<MediaAlbumMutationResult> SetArchivedAsync(
        Guid albumId,
        bool archived,
        Guid concurrencyToken,
        MediaAlbumActor actor,
        CancellationToken cancellationToken)
    {
        EnsureActor(actor);
        var album = await LoadMutableAlbumAsync(albumId, actor, cancellationToken);
        if (album.Result is not null) return album.Result;
        var entity = album.Album!;
        if (entity.ConcurrencyToken != concurrencyToken)
        {
            return ConcurrencyFailure(entity.Id);
        }

        if (entity.IsArchived == archived)
        {
            return MediaAlbumMutationResult.Success(entity.Id, archived ? "Album is already archived." : "Album is already active.");
        }

        if (!archived && await ActiveNameExistsAsync(entity.Name, entity.Id, cancellationToken))
        {
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.DuplicateName,
                "Another active album now uses this name. Rename this album before restoring it.",
                entity.Id);
        }

        var now = DateTimeOffset.UtcNow;
        entity.IsArchived = archived;
        entity.ArchivedAtUtc = archived ? now : null;
        entity.ArchivedByUserId = archived ? actor.UserId : null;
        Touch(entity, actor.UserId, now);
        AddAudit(archived ? "AlbumArchived" : "AlbumRestored", entity.Id, null, actor.UserId, null);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return MediaAlbumMutationResult.Success(entity.Id, archived ? "Album archived." : "Album restored.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyFailure(entity.Id);
        }
    }

    public async Task<MediaAlbumMutationResult> UpdateEditorialCaptionAsync(
        long assetId,
        string? caption,
        Guid? concurrencyToken,
        MediaAlbumActor actor,
        CancellationToken cancellationToken)
    {
        EnsureActor(actor);
        if (!actor.CanManageAnyAlbum)
        {
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.Forbidden,
                "You are not authorised to edit organisation-wide media captions.");
        }

        var normalized = NormalizeOptional(caption);
        if (normalized?.Length > MaximumCaptionLength)
        {
            return MediaAlbumMutationResult.Failed(MediaAlbumMutationFailure.InvalidRequest, $"Caption cannot exceed {MaximumCaptionLength} characters.");
        }

        var asset = await _visibility
            .Apply(_db.Assets)
            .SingleOrDefaultAsync(item => item.Id == assetId, cancellationToken);
        if (asset is null)
        {
            return MediaAlbumMutationResult.Failed(MediaAlbumMutationFailure.NotFound, "The media item is no longer available.");
        }

        if (concurrencyToken.HasValue && concurrencyToken.Value != asset.EditorialConcurrencyToken)
        {
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.ConcurrencyConflict,
                "The media caption changed in another session. Reload before editing it again.");
        }

        var previous = asset.EditorialCaption;
        if (string.Equals(previous, normalized, StringComparison.Ordinal))
        {
            return MediaAlbumMutationResult.Success(Guid.Empty, "Caption is unchanged.");
        }

        asset.EditorialCaption = normalized;
        asset.EditorialCaptionUpdatedByUserId = actor.UserId;
        asset.EditorialCaptionUpdatedAtUtc = DateTimeOffset.UtcNow;
        asset.EditorialConcurrencyToken = Guid.NewGuid();
        AddAudit("MediaCaptionUpdated", null, asset.Id, actor.UserId, new { Previous = previous, Current = normalized });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return MediaAlbumMutationResult.Success(Guid.Empty, string.IsNullOrWhiteSpace(normalized) ? "Editorial caption cleared." : "Editorial caption updated.", 1);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MediaAlbumMutationResult.Failed(
                MediaAlbumMutationFailure.ConcurrencyConflict,
                "The media caption changed in another session. Reload before editing it again.");
        }
    }

    private async Task<(MediaAlbum? Album, MediaAlbumMutationResult? Result)> LoadMutableAlbumAsync(
        Guid albumId,
        MediaAlbumActor actor,
        CancellationToken cancellationToken)
    {
        if (albumId == Guid.Empty)
        {
            return (null, MediaAlbumMutationResult.Failed(MediaAlbumMutationFailure.InvalidRequest, "Album identifier is required."));
        }

        var album = await _db.Albums.SingleOrDefaultAsync(item => item.Id == albumId, cancellationToken);
        if (album is null)
        {
            return (null, MediaAlbumMutationResult.Failed(MediaAlbumMutationFailure.NotFound, "The album could not be found.", albumId));
        }

        if (!CanManage(actor, album.CreatedByUserId))
        {
            return (null, MediaAlbumMutationResult.Failed(MediaAlbumMutationFailure.Forbidden, "You can view this album but cannot modify it.", albumId));
        }

        return (album, null);
    }

    private async Task<List<MediaAsset>> LoadEligibleAssetsAsync(
        IReadOnlyCollection<long> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return new List<MediaAsset>();
        var orderedIds = ids.Where(id => id > 0).Distinct().Take(MaximumAlbumItems).ToArray();
        var assets = await _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Where(asset => orderedIds.Contains(asset.Id))
            .Select(asset => new { asset.Id, asset.Kind })
            .ToListAsync(cancellationToken);
        var byId = assets.ToDictionary(asset => asset.Id);
        return orderedIds
            .Where(byId.ContainsKey)
            .Select(id => new MediaAsset { Id = id, Kind = byId[id].Kind })
            .ToList();
    }

    private Task<bool> IsEligiblePhotoAsync(long assetId, CancellationToken cancellationToken)
        => _visibility.Apply(_db.Assets.AsNoTracking()).AnyAsync(asset => asset.Id == assetId && asset.Kind == MediaAssetKind.Photo, cancellationToken);

    private async Task<bool> ActiveNameExistsAsync(
        string name,
        Guid? excludingAlbumId,
        CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToLower();
        return await _db.Albums.AsNoTracking().AnyAsync(
            album => !album.IsArchived
                     && (!excludingAlbumId.HasValue || album.Id != excludingAlbumId.Value)
                     && album.Name.ToLower() == normalized,
            cancellationToken);
    }

    private void AddAudit(
        string action,
        Guid? albumId,
        long? assetId,
        string actorUserId,
        object? metadata)
    {
        _db.CurationAudits.Add(new MediaCurationAudit
        {
            Action = action,
            MediaAlbumId = albumId,
            MediaAssetId = assetId,
            PerformedByUserId = actorUserId,
            PerformedAtUtc = DateTimeOffset.UtcNow,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata)
        });
    }

    private static void Touch(MediaAlbum album, string actorUserId, DateTimeOffset now)
    {
        album.UpdatedByUserId = actorUserId;
        album.UpdatedAtUtc = now;
        album.ConcurrencyToken = Guid.NewGuid();
    }

    private static MediaAlbumMutationResult? ValidateMetadata(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return MediaAlbumMutationResult.Failed(MediaAlbumMutationFailure.InvalidRequest, "Album name is required.");
        }
        if (name.Trim().Length > MaximumAlbumNameLength)
        {
            return MediaAlbumMutationResult.Failed(MediaAlbumMutationFailure.InvalidRequest, $"Album name cannot exceed {MaximumAlbumNameLength} characters.");
        }
        if (NormalizeOptional(description)?.Length > MaximumDescriptionLength)
        {
            return MediaAlbumMutationResult.Failed(MediaAlbumMutationFailure.InvalidRequest, $"Album description cannot exceed {MaximumDescriptionLength} characters.");
        }
        return null;
    }

    private static IReadOnlyCollection<long> NormalizeAssetIds(IReadOnlyCollection<long> assetIds)
        => (assetIds ?? Array.Empty<long>())
            .Where(id => id > 0)
            .Distinct()
            .Take(MaximumAlbumItems + 1)
            .ToArray();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool CanManage(MediaAlbumActor actor, string createdByUserId)
        => actor.CanManageAnyAlbum || string.Equals(actor.UserId, createdByUserId, StringComparison.Ordinal);

    private static void EnsureActor(MediaAlbumActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.IsValid) throw new InvalidOperationException("An authenticated user is required for media curation.");
    }

    private static long? ResolveCover(long? explicitCover, long? fallbackCover)
        => explicitCover ?? fallbackCover;

    private static MediaAlbumMutationResult CapacityFailure(Guid? albumId = null)
        => MediaAlbumMutationResult.Failed(
            MediaAlbumMutationFailure.CapacityExceeded,
            $"An album can contain up to {MaximumAlbumItems} media items.",
            albumId);

    private static MediaAlbumMutationResult ConcurrencyFailure(Guid albumId)
        => MediaAlbumMutationResult.Failed(
            MediaAlbumMutationFailure.ConcurrencyConflict,
            "The album changed in another session. Reload the album and try again.",
            albumId);

    private static bool LooksLikeDuplicateName(DbUpdateException exception)
        => exception.InnerException?.Message.Contains("UX_MediaAlbums_ActiveName_CI", StringComparison.OrdinalIgnoreCase) == true
           || exception.Message.Contains("UX_MediaAlbums_ActiveName_CI", StringComparison.OrdinalIgnoreCase);

    private sealed record AlbumRow(
        Guid Id,
        string Name,
        string? Description,
        string CreatedByUserId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        bool IsArchived,
        long? CoverMediaAssetId,
        int ItemCount,
        int PhotoCount,
        int VideoCount,
        long? FallbackCoverMediaAssetId);

}
