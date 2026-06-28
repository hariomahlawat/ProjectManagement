using System.Globalization;
using System.Text.Json;
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
    private readonly IMediaLibraryQueryService _library;
    private readonly MediaLibraryOptions _mediaOptions;

    public IndexModel(
        ApplicationDbContext db,
        IMediaLibraryQueryService library,
        IOptions<MediaLibraryOptions> mediaOptions)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _mediaOptions = mediaOptions?.Value ?? throw new ArgumentNullException(nameof(mediaOptions));
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string Source { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string Classification { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public int? ProjectId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? PersonId { get; set; }
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public IReadOnlyList<MediaItem> Items { get; private set; } = Array.Empty<MediaItem>();
    public IReadOnlyList<MediaGroup> Groups { get; private set; } = Array.Empty<MediaGroup>();
    public IReadOnlyList<ProjectOption> Projects { get; private set; } = Array.Empty<ProjectOption>();
    public IReadOnlyList<PersonOption> People { get; private set; } = Array.Empty<PersonOption>();
    public IReadOnlyList<int> Years { get; private set; } = Array.Empty<int>();
    public LibraryStats Stats { get; private set; } = new();
    public bool HasPreviousPage { get; private set; }
    public bool HasNextPage { get; private set; }
    public int CurrentPage => Math.Max(1, PageNumber);
    public bool ExternalSourcesEnabled => _mediaOptions.IsExternalSourceFeatureEnabled;
    public bool PeopleFeatureEnabled => _mediaOptions.People.Enabled
                                        && (User.IsInRole("Admin") || User.IsInRole("HoD"));
    public bool ExternalLibraryAvailable { get; private set; } = true;
    public string? ExternalLibraryWarning { get; private set; }
    public bool IsUsingCatalogue { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeRequest();

        var result = await _library.SearchAsync(
            new MediaLibraryQuery(
                Q, Source, Kind, Classification, ProjectId,
                PeopleFeatureEnabled ? PersonId : null,
                Year, PageNumber, PageSize, PeopleFeatureEnabled),
            cancellationToken);

        if (result.IsAvailable && result.HasPrismCatalogue)
        {
            ApplyCatalogueResult(result);
            return;
        }

        // Fail-safe path: the media catalogue is optional. Core PRISM media remains
        // browsable if migrations are pending, PostgreSQL is unavailable, or the worker
        // has not completed its first synchronisation.
        await LoadPrismFallbackAsync(cancellationToken);
        ExternalLibraryAvailable = result.IsAvailable;
        ExternalLibraryWarning = result.Warning;
        if (PersonId.HasValue)
        {
            ExternalLibraryWarning = string.Join(" ", new[]
            {
                ExternalLibraryWarning,
                "The selected person filter could not be applied because the media catalogue is unavailable."
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
            PersonId = null;
        }
    }

    private void NormalizeRequest()
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
        if (!PeopleFeatureEnabled)
        {
            PersonId = null;
        }
    }

    private void ApplyCatalogueResult(MediaLibraryQueryResult result)
    {
        IsUsingCatalogue = true;
        PageNumber = result.PageNumber;
        HasPreviousPage = result.HasPreviousPage;
        HasNextPage = result.HasNextPage;
        ExternalLibraryAvailable = true;
        ExternalLibraryWarning = result.Warning;
        Projects = result.Projects.Select(project => new ProjectOption(project.Id, project.Name)).ToList();
        People = result.People.Select(person => new PersonOption(person.Id, person.Name, person.PhotoCount)).ToList();
        Years = result.Years;
        Stats = new LibraryStats
        {
            Total = result.Statistics.Total,
            Photos = result.Statistics.Photos,
            Videos = result.Statistics.Videos,
            Collections = result.Statistics.Collections
        };
        Items = result.Items.Select(MapCatalogueItem).ToList();
        BuildGroups();
    }

    private MediaItem MapCatalogueItem(MediaLibraryQueryItem row)
    {
        var source = row.Origin switch
        {
            MediaAssetOrigin.ProjectPhoto or MediaAssetOrigin.ProjectVideo => MediaSource.Project,
            MediaAssetOrigin.VisitPhoto => MediaSource.Visit,
            MediaAssetOrigin.SocialMediaEventPhoto => MediaSource.Event,
            _ => MediaSource.ExternalFolder
        };

        var id = ExtractEntityId(row.SourceEntityId);
        var parentId = row.ParentEntityId ?? ExtractParentId(row.ContextKey);
        var version = row.VersionToken;

        string thumbnail;
        string display;
        string original;
        string? download = null;
        string? sourceUrl;

        switch (row.Origin)
        {
            case MediaAssetOrigin.ProjectPhoto:
                display = Url.Page("/Projects/Photos/View", new { id = parentId, photoId = id, size = "xl", v = version }) ?? string.Empty;
                thumbnail = Url.Page("/Projects/Photos/View", new { id = parentId, photoId = id, size = "md", v = version }) ?? display;
                original = Url.Page("/Projects/Photos/View", new { id = parentId, photoId = id, size = "original", v = version }) ?? display;
                download = Url.Page("/Projects/Photos/Download", new { id = parentId, photoId = id, size = "original" });
                sourceUrl = Url.Page("/Projects/Photos/Index", new { id = parentId });
                break;

            case MediaAssetOrigin.ProjectVideo:
                thumbnail = Url.Page("/Projects/Videos/Poster", new { id = parentId, videoId = id, v = version })
                            ?? Url.Content("~/img/placeholders/project-video-placeholder.svg");
                display = Url.Page("/Projects/Videos/Stream", new { id = parentId, videoId = id, v = version }) ?? string.Empty;
                original = display;
                sourceUrl = Url.Page("/Projects/Videos/Index", new { id = parentId });
                break;

            case MediaAssetOrigin.VisitPhoto:
                display = Url.Page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = "xl", v = version }) ?? string.Empty;
                thumbnail = Url.Page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = "md", v = version }) ?? display;
                original = Url.Page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = "original", v = version }) ?? display;
                sourceUrl = Url.Page("/Visits/Details", new { area = "ProjectOfficeReports", id = parentId });
                break;

            case MediaAssetOrigin.SocialMediaEventPhoto:
                display = Url.Page("/SocialMedia/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = "story", v = version }) ?? string.Empty;
                thumbnail = Url.Page("/SocialMedia/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = "feed", v = version }) ?? display;
                original = Url.Page("/SocialMedia/ViewPhoto", new { area = "ProjectOfficeReports", id = parentId, photoId = id, size = "original", v = version }) ?? display;
                sourceUrl = Url.Page("/SocialMedia/Details", new { area = "ProjectOfficeReports", id = parentId });
                break;

            default:
                display = Url.Page("/Photos/Media", new
                {
                    id = row.Id,
                    variant = row.Kind == MediaAssetKind.Photo ? "preview" : "original",
                    v = row.CacheVersion
                }) ?? string.Empty;
                thumbnail = row.Kind == MediaAssetKind.Photo
                    ? Url.Page("/Photos/Media", new { id = row.Id, variant = "thumb", v = row.CacheVersion }) ?? display
                    : Url.Content("~/img/placeholders/project-video-placeholder.svg");
                original = Url.Page("/Photos/Media", new { id = row.Id, variant = "original", v = row.CacheVersion }) ?? display;
                download = Url.Page("/Photos/Media", new { id = row.Id, variant = "original", download = true, v = row.CacheVersion });
                sourceUrl = Url.Page("/Photos/Index", new { Source = "external", Q = row.ParentEntityId });
                break;
        }

        return new MediaItem
        {
            Id = $"catalogue:{row.Id}",
            AssetId = row.Id,
            Kind = row.Kind == MediaAssetKind.Video ? MediaKind.Video : MediaKind.Photo,
            Source = source,
            SourceLabel = row.SourceLabel,
            Classification = row.Classification,
            People = row.People.Select(person => new PersonSummary(person.Id, person.DisplayName)).ToList(),
            UnidentifiedFaceCount = row.UnidentifiedFaceCount,
            ContextKey = row.ContextKey,
            CollectionKey = row.CollectionKey,
            ContextTitle = row.ContextTitle,
            ContextSubtitle = row.ContextSubtitle,
            Title = row.Title,
            Caption = row.Caption,
            OriginalFileName = row.OriginalFileName,
            MediaDate = row.MediaDateUtc.ToLocalTime().DateTime,
            ThumbnailUrl = thumbnail,
            DisplayUrl = display,
            OriginalUrl = original,
            DownloadUrl = download,
            SourceUrl = sourceUrl,
            Width = row.Width,
            Height = row.Height,
            DurationSeconds = row.DurationSeconds,
            IsCover = row.IsCover,
            SortOrder = row.SortOrder
        };
    }

    private async Task LoadPrismFallbackAsync(CancellationToken cancellationToken)
    {
        IsUsingCatalogue = false;
        var items = new List<MediaItem>(512);

        if (Source is "all" or "projects")
        {
            if (Kind is "all" or "photo") items.AddRange(await LoadProjectPhotosAsync(cancellationToken));
            if (Kind is "all" or "video") items.AddRange(await LoadProjectVideosAsync(cancellationToken));
        }

        if (Kind is "all" or "photo")
        {
            if (Source is "all" or "visits") items.AddRange(await LoadVisitPhotosAsync(cancellationToken));
            if (Source is "all" or "events") items.AddRange(await LoadSocialMediaPhotosAsync(cancellationToken));
        }

        var filtered = items
            .Where(MatchesSearch)
            .Where(MatchesClassification)
            .Where(item => !Year.HasValue || item.MediaDate.Year == Year.Value)
            .OrderByDescending(item => item.MediaDate)
            .ThenBy(item => item.ContextTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        var total = filtered.Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        PageNumber = Math.Clamp(PageNumber, 1, pageCount);
        var skip = (PageNumber - 1) * PageSize;
        HasPreviousPage = PageNumber > 1;
        HasNextPage = total > skip + PageSize;
        Items = filtered.Skip(skip).Take(PageSize).ToList();

        Years = items.Where(MatchesSearch).Where(MatchesClassification)
            .Select(item => item.MediaDate.Year).Distinct().OrderByDescending(year => year).ToList();
        Projects = await _db.Projects.AsNoTracking()
            .Where(project => !project.IsDeleted)
            .Where(project => _db.ProjectPhotos.Any(photo => photo.ProjectId == project.Id)
                              || _db.ProjectVideos.Any(video => video.ProjectId == project.Id))
            .OrderBy(project => project.Name)
            .Select(project => new ProjectOption(project.Id, project.Name))
            .ToListAsync(cancellationToken);
        Stats = new LibraryStats
        {
            Total = total,
            Photos = filtered.Count(item => item.Kind == MediaKind.Photo),
            Videos = filtered.Count(item => item.Kind == MediaKind.Video),
            Collections = filtered.Select(item => item.CollectionKey).Distinct(StringComparer.Ordinal).Count()
        };
        BuildGroups();
    }

    private void BuildGroups()
    {
        Groups = Items
            .GroupBy(item => new { item.ContextKey, item.ContextTitle, item.ContextSubtitle, Date = item.MediaDate.Date })
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
            var display = Url.Page("/Projects/Photos/View", new { id = row.ProjectId, photoId = row.Id, size = "xl", v = row.Version }) ?? string.Empty;
            return new MediaItem
            {
                Id = $"project-photo:{row.Id}", Kind = MediaKind.Photo, Source = MediaSource.Project,
                SourceLabel = "Project", ContextKey = $"project:{row.ProjectId}", CollectionKey = $"project:{row.ProjectId}",
                ContextTitle = row.ProjectName, ContextSubtitle = "Project media",
                Title = string.IsNullOrWhiteSpace(row.Caption) ? row.OriginalFileName : row.Caption,
                Caption = row.Caption, OriginalFileName = row.OriginalFileName,
                MediaDate = DateTime.SpecifyKind(row.CreatedUtc, DateTimeKind.Utc),
                ThumbnailUrl = Url.Page("/Projects/Photos/View", new { id = row.ProjectId, photoId = row.Id, size = "md", v = row.Version }) ?? display,
                DisplayUrl = display,
                OriginalUrl = Url.Page("/Projects/Photos/View", new { id = row.ProjectId, photoId = row.Id, size = "original", v = row.Version }) ?? display,
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
        if (ProjectId.HasValue) return new();
        var rows = await _db.VisitPhotos.AsNoTracking().Select(photo => new
        {
            photo.Id, photo.VisitId, VisitorName = photo.Visit!.VisitorName,
            VisitType = photo.Visit.VisitType != null ? photo.Visit.VisitType.Name : null,
            photo.Visit.DateOfVisit, photo.Caption, photo.Width, photo.Height, photo.VersionStamp, photo.CreatedAtUtc
        }).ToListAsync(cancellationToken);
        return rows.Select(row =>
        {
            var display = Url.Page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = row.VisitId, photoId = row.Id, size = "xl", v = row.VersionStamp }) ?? string.Empty;
            return new MediaItem
            {
                Id = $"visit-photo:{row.Id}", Kind = MediaKind.Photo, Source = MediaSource.Visit, SourceLabel = "Visit",
                ContextKey = $"visit:{row.VisitId}", CollectionKey = $"visit:{row.VisitId}", ContextTitle = $"Visit of {row.VisitorName}",
                ContextSubtitle = string.IsNullOrWhiteSpace(row.VisitType) ? "Visit to SDD" : row.VisitType,
                Title = string.IsNullOrWhiteSpace(row.Caption) ? $"Visit of {row.VisitorName}" : row.Caption,
                Caption = row.Caption, MediaDate = row.DateOfVisit.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local),
                ThumbnailUrl = Url.Page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = row.VisitId, photoId = row.Id, size = "md", v = row.VersionStamp }) ?? display,
                DisplayUrl = display,
                OriginalUrl = Url.Page("/Visits/ViewPhoto", new { area = "ProjectOfficeReports", id = row.VisitId, photoId = row.Id, size = "original", v = row.VersionStamp }) ?? display,
                SourceUrl = Url.Page("/Visits/Details", new { area = "ProjectOfficeReports", id = row.VisitId }),
                Width = row.Width, Height = row.Height, SortOrder = row.CreatedAtUtc.UtcDateTime.Ticks
            };
        }).ToList();
    }

    private async Task<List<MediaItem>> LoadSocialMediaPhotosAsync(CancellationToken cancellationToken)
    {
        if (ProjectId.HasValue) return new();
        var rows = await _db.SocialMediaEventPhotos.AsNoTracking().Select(photo => new
        {
            photo.Id, EventId = photo.SocialMediaEventId, EventTitle = photo.SocialMediaEvent!.Title,
            EventType = photo.SocialMediaEvent.SocialMediaEventType != null ? photo.SocialMediaEvent.SocialMediaEventType.Name : null,
            photo.SocialMediaEvent.DateOfEvent, photo.Caption, photo.Width, photo.Height, photo.IsCover, photo.VersionStamp, photo.CreatedAtUtc
        }).ToListAsync(cancellationToken);
        return rows.Select(row =>
        {
            var display = Url.Page("/SocialMedia/ViewPhoto", new { area = "ProjectOfficeReports", id = row.EventId, photoId = row.Id, size = "story", v = row.VersionStamp }) ?? string.Empty;
            return new MediaItem
            {
                Id = $"event-photo:{row.Id}", Kind = MediaKind.Photo, Source = MediaSource.Event, SourceLabel = "Event",
                ContextKey = $"event:{row.EventId}", CollectionKey = $"event:{row.EventId}", ContextTitle = row.EventTitle,
                ContextSubtitle = string.IsNullOrWhiteSpace(row.EventType) ? "Social media event" : row.EventType,
                Title = string.IsNullOrWhiteSpace(row.Caption) ? row.EventTitle : row.Caption,
                Caption = row.Caption, MediaDate = row.DateOfEvent.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local),
                ThumbnailUrl = Url.Page("/SocialMedia/ViewPhoto", new { area = "ProjectOfficeReports", id = row.EventId, photoId = row.Id, size = "feed", v = row.VersionStamp }) ?? display,
                DisplayUrl = display,
                OriginalUrl = Url.Page("/SocialMedia/ViewPhoto", new { area = "ProjectOfficeReports", id = row.EventId, photoId = row.Id, size = "original", v = row.VersionStamp }) ?? display,
                SourceUrl = Url.Page("/SocialMedia/Details", new { area = "ProjectOfficeReports", id = row.EventId }),
                Width = row.Width, Height = row.Height, IsCover = row.IsCover, SortOrder = row.CreatedAtUtc.UtcDateTime.Ticks
            };
        }).ToList();
    }

    private bool MatchesSearch(MediaItem item)
        => Q is null || Contains(item.Title, Q) || Contains(item.Caption, Q)
           || Contains(item.ContextTitle, Q) || Contains(item.ContextSubtitle, Q)
           || Contains(item.OriginalFileName, Q) || Contains(item.SourceLabel, Q);

    private bool MatchesClassification(MediaItem item)
        => Classification switch
        {
            "photograph" => item.Kind == MediaKind.Photo,
            "unknown" => item.Kind == MediaKind.Video,
            "screenshot" or "scanned-document" or "diagram" or "presentation-slide" or "graphic" => false,
            _ => true
        };

    private static string ExtractEntityId(string sourceEntityId)
    {
        var separator = sourceEntityId.IndexOf(':');
        return separator >= 0 && separator + 1 < sourceEntityId.Length
            ? sourceEntityId[(separator + 1)..]
            : sourceEntityId;
    }

    private static string ExtractParentId(string contextKey)
    {
        var separator = contextKey.IndexOf(':');
        return separator >= 0 && separator + 1 < contextKey.Length
            ? contextKey[(separator + 1)..]
            : contextKey;
    }

    private static bool Contains(string? value, string query)
        => !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static string NormalizeSource(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "projects" => "projects", "visits" => "visits", "events" => "events",
            "external" or "nas" => "external", _ => "all"
        };

    private static string NormalizeKind(string? value)
        => value?.Trim().ToLowerInvariant() switch { "photo" => "photo", "video" => "video", _ => "all" };

    private static string NormalizeClassification(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "photograph" => "photograph",
            "screenshot" => "screenshot",
            "scanneddocument" or "scanned-document" => "scanned-document",
            "diagram" => "diagram",
            "presentationslide" or "presentation-slide" => "presentation-slide",
            "graphic" => "graphic",
            "unknown" => "unknown",
            _ => "all"
        };

    public string SerializeViewerPeople(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return JsonSerializer.Serialize(item.People.Select(person => new
        {
            name = person.Name,
            url = Url.Page("/Photos/People/Details", new { id = person.Id }) ?? string.Empty
        }));
    }

    public static string ClassificationLabel(MediaClassification classification)
        => classification switch
        {
            MediaClassification.Photograph => "Photograph",
            MediaClassification.Screenshot => "Screenshot",
            MediaClassification.ScannedDocument => "Scanned document",
            MediaClassification.Diagram => "Diagram",
            MediaClassification.PresentationSlide => "Presentation slide",
            MediaClassification.Graphic => "Graphic",
            _ => "Not classified"
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
        public long? AssetId { get; init; }
        public required MediaKind Kind { get; init; }
        public required MediaSource Source { get; init; }
        public required string SourceLabel { get; init; }
        public MediaClassification Classification { get; init; } = MediaClassification.Unknown;
        public IReadOnlyList<PersonSummary> People { get; init; } = Array.Empty<PersonSummary>();
        public int UnidentifiedFaceCount { get; init; }
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
    public sealed record PersonOption(Guid Id, string Name, int PhotoCount);
    public sealed record PersonSummary(Guid Id, string Name);
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
