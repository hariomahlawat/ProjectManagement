using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Collection-level read model for Photos. Collection pagination is deliberately independent
/// of timeline pagination: a collection represents the complete CollectionKey across the
/// filtered catalogue, never only the media currently loaded on a Photos page.
/// </summary>
public sealed class MediaCollectionQueryService : IMediaCollectionQueryService
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaAssetVisibilityPolicy _visibility;

    public MediaCollectionQueryService(
        MediaLibraryDbContext db,
        IMediaAssetVisibilityPolicy visibility)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
    }

    public async Task<MediaCollectionQueryResult> SearchAsync(
        MediaCollectionQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = _visibility.Apply(_db.Assets.AsNoTracking());
        query = ApplySource(query, request.Source);
        query = ApplyKind(query, request.Kind);
        query = ApplyClassification(query, request.Classification);

        if (request.ProjectId.HasValue)
        {
            query = query.Where(asset => asset.ProjectId == request.ProjectId.Value);
        }

        if (request.Year.HasValue)
        {
            query = query.Where(asset => asset.MediaDateUtc.Year == request.Year.Value);
        }

        var selectedPeople = NormalizePeople(request.PersonIds);
        if (request.IncludePeople && selectedPeople.Count > 0)
        {
            query = MediaLibraryQueryService.ApplyPeopleFilter(query, selectedPeople, request.PeopleMatch);
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var pattern = $"%{EscapeLikePattern(request.Query.Trim())}%";
            query = request.IncludePeople
                ? query.Where(asset =>
                    EF.Functions.ILike(asset.Title, pattern, "\\")
                    || (asset.Caption != null && EF.Functions.ILike(asset.Caption, pattern, "\\"))
                    || (asset.EditorialCaption != null && EF.Functions.ILike(asset.EditorialCaption, pattern, "\\"))
                    || asset.AlbumItems.Any(item => !item.MediaAlbum.IsArchived && EF.Functions.ILike(item.MediaAlbum.Name, pattern, "\\"))
                    || EF.Functions.ILike(asset.ContextTitle, pattern, "\\")
                    || EF.Functions.ILike(asset.ContextSubtitle, pattern, "\\")
                    || EF.Functions.ILike(asset.OriginalFileName, pattern, "\\")
                    || EF.Functions.ILike(asset.SourceLabel, pattern, "\\")
                    || EF.Functions.ILike(asset.Source.Name, pattern, "\\")
                    || (asset.RelativePath != null && EF.Functions.ILike(asset.RelativePath, pattern, "\\"))
                    || asset.Faces.Any(face => face.PersonAssignments.Any(assignment =>
                        assignment.RemovedAtUtc == null
                        && assignment.MediaPerson.Status == MediaPersonStatus.Confirmed
                        && !assignment.MediaPerson.IsHidden
                        && EF.Functions.ILike(assignment.MediaPerson.DisplayName, pattern, "\\"))))
                : query.Where(asset =>
                    EF.Functions.ILike(asset.Title, pattern, "\\")
                    || (asset.Caption != null && EF.Functions.ILike(asset.Caption, pattern, "\\"))
                    || (asset.EditorialCaption != null && EF.Functions.ILike(asset.EditorialCaption, pattern, "\\"))
                    || asset.AlbumItems.Any(item => !item.MediaAlbum.IsArchived && EF.Functions.ILike(item.MediaAlbum.Name, pattern, "\\"))
                    || EF.Functions.ILike(asset.ContextTitle, pattern, "\\")
                    || EF.Functions.ILike(asset.ContextSubtitle, pattern, "\\")
                    || EF.Functions.ILike(asset.OriginalFileName, pattern, "\\")
                    || EF.Functions.ILike(asset.SourceLabel, pattern, "\\")
                    || EF.Functions.ILike(asset.Source.Name, pattern, "\\")
                    || (asset.RelativePath != null && EF.Functions.ILike(asset.RelativePath, pattern, "\\")));
        }

        var grouped = query
            .GroupBy(asset => asset.CollectionKey)
            .Select(group => new CollectionAggregateRow
            {
                CollectionKey = group.Key,
                ItemCount = group.Count(),
                PhotoCount = group.Count(asset => asset.Kind == MediaAssetKind.Photo),
                VideoCount = group.Count(asset => asset.Kind == MediaAssetKind.Video),
                FirstMediaDateUtc = group.Min(asset => asset.MediaDateUtc),
                LatestMediaDateUtc = group.Max(asset => asset.MediaDateUtc),
                HasNonProjectMedia = group.Any(asset =>
                    asset.Origin != MediaAssetOrigin.ProjectPhoto
                    && asset.Origin != MediaAssetOrigin.ProjectVideo)
            });

        if (!request.IncludeSingletons)
        {
            grouped = grouped.Where(group => group.ItemCount > 1 || group.HasNonProjectMedia);
        }

        var totals = await grouped
            .GroupBy(_ => 1)
            .Select(all => new
            {
                Collections = all.Count(),
                Items = all.Sum(row => row.ItemCount),
                Photos = all.Sum(row => row.PhotoCount),
                Videos = all.Sum(row => row.VideoCount)
            })
            .FirstOrDefaultAsync(cancellationToken);
        var totalCollections = totals?.Collections ?? 0;
        var totalItems = totals?.Items ?? 0;
        var totalPhotos = totals?.Photos ?? 0;
        var totalVideos = totals?.Videos ?? 0;
        var pageSize = Math.Clamp(request.PageSize, 12, 96);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCollections / (double)pageSize));
        var pageNumber = Math.Clamp(request.PageNumber, 1, pageCount);

        grouped = string.Equals(request.Sort?.Trim(), "oldest", StringComparison.OrdinalIgnoreCase)
            ? grouped.OrderBy(group => group.LatestMediaDateUtc)
                .ThenBy(group => group.CollectionKey)
            : grouped.OrderByDescending(group => group.LatestMediaDateUtc)
                .ThenBy(group => group.CollectionKey);

        var aggregateRows = await grouped
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        if (aggregateRows.Count == 0)
        {
            return new MediaCollectionQueryResult(
                Array.Empty<MediaCollectionSummary>(),
                totalCollections,
                totalItems,
                totalPhotos,
                totalVideos,
                pageNumber,
                pageSize,
                pageNumber > 1,
                pageNumber < pageCount);
        }

        var keys = aggregateRows.Select(row => row.CollectionKey).ToArray();
        var metadataRows = await query
            .Where(asset => keys.Contains(asset.CollectionKey))
            .Select(asset => new CollectionMediaRow
            {
                CollectionKey = asset.CollectionKey,
                Id = asset.Id,
                Origin = asset.Origin,
                Kind = asset.Kind,
                ContextKey = asset.ContextKey,
                ContextTitle = asset.ContextTitle,
                ContextSubtitle = asset.ContextSubtitle,
                SourceLabel = asset.SourceLabel,
                MediaDateUtc = asset.MediaDateUtc,
                Width = asset.Width,
                Height = asset.Height,
                IsCover = asset.IsCover,
                SortOrder = asset.SortOrder
            })
            .ToListAsync(cancellationToken);

        var rowsByCollection = metadataRows
            .GroupBy(row => row.CollectionKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var collections = new List<MediaCollectionSummary>(aggregateRows.Count);
        foreach (var aggregate in aggregateRows)
        {
            if (!rowsByCollection.TryGetValue(aggregate.CollectionKey, out var rows) || rows.Count == 0)
            {
                continue;
            }

            var metadata = rows
                .OrderByDescending(row => row.MediaDateUtc)
                .ThenBy(row => row.SortOrder)
                .ThenBy(row => row.Id)
                .First();
            var cover = rows
                .Where(row => row.Kind == MediaAssetKind.Photo)
                .OrderByDescending(row => row.IsCover)
                .ThenByDescending(row => row.MediaDateUtc)
                .ThenBy(row => row.SortOrder)
                .ThenBy(row => row.Id)
                .FirstOrDefault();

            collections.Add(new MediaCollectionSummary(
                aggregate.CollectionKey,
                metadata.Origin,
                metadata.ContextKey,
                MediaCollectionTitleFormatter.FormatCollectionTitle(metadata.Origin, metadata.ContextTitle),
                metadata.ContextSubtitle,
                metadata.SourceLabel,
                aggregate.ItemCount,
                aggregate.PhotoCount,
                aggregate.VideoCount,
                aggregate.FirstMediaDateUtc,
                aggregate.LatestMediaDateUtc,
                cover?.Id,
                cover?.Width,
                cover?.Height));
        }

        return new MediaCollectionQueryResult(
            collections,
            totalCollections,
            totalItems,
            totalPhotos,
            totalVideos,
            pageNumber,
            pageSize,
            pageNumber > 1,
            pageNumber < pageCount);
    }

    private static IReadOnlyList<Guid> NormalizePeople(IReadOnlyList<Guid>? personIds)
        => (personIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(10)
            .ToArray();

    private static IQueryable<MediaAsset> ApplySource(IQueryable<MediaAsset> query, string source)
        => source switch
        {
            "projects" => query.Where(asset => asset.Origin == MediaAssetOrigin.ProjectPhoto
                                               || asset.Origin == MediaAssetOrigin.ProjectVideo),
            "visits" => query.Where(asset => asset.Origin == MediaAssetOrigin.VisitPhoto),
            "events" => query.Where(asset => asset.Origin == MediaAssetOrigin.SocialMediaEventPhoto),
            "activities" => query.Where(asset => asset.Origin == MediaAssetOrigin.ActivityPhoto),
            "external" => query.Where(asset => asset.Origin == MediaAssetOrigin.ExternalFile),
            _ => query
        };

    private static IQueryable<MediaAsset> ApplyKind(IQueryable<MediaAsset> query, string kind)
        => kind switch
        {
            "photo" => query.Where(asset => asset.Kind == MediaAssetKind.Photo),
            "video" => query.Where(asset => asset.Kind == MediaAssetKind.Video),
            _ => query
        };

    private static IQueryable<MediaAsset> ApplyClassification(
        IQueryable<MediaAsset> query,
        string classification)
        => classification switch
        {
            "photograph" => query.Where(asset => asset.Classification == MediaClassification.Photograph),
            "screenshot" => query.Where(asset => asset.Classification == MediaClassification.Screenshot),
            "scanned-document" => query.Where(asset => asset.Classification == MediaClassification.ScannedDocument),
            "diagram" => query.Where(asset => asset.Classification == MediaClassification.Diagram),
            "presentation-slide" => query.Where(asset => asset.Classification == MediaClassification.PresentationSlide),
            "graphic" => query.Where(asset => asset.Classification == MediaClassification.Graphic),
            "unknown" => query.Where(asset => asset.Classification == MediaClassification.Unknown),
            _ => query
        };

    private static string EscapeLikePattern(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed class CollectionAggregateRow
    {
        public string CollectionKey { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public int PhotoCount { get; set; }
        public int VideoCount { get; set; }
        public DateTimeOffset FirstMediaDateUtc { get; set; }
        public DateTimeOffset LatestMediaDateUtc { get; set; }
        public bool HasNonProjectMedia { get; set; }
    }

    private sealed class CollectionMediaRow
    {
        public string CollectionKey { get; set; } = string.Empty;
        public long Id { get; set; }
        public MediaAssetOrigin Origin { get; set; }
        public MediaAssetKind Kind { get; set; }
        public string ContextKey { get; set; } = string.Empty;
        public string ContextTitle { get; set; } = string.Empty;
        public string ContextSubtitle { get; set; } = string.Empty;
        public string SourceLabel { get; set; } = string.Empty;
        public DateTimeOffset MediaDateUtc { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public bool IsCover { get; set; }
        public long SortOrder { get; set; }
    }
}
