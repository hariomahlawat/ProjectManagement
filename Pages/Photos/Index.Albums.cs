using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos;

public sealed partial class IndexModel
{
    private async Task LoadAddMediaTargetAsync(CancellationToken cancellationToken)
    {
        if (!AddToAlbumId.HasValue) return;

        try
        {
            var target = await _albums.GetDetailsAsync(
                AddToAlbumId.Value,
                CurrentAlbumActor(),
                cancellationToken);

            if (target is null || !target.CanManage)
            {
                AddMediaTargetWarning = "The selected album is unavailable or you no longer have permission to manage it.";
                AddToAlbumId = null;
                return;
            }

            if (target.IsArchived)
            {
                AddMediaTargetWarning = "Archived albums cannot accept new media. Restore the album before adding items.";
                AddToAlbumId = null;
                return;
            }

            if (target.TotalMembershipCount >= MediaAlbumService.MaximumAlbumItems)
            {
                AddMediaTargetWarning = $"{target.Name} has reached the {MediaAlbumService.MaximumAlbumItems}-item album limit.";
                AddToAlbumId = null;
                return;
            }

            AddMediaTargetAlbum = target;
            AddMediaExistingAssetIds = target.OrderedVisibleAssetIds.ToHashSet();
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            _logger.LogWarning(exception, "Unable to prepare target album {AlbumId} for media selection.", AddToAlbumId);
            AddMediaTargetWarning = "The target album could not be prepared. Continue browsing Photos and try again later.";
            AddToAlbumId = null;
        }
    }

    private async Task LoadAlbumsWorkspaceAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsUsingCatalogue = true;
            var result = await _albums.SearchAsync(
                new MediaAlbumListQuery(
                    Q,
                    Sort,
                    PageNumber,
                    AlbumPageSize,
                    IncludeArchivedAlbums,
                    CurrentAlbumActor()),
                cancellationToken);

