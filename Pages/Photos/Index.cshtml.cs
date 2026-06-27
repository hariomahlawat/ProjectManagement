using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos;

[Authorize]
public sealed class IndexModel : PageModel
{
    private const int PageSize = 120;
    private readonly ApplicationDbContext _db;
    private readonly IExternalMediaLibraryReader _externalMedia;
    private readonly MediaLibraryOptions _mediaOptions;

    public IndexModel(
        ApplicationDbContext db,
        IExternalMediaLibraryReader externalMedia,
        IOptions<MediaLibraryOptions> mediaOptions)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _externalMedia = externalMedia ?? throw new ArgumentNullException(nameof(externalMedia));
        _mediaOptions = mediaOptions?.Value ?? throw new ArgumentNullException(nameof(mediaOptions));
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string Source { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string Classification { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public int? ProjectId { get; set; }
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public IReadOnlyList<MediaItem> Items { get; private set; } = Array.Empty<MediaItem>();
    public IReadOnlyList<MediaGroup> Groups { get; private set; } = Array.Empty<MediaGroup>();
    public IReadOnlyList<ProjectOption> Projects { get; private set; } = Array.Empty<ProjectOption>();
    public IReadOnlyList<int> Years { get; private set; } = Array.Empty<int>();
    public LibraryStats Stats { get; private set; } = new();
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage { get; private set; }
    public int CurrentPage => Math.Max(1, PageNumber);
    public bool ExternalSourcesEnabled => _mediaOptions.IsExternalSourceFeatureEnabled;
    public bool ExternalLibraryAvailable { get; private set; } = true;
    public string? ExternalLibraryWarning { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        PageNumber = Math.Max(1, PageNumber);
        Source = NormalizeSource(Source);
        if (!ExternalSourcesEnabled && Source == "external")
        {
            Source = "all";
        }

        Kind = NormalizeKind(Kind);
        Classification = NormalizeClassification(Classification);
        Q = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();

        Projects = await _db.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted)
            .Where(project => _db.ProjectPhotos.Any(photo => photo.ProjectId == project.Id)
                           || _db.ProjectVideos.Any(video => video.ProjectId == project.Id))
            .OrderBy(project => project.Name)
            .Select(project => new ProjectOption(project.Id, project.Name))
            .ToListAsync(cancellationToken);

        var internalItems = new List<MediaItem>(512);
        if (Source is "all" or "projects")
        {
            if (Kind is "all" or "photo") internalItems.AddRange(await LoadProjectPhotosAsync(cancellationToken));
            if (Kind is "all" or "video") internalItems.AddRange(await LoadProjectVideosAsync(cancellationToken));
        }

        if (Kind is "all" or "photo")
        {
            if (Source is "all" or "visits") internalItems.AddRange(await LoadVisitPhotosAsync(cancellationToken));
            if (Source is "all" or "events") internalItems.AddRange(await LoadSocialMediaPhotosAsync(cancellationToken));
        }

        var filteredInternal = internalItems
            .Where(MatchesSearch)
            .Where(MatchesClassification)
            .Where(item => !Year.HasValue || item.MediaDate.Year == Year.Value)
            .ToList();

        var requestExternal = ExternalSourcesEnabled
                              && (Source is "all" or "external")
                              && !ProjectId.HasValue;

        var requestedGlobalSkip = GetPageSkip(PageNumber);
        var externalWindowSkip = Math.Max(0, requestedGlobalSkip - filteredInternal.Count);
        var externalWindowTake = GetExternalWindowSize(filteredInternal.Count);
        var externalResult = requestExternal
            ? await SearchExternalAsync(externalWindowSkip, externalWindowTake, cancellationToken)
            : ExternalMediaSearchResult.Empty();

        var totalCount = filteredInternal.Count + externalResult.Total;
        var maxPage = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        PageNumber = Math.Min(PageNumber, maxPage);
        var globalSkip = GetPageSkip(PageNumber);
        var requiredExternalSkip = Math.Max(0, globalSkip - filteredInternal.Count);

        // An out-of-range page can be clamped after the first count query. Re-read only
        // the correct external window instead of returning a blank last page.
        if (requestExternal && requiredExternalSkip != externalWindowSkip)
        {
            externalWindowSkip = requiredExternalSkip;
            externalResult = await SearchExternalAsync(
                externalWindowSkip,
                externalWindowTake,
                cancellationToken);
        }

        ExternalLibraryAvailable = externalResult.IsAvailable;
        ExternalLibraryWarning = externalResult.Warning;
        var externalItems = MapExternalItems(externalResult.Items);

        var merged = filteredInternal
            .Concat(externalItems)
            .OrderByDescending(item => item.MediaDate)
            .ThenBy(item => item.ContextTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        // Rows before externalWindowSkip are deliberately absent from the local merge.
        // Subtract that known prefix to obtain the page offset within the bounded window.
        var localSkip = Math.Max(0, globalSkip - externalWindowSkip);
        HasNextPage = totalCount > globalSkip + PageSize;
        Items = merged.Skip(localSkip).Take(PageSize).ToList();

        var internalYears = internalItems
            .Where(MatchesSearch)
            .Where(MatchesClassification)
            .Select(item => item.MediaDate.Year);
        Years = internalYears
            .Concat(externalResult.Years)
            .Distinct()
            .OrderByDescending(year => year)
            .ToList();

        Stats = new LibraryStats
        {
            Total = totalCount,
            Photos = filteredInternal.Count(item => item.Kind == MediaKind.Photo) + externalResult.Photos,
            Videos = filteredInternal.Count(item => item.Kind == MediaKind.Video) + externalResult.Videos,
            Collections = filteredInternal.Select(item => item.CollectionKey).Distinct(StringComparer.Ordinal).Count()
                          + externalResult.Collections
        };

        Groups = Items
            .GroupBy(item => new
            {
                item.ContextKey,
                item.ContextTitle,
                item.ContextSubtitle,
                Date = item.MediaDate.Date
            })
            .Select(group => new MediaGroup(
                group.Key.ContextKey,
                group.Key.ContextTitle,
                group.Key.ContextSubtitle,
                group.Key.Date,
                group.ToList()))
            .OrderByDescending(group => group.Date)
            .ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }


    private Task<ExternalMediaSearchResult> SearchExternalAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
        => _externalMedia.SearchAsync(
            new ExternalMediaSearchRequest(
                Q,
                Kind,
                Classification,
                Year,
                skip,
                take),
            cancellationToken);

    private static int GetPageSkip(int pageNumber)
    {
        var skip = ((long)Math.Max(1, pageNumber) - 1L) * PageSize;
        return skip >= int.MaxValue ? int.MaxValue - PageSize : (int)skip;
    }

    private static int GetExternalWindowSize(int internalItemCount)
    {
        var size = (long)PageSize + Math.Max(0, internalItemCount) + 1L;
        return size >= int.MaxValue ? int.MaxValue : (int)size;
    }

    private List<MediaItem> MapExternalItems(IReadOnlyList<ExternalMediaSearchItem> rows)
        => rows.Select(row =>
        {
            var preview = Url.Page("/Photos/Media", new
            {
                id = row.Id,
                variant = row.Kind == MediaAssetKind.Photo ? "preview" : "original",
                v = row.CacheVersion
            }) ?? string.Empty;

            return new MediaItem
            {
                Id = $"external:{row.Id}",
                Kind = row.Kind == MediaAssetKind.Video ? MediaKind.Video : MediaKind.Photo,
                Source = MediaSource.ExternalFolder,
                SourceLabel = row.SourceLabel,
                ContextKey = row.ContextKey,
                CollectionKey = row.CollectionKey,
                ContextTitle = row.ContextTitle,
                ContextSubtitle = row.ContextSubtitle,
                Title = row.Title,
                Caption = row.Caption,
                OriginalFileName = row.OriginalFileName,
                MediaDate = row.MediaDateUtc.ToLocalTime().DateTime,
                ThumbnailUrl = row.Kind == MediaAssetKind.Photo
                    ? Url.Page("/Photos/Media", new { id = row.Id, variant = "thumb", v = row.CacheVersion }) ?? preview
                    : Url.Content("~/img/placeholders/project-video-placeholder.svg"),
                DisplayUrl = preview,
                OriginalUrl = Url.Page("/Photos/Media", new { id = row.Id, variant = "original", v = row.CacheVersion }) ?? preview,
                DownloadUrl = Url.Page("/Photos/Media", new { id = row.Id, variant = "original", download = true, v = row.CacheVersion }),
                SourceUrl = Url.Page("/Photos/Index", new { Source = "external", Q = row.ParentEntityId }),
                Width = row.Width,
                Height = row.Height,
                DurationSeconds = row.DurationSeconds,
                SortOrder = row.SortOrder
            };
        }).ToList();

    private async Task<List<MediaItem>> LoadProjectPhotosAsync(CancellationToken cancellationToken)
    {
        var query = _db.ProjectPhotos.AsNoTracking().Where(photo => !photo.Project.IsDeleted);
        if (ProjectId.HasValue) query = query.Where(photo => photo.ProjectId == ProjectId.Value);

        var rows = await query.Select(photo => new
        {
            photo.Id, photo.ProjectId, ProjectName = photo.Project.Name, photo.Caption,
            photo.OriginalFileName, photo.Width, photo.Height, photo.Ordinal, photo.IsCover,
            photo.Version, photo.CreatedUtc
        }).ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var displayUrl = Url.Page("/Projects/Photos/View", new { id = row.ProjectId, photoId = row.Id, size = "xl", v = row.Version }) ?? string.Empty;
            return new MediaItem
            {
                Id = $"project-photo:{row.Id}", Kind = MediaKind.Photo, Source = MediaSource.Project,
                SourceLabel = "Project", ContextKey = $"project:{row.ProjectId}", CollectionKey = $"project:{row.ProjectId}",
                ContextTitle = row.ProjectName, ContextSubtitle = "Project media",
                Title = string.IsNullOrWhiteSpace(row.Caption) ? row.OriginalFileName : row.Caption,
                Caption = row.Caption, OriginalFileName = row.OriginalFileName,
                MediaDate = DateTime.SpecifyKind(row.CreatedUtc, DateTimeKind.Utc),
                ThumbnailUrl = Url.Page("/Projects/Photos/View", new { id = row.ProjectId, photoId = row.Id, size = "md", v = row.Version }) ?? displayUrl,
                DisplayUrl = displayUrl,
                OriginalUrl = Url.Page("/Projects/Photos/View", new { id = row.ProjectId, photoId = row.Id, size = "original", v = row.Version }) ?? displayUrl,
                DownloadUrl = Url.Page("/Projects/Photos/Download", new { id = row.ProjectId, photoId = row.Id, size = "original" }),
                SourceUrl = Url.Page("/Projects/Photos/Index", new { id = row.ProjectId }),
                Width = row.Width, Height = row.Height, IsCover = row.IsCover, SortOrder = row.Ordinal
            };
        }).ToList();
    }