            var latestAlbumUpdate = result.Albums.Count == 0
                ? (DateTimeOffset?)null
                : result.Albums.Max(album => album.UpdatedAtUtc);
            LibraryRevision = string.Concat(
                LibraryRevision,
                ":albums:",
                result.TotalAlbums.ToString(CultureInfo.InvariantCulture),
                ":",
                latestAlbumUpdate?.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) ?? "0");

            PageNumber = result.PageNumber;
            HasPreviousPage = result.HasPreviousPage;
            HasNextPage = result.HasNextPage;
            var creatorNames = await ResolveAlbumCreatorNamesAsync(
                result.Albums.Select(album => album.CreatedByUserId),
                cancellationToken);
            Albums = result.Albums.Select(album => MapAlbum(album, creatorNames)).ToList();
            Collections = Array.Empty<CollectionCard>();
            Items = Array.Empty<MediaItem>();
            Groups = Array.Empty<MediaGroup>();
            Stats = new LibraryStats
            {
                Total = result.TotalVisibleItems,
                Photos = result.TotalPhotos,
                Videos = result.TotalVideos,
                Collections = result.TotalAlbums
            };
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            _logger.LogWarning(exception, "Unable to load organisation-wide media albums.");
            ExternalLibraryAvailable = false;
            ExternalLibraryWarning = "Organisation-wide albums are temporarily unavailable while the media catalogue is being prepared.";
            Albums = Array.Empty<AlbumCard>();
            Stats = new LibraryStats();
        }
    }

    private async Task LoadAlbumDetailAsync(CancellationToken cancellationToken)
    {
        if (!AlbumId.HasValue)
        {
            return;
        }

        try
        {
            CurrentAlbum = await _albums.GetDetailsAsync(
                AlbumId.Value,
                CurrentAlbumActor(),
                cancellationToken);
            if (CurrentAlbum is null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                ExternalLibraryAvailable = false;
                ExternalLibraryWarning = "This album could not be found.";
                Items = Array.Empty<MediaItem>();
                Groups = Array.Empty<MediaGroup>();
                return;
            }

            var creatorNames = await ResolveAlbumCreatorNamesAsync(
                new[] { CurrentAlbum.CreatedByUserId },
                cancellationToken);
            CurrentAlbumCreatorDisplayName = creatorNames.TryGetValue(CurrentAlbum.CreatedByUserId, out var creatorName)
                ? creatorName
                : "PRISM user";

            if (!CanOrganizeCurrentAlbum)
            {
                // Organise is meaningful only for two or more visible media items and is
                // never available on archived/read-only albums. Direct URLs are normalised too.
                OrganizeAlbum = false;
            }

            LibraryRevision = string.Concat(
                LibraryRevision,
                ":album:",
                CurrentAlbum.Id.ToString("N"),
                ":",
                CurrentAlbum.UpdatedAtUtc.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture));

            Stats = new LibraryStats
            {
                Total = CurrentAlbum.ItemCount,
                Photos = CurrentAlbum.PhotoCount,
                Videos = CurrentAlbum.VideoCount,
                Collections = 1
            };
            HasPreviousPage = false;
            HasNextPage = false;
            PageNumber = 1;
            Collections = Array.Empty<CollectionCard>();
            Albums = Array.Empty<AlbumCard>();

            if (CurrentAlbum.OrderedVisibleAssetIds.Count == 0)
            {
                Items = Array.Empty<MediaItem>();
                Groups = Array.Empty<MediaGroup>();
                await LoadManageableAlbumsSafeAsync(cancellationToken);
                return;
            }

            var result = await _library.SearchAsync(
                new MediaLibraryQuery(
                    Query: null,
                    Source: "all",
                    Kind: "all",
                    Classification: "all",
                    ProjectId: null,
                    PersonId: null,
                    Year: null,
                    PageNumber: 1,
                    PageSize: MediaAlbumService.MaximumAlbumItems,
                    IncludePeople: PeopleFeatureEnabled,
                    PersonIds: Array.Empty<Guid>(),
                    PeopleMatch: "all",
                    IncludeUnidentifiedFaces: CanManagePeople,
                    Sort: "newest",
                    CollectionKey: null,
                    AssetIds: CurrentAlbum.OrderedVisibleAssetIds),
                cancellationToken);

            if (!result.IsAvailable)
            {
                ExternalLibraryAvailable = false;
                ExternalLibraryWarning = result.Warning ?? "The album media could not be loaded from the catalogue.";
                Items = Array.Empty<MediaItem>();
                Groups = Array.Empty<MediaGroup>();
                return;
            }

            IsUsingCatalogue = true;
            ExternalLibraryAvailable = true;
            ExternalLibraryWarning = result.Warning;
            var byAssetId = result.Items.ToDictionary(item => item.Id);
            var mapped = new List<MediaItem>(CurrentAlbum.OrderedVisibleAssetIds.Count);
            foreach (var assetId in CurrentAlbum.OrderedVisibleAssetIds)
            {
                if (!byAssetId.TryGetValue(assetId, out var row)) continue;
                var item = MapCatalogueItem(row);
                mapped.Add(CloneWithAlbumCover(item, assetId == CurrentAlbum.CoverMediaAssetId));
            }

            Items = mapped;
            Groups = mapped.Count == 0
                ? Array.Empty<MediaGroup>()
                : new[]
                {
                    new MediaGroup(
                        $"album:{CurrentAlbum.Id:N}",
                        CurrentAlbum.Name,
                        CurrentAlbum.Description ?? string.Empty,
                        CurrentAlbum.UpdatedAtUtc.ToLocalTime().DateTime,
                        mapped)
                };

            await LoadManageableAlbumsSafeAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            _logger.LogWarning(exception, "Unable to load media album {AlbumId}.", AlbumId);
            ExternalLibraryAvailable = false;
            ExternalLibraryWarning = "This album is temporarily unavailable.";
            Items = Array.Empty<MediaItem>();
            Groups = Array.Empty<MediaGroup>();
        }
    }

    private async Task LoadManageableAlbumsSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            ManageableAlbums = await _albums.GetManageableOptionsAsync(CurrentAlbumActor(), cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            _logger.LogWarning(exception, "Unable to load manageable media album options.");
            ManageableAlbums = Array.Empty<MediaAlbumOption>();
        }
    }

    private AlbumCard MapAlbum(
        MediaAlbumSummary row,
        IReadOnlyDictionary<string, string> creatorNames)
    {
        var coverUrl = row.CoverMediaAssetId.HasValue
            ? Url.Page("/Photos/Media", new { id = row.CoverMediaAssetId.Value, variant = "thumb" })
            : null;
        var actor = CurrentAlbumActor();
        return new AlbumCard(
            row.Id,
            row.Name,
            row.Description,
            row.ItemCount,
            row.PhotoCount,
            row.VideoCount,
            row.CreatedAtUtc.ToLocalTime().DateTime,
            row.UpdatedAtUtc.ToLocalTime().DateTime,
            row.CoverMediaAssetId,
            coverUrl,
            row.IsArchived,
            row.CanManage,
            string.Equals(row.CreatedByUserId, actor.UserId, StringComparison.Ordinal),
            creatorNames.TryGetValue(row.CreatedByUserId, out var creatorName) ? creatorName : "PRISM user");
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveAlbumCreatorNamesAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0) return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var rows = await _db.Users
                .AsNoTracking()
                .Where(user => ids.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    user.Rank,
                    user.FullName,
                    user.UserName
                })
                .ToListAsync(cancellationToken);

            return rows.ToDictionary(
                row => row.Id,
                row => PhotosCurationPresentation.BuildCreatorDisplayName(row.Rank, row.FullName, row.UserName),
                StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            // Album access must not fail merely because optional Identity profile data is
            // temporarily unavailable. The album itself remains organisation-visible.
            _logger.LogWarning(exception, "Unable to resolve media album creator display names.");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static MediaItem CloneWithAlbumCover(MediaItem item, bool isAlbumCover)
        => new()
        {
            Id = item.Id,
            AssetId = item.AssetId,
            Kind = item.Kind,
            Source = item.Source,
            SourceLabel = item.SourceLabel,
            Classification = item.Classification,
            People = item.People,
            UnidentifiedFaceCount = item.UnidentifiedFaceCount,
            ContextKey = item.ContextKey,
            CollectionKey = item.CollectionKey,
            ContextTitle = item.ContextTitle,
            ContextSubtitle = item.ContextSubtitle,
            OriginalTitle = item.OriginalTitle,
            Title = item.Title,
            DisplayContext = item.DisplayContext,
            DisplaySubtitle = item.DisplaySubtitle,
            Caption = item.Caption,
            EditorialCaption = item.EditorialCaption,
            EditorialConcurrencyToken = item.EditorialConcurrencyToken,
            OriginalFileName = item.OriginalFileName,
            FileSizeBytes = item.FileSizeBytes,
            Albums = item.Albums,
            MediaDate = item.MediaDate,
            ThumbnailUrl = item.ThumbnailUrl,
            DisplayUrl = item.DisplayUrl,
            OriginalUrl = item.OriginalUrl,
            DownloadUrl = item.DownloadUrl,
            SourceUrl = item.SourceUrl,
            Width = item.Width,
            Height = item.Height,
            DurationSeconds = item.DurationSeconds,
            IsCover = item.IsCover,
            IsAlbumCover = isAlbumCover,
            SortOrder = item.SortOrder,
            VersionToken = item.VersionToken
        };

    private MediaAlbumActor CurrentAlbumActor()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("Authenticated Photos user identifier is unavailable.");
        }
        return new MediaAlbumActor(userId, CanManageAnyAlbum);
    }

}