    private async Task<List<MediaItem>> LoadProjectVideosAsync(CancellationToken cancellationToken)
    {
        var query = _db.ProjectVideos.AsNoTracking().Where(video => !video.Project.IsDeleted);
        if (ProjectId.HasValue) query = query.Where(video => video.ProjectId == ProjectId.Value);

        var rows = await query.Select(video => new
        {
            video.Id, video.ProjectId, ProjectName = video.Project.Name, video.Title, video.Description,
            video.OriginalFileName, video.DurationSeconds, video.Ordinal, video.IsFeatured, video.Version, video.CreatedUtc
        }).ToListAsync(cancellationToken);

        return rows.Select(row => new MediaItem
        {
            Id = $"project-video:{row.Id}", Kind = MediaKind.Video, Source = MediaSource.Project,
            SourceLabel = "Project video", ContextKey = $"project:{row.ProjectId}", CollectionKey = $"project:{row.ProjectId}",
            ContextTitle = row.ProjectName, ContextSubtitle = "Project media",
            Title = string.IsNullOrWhiteSpace(row.Title) ? row.OriginalFileName : row.Title,
            Caption = row.Description, OriginalFileName = row.OriginalFileName,
            MediaDate = DateTime.SpecifyKind(row.CreatedUtc, DateTimeKind.Utc),
            ThumbnailUrl = Url.Page("/Projects/Videos/Poster", new { id = row.ProjectId, videoId = row.Id, v = row.Version }) ?? string.Empty,
            DisplayUrl = Url.Page("/Projects/Videos/Stream", new { id = row.ProjectId, videoId = row.Id, v = row.Version }) ?? string.Empty,
            OriginalUrl = Url.Page("/Projects/Videos/Stream", new { id = row.ProjectId, videoId = row.Id, v = row.Version }) ?? string.Empty,
            SourceUrl = Url.Page("/Projects/Videos/Index", new { id = row.ProjectId }),
            DurationSeconds = row.DurationSeconds, IsCover = row.IsFeatured, SortOrder = row.Ordinal
        }).ToList();
    }

    private async Task<List<MediaItem>> LoadVisitPhotosAsync(CancellationToken cancellationToken)
    {
        if (ProjectId.HasValue) return new List<MediaItem>();
        var rows = await _db.VisitPhotos.AsNoTracking().Select(photo => new
        {
            photo.Id, photo.VisitId, VisitorName = photo.Visit!.VisitorName,
            VisitType = photo.Visit.VisitType != null ? photo.Visit.VisitType.Name : null,
            photo.Visit.DateOfVisit, photo.Caption, photo.Width, photo.Height, photo.VersionStamp, photo.CreatedAtUtc
        }).ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var date = row.DateOfVisit.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
            var displayUrl = Url.Page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = row.VisitId, photoId = row.Id, size = "xl", v = row.VersionStamp }) ?? string.Empty;
            return new MediaItem
            {
                Id = $"visit-photo:{row.Id}", Kind = MediaKind.Photo, Source = MediaSource.Visit, SourceLabel = "Visit",
                ContextKey = $"visit:{row.VisitId}", CollectionKey = $"visit:{row.VisitId}", ContextTitle = $"Visit of {row.VisitorName}",
                ContextSubtitle = string.IsNullOrWhiteSpace(row.VisitType) ? "Visit to SDD" : row.VisitType,
                Title = string.IsNullOrWhiteSpace(row.Caption) ? $"Visit of {row.VisitorName}" : row.Caption,
                Caption = row.Caption, MediaDate = date,
                ThumbnailUrl = Url.Page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = row.VisitId, photoId = row.Id, size = "md", v = row.VersionStamp }) ?? displayUrl,
                DisplayUrl = displayUrl,
                OriginalUrl = Url.Page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = row.VisitId, photoId = row.Id, size = "original", v = row.VersionStamp }) ?? displayUrl,
                SourceUrl = Url.Page("/Visits/Details", new { area = "ProjectOfficeReports", id = row.VisitId }),
                Width = row.Width, Height = row.Height, SortOrder = row.CreatedAtUtc.UtcDateTime.Ticks
            };
        }).ToList();
    }

    private async Task<List<MediaItem>> LoadSocialMediaPhotosAsync(CancellationToken cancellationToken)
    {
        if (ProjectId.HasValue) return new List<MediaItem>();
        var rows = await _db.SocialMediaEventPhotos.AsNoTracking().Select(photo => new
        {
            photo.Id, EventId = photo.SocialMediaEventId, EventTitle = photo.SocialMediaEvent!.Title,
            EventType = photo.SocialMediaEvent.SocialMediaEventType != null ? photo.SocialMediaEvent.SocialMediaEventType.Name : null,
            photo.SocialMediaEvent.DateOfEvent, photo.Caption, photo.Width, photo.Height, photo.IsCover, photo.VersionStamp, photo.CreatedAtUtc
        }).ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var date = row.DateOfEvent.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
            var displayUrl = Url.Page("/SocialMedia/ViewPhoto", new { area = "ProjectOfficeReports", id = row.EventId, photoId = row.Id, size = "story", v = row.VersionStamp }) ?? string.Empty;
            return new MediaItem
            {
                Id = $"event-photo:{row.Id}", Kind = MediaKind.Photo, Source = MediaSource.Event, SourceLabel = "Event",
                ContextKey = $"event:{row.EventId}", CollectionKey = $"event:{row.EventId}", ContextTitle = row.EventTitle,
                ContextSubtitle = string.IsNullOrWhiteSpace(row.EventType) ? "Social media event" : row.EventType,
                Title = string.IsNullOrWhiteSpace(row.Caption) ? row.EventTitle : row.Caption,
                Caption = row.Caption, MediaDate = date,
                ThumbnailUrl = Url.Page("/SocialMedia/ViewPhoto", new { area = "ProjectOfficeReports", id = row.EventId, photoId = row.Id, size = "feed", v = row.VersionStamp }) ?? displayUrl,
                DisplayUrl = displayUrl,
                OriginalUrl = Url.Page("/SocialMedia/ViewPhoto", new { area = "ProjectOfficeReports", id = row.EventId, photoId = row.Id, size = "original", v = row.VersionStamp }) ?? displayUrl,
                SourceUrl = Url.Page("/SocialMedia/Details", new { area = "ProjectOfficeReports", id = row.EventId }),
                Width = row.Width, Height = row.Height, IsCover = row.IsCover, SortOrder = row.CreatedAtUtc.UtcDateTime.Ticks
            };
        }).ToList();
    }

    private bool MatchesSearch(MediaItem item)
        => Q is null
           || Contains(item.Title, Q)
           || Contains(item.Caption, Q)
           || Contains(item.ContextTitle, Q)
           || Contains(item.ContextSubtitle, Q)
           || Contains(item.OriginalFileName, Q)
           || Contains(item.SourceLabel, Q);

    private bool MatchesClassification(MediaItem item)
        => Classification switch
        {
            "photograph" => item.Kind == MediaKind.Photo,
            "screenshot" => false,
            "unknown" => item.Kind == MediaKind.Video,
            _ => true
        };

    private static bool Contains(string? value, string query)
        => !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static string NormalizeSource(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "projects" => "projects",
            "visits" => "visits",
            "events" => "events",
            "external" or "nas" => "external",
            _ => "all"
        };

    private static string NormalizeKind(string? value)
        => value?.Trim().ToLowerInvariant() switch { "photo" => "photo", "video" => "video", _ => "all" };

    private static string NormalizeClassification(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "screenshot" => "screenshot", "photograph" => "photograph", "unknown" => "unknown", _ => "all"
        };

    public static string FormatDuration(int? totalSeconds)
    {
        if (!totalSeconds.HasValue || totalSeconds.Value <= 0) return string.Empty;
        var duration = TimeSpan.FromSeconds(totalSeconds.Value);
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    public sealed class MediaItem
    {
        public required string Id { get; init; }
        public required MediaKind Kind { get; init; }
        public required MediaSource Source { get; init; }
        public required string SourceLabel { get; init; }
        public required string ContextKey { get; init; }
        public required string CollectionKey { get; init; }
        public required string ContextTitle { get; init; }
        public required string ContextSubtitle { get; init; }
        public required string Title { get; init; }
        public string? Caption { get; init; }
        public string? OriginalFileName { get; init; }
        public required DateTime MediaDate { get; init; }
        public required string ThumbnailUrl { get; init; }
        public required string DisplayUrl { get; init; }
        public required string OriginalUrl { get; init; }
        public string? DownloadUrl { get; init; }
        public string? SourceUrl { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public int? DurationSeconds { get; init; }
        public bool IsCover { get; init; }
        public long SortOrder { get; init; }
        public double AspectRatio => Width.GetValueOrDefault() > 0 && Height.GetValueOrDefault() > 0
            ? Math.Clamp((double)Width!.Value / Height!.Value, .55d, 2.2d)
            : Kind == MediaKind.Video ? 16d / 9d : 1.35d;
    }

    public sealed record MediaGroup(string Key, string Title, string Subtitle, DateTime Date, IReadOnlyList<MediaItem> Items);
    public sealed record ProjectOption(int Id, string Name);
    public sealed class LibraryStats
    {
        public int Total { get; init; }
        public int Photos { get; init; }
        public int Videos { get; init; }
        public int Collections { get; init; }
    }

    public enum MediaKind { Photo, Video }
    public enum MediaSource { Project, Visit, Event, ExternalFolder }
}
