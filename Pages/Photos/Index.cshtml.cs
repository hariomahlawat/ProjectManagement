using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Pages.Photos;

[Authorize]
public sealed class IndexModel : PageModel
{
    private const int PageSize = 120;
    private readonly ApplicationDbContext _db;
    private readonly MediaLibraryDbContext _mediaDb;
    private readonly IMediaLibraryQueryService _library;
    private readonly IPrismMediaSourceSnapshotService _sourceSnapshot;
    private readonly IMediaCatalogueConsistencyService _consistencyService;
    private readonly MediaLibraryOptions _mediaOptions;
    private readonly IProtectedFileUrlBuilder _fileUrlBuilder;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext db,
        MediaLibraryDbContext mediaDb,
        IMediaLibraryQueryService library,
        IPrismMediaSourceSnapshotService sourceSnapshot,
        IMediaCatalogueConsistencyService consistencyService,
        IOptions<MediaLibraryOptions> mediaOptions,
        IProtectedFileUrlBuilder fileUrlBuilder,
        ILogger<IndexModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _mediaDb = mediaDb ?? throw new ArgumentNullException(nameof(mediaDb));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _sourceSnapshot = sourceSnapshot ?? throw new ArgumentNullException(nameof(sourceSnapshot));
        _consistencyService = consistencyService ?? throw new ArgumentNullException(nameof(consistencyService));
        _mediaOptions = mediaOptions?.Value ?? throw new ArgumentNullException(nameof(mediaOptions));
        _fileUrlBuilder = fileUrlBuilder ?? throw new ArgumentNullException(nameof(fileUrlBuilder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string View { get; set; } = "photos";
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
    public bool CatalogueCatchUpPending { get; private set; }
    public int SourceVisibleCount { get; private set; }
    public long CatalogueBackedCount { get; private set; }
    public long AwaitingCatalogueCount => Math.Max(0L, SourceVisibleCount - CatalogueBackedCount);
    public string LibraryRevision { get; private set; } = "initial";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeRequest();

        var sourceSnapshot = await _sourceSnapshot.GetSnapshotAsync(cancellationToken);
        var catalogueFreshness = await GetCatalogueFreshnessAsync(sourceSnapshot, cancellationToken);
        SourceVisibleCount = sourceSnapshot.TotalCount;
        CatalogueBackedCount = catalogueFreshness.IndexedAssetCount;
        var catalogueRepresentationsComplete = false;
        try
        {
            var consistency = await _consistencyService.CheckAsync(cancellationToken);
            CatalogueBackedCount = consistency.AvailableCatalogueRecords;
            SourceVisibleCount = consistency.AvailableCatalogueRecords + consistency.MissingFromCatalogue;
            catalogueRepresentationsComplete = consistency.MissingFromCatalogue == 0;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            _logger.LogDebug(ex, "Unable to calculate visible/catalogued PRISM media counts for the Photos status banner.");
        }
        LibraryRevision = BuildLibraryRevision(sourceSnapshot, catalogueFreshness);

        var result = await _library.SearchAsync(
            new MediaLibraryQuery(
                Q, Source, Kind, Classification, ProjectId,
                PeopleFeatureEnabled ? PersonId : null,
                Year, PageNumber, PageSize, PeopleFeatureEnabled),
            cancellationToken);

        if (result.IsAvailable
            && (catalogueFreshness.IsFresh
                || catalogueRepresentationsComplete
                || Source == "external"))
        {
            ApplyCatalogueResult(result);
            return;
        }

        // A catalogue row existing is not proof that it represents the latest PRISM
        // uploads. When the source revision is newer than the last successful catalogue
        // pass, read PRISM-owned media directly so uploads remain visible immediately.
        // The catalogue continues catching up in the background and later restores
        // classification and people enrichment.
        CatalogueCatchUpPending = catalogueFreshness.HasSource
                                  && !catalogueFreshness.IsFresh
                                  && !catalogueRepresentationsComplete;
        if (CatalogueCatchUpPending)
        {
            await RequestCatalogueCatchUpAsync(catalogueFreshness.SourceId, cancellationToken);
        }

        var fallbackWarnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.Warning)) fallbackWarnings.Add(result.Warning);
        if (Classification != "all")
        {
            fallbackWarnings.Add("The classification filter was cleared while new PRISM media is being catalogued.");
            Classification = "all";
        }
        if (PersonId.HasValue)
        {
            fallbackWarnings.Add("The person filter was cleared while new PRISM media is being catalogued.");
            PersonId = null;
        }

        await LoadPrismFallbackAsync(catalogueFreshness.SourceId, cancellationToken);
        ExternalLibraryAvailable = result.IsAvailable;
        ExternalLibraryWarning = fallbackWarnings.Count == 0
            ? null
            : string.Join(" ", fallbackWarnings);
    }

    public async Task<IActionResult> OnGetRevisionAsync(CancellationToken cancellationToken)
    {
        var sourceSnapshot = await _sourceSnapshot.GetSnapshotAsync(cancellationToken);
        var catalogueFreshness = await GetCatalogueFreshnessAsync(sourceSnapshot, cancellationToken);
        if (!catalogueFreshness.IsFresh)
        {
            await RequestCatalogueCatchUpAsync(catalogueFreshness.SourceId, cancellationToken);
        }

        return new JsonResult(new
        {
            revision = BuildLibraryRevision(sourceSnapshot, catalogueFreshness),
            sourceCount = sourceSnapshot.TotalCount,
            catalogueFresh = catalogueFreshness.IsFresh
        });
    }

    private async Task<PrismCatalogueFreshness> GetCatalogueFreshnessAsync(
        PrismMediaSourceSnapshot sourceSnapshot,
        CancellationToken cancellationToken)
    {
        if (!_mediaOptions.IsCatalogueEnabled || !_mediaOptions.Catalogue.SynchronizePrismMedia)
        {
            return PrismCatalogueFreshness.Unavailable;
        }

        try
        {
            var source = await _mediaDb.Sources
                .AsNoTracking()
                .Where(item => item.Key == MediaSourceBootstrapper.PrismSourceKey && !item.IsDeleted)
                .Select(item => new
                {
                    item.Id,
                    item.ConfigurationFingerprint,
                    item.ScanStatus,
                    item.LastSuccessfulScanAtUtc,
                    item.IndexedAssetCount
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (source is null)
            {
                return PrismCatalogueFreshness.Unavailable;
            }

            var isFresh = source.LastSuccessfulScanAtUtc.HasValue
                          && string.Equals(source.ScanStatus, "Healthy", StringComparison.OrdinalIgnoreCase)
                          && string.Equals(
                              source.ConfigurationFingerprint,
                              sourceSnapshot.Fingerprint,
                              StringComparison.Ordinal);

            return new PrismCatalogueFreshness(
                source.Id,
                true,
                isFresh,
                source.ConfigurationFingerprint,
                source.ScanStatus,
                source.LastSuccessfulScanAtUtc,
                source.IndexedAssetCount);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            _logger.LogWarning(ex, "Unable to verify PRISM media catalogue freshness; using source-owned media directly.");
            return PrismCatalogueFreshness.Unavailable;
        }
    }

    private async Task RequestCatalogueCatchUpAsync(Guid? sourceId, CancellationToken cancellationToken)
    {
        if (!sourceId.HasValue)
        {
            return;
        }

        try
        {
            var source = await _mediaDb.Sources
                .SingleOrDefaultAsync(item => item.Id == sourceId.Value, cancellationToken);
            if (source is null)
            {
                return;
            }

            var requestAlreadyPending = source.ScanRequestedAtUtc.HasValue
                                        && (!source.LastScanStartedAtUtc.HasValue
                                            || source.ScanRequestedAtUtc > source.LastScanStartedAtUtc);
            if (requestAlreadyPending)
            {
                return;
            }

            source.ScanRequestedAtUtc = DateTimeOffset.UtcNow;
            await _mediaDb.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
        {
            _logger.LogWarning(ex, "Unable to request PRISM media catalogue catch-up.");
        }
    }

    private static string BuildLibraryRevision(
        PrismMediaSourceSnapshot sourceSnapshot,
        PrismCatalogueFreshness catalogueFreshness)
        => string.Concat(
            sourceSnapshot.Fingerprint,
            ":",
            catalogueFreshness.CatalogueFingerprint ?? "none",
            ":",
            catalogueFreshness.IndexedAssetCount.ToString(CultureInfo.InvariantCulture),
            ":",
            catalogueFreshness.ScanStatus ?? "none");

    private void NormalizeRequest()
    {
        View = string.Equals(View?.Trim(), "collections", StringComparison.OrdinalIgnoreCase) ? "collections" : "photos";
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
            MediaAssetOrigin.ActivityPhoto => MediaSource.Activity,
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

            case MediaAssetOrigin.ActivityPhoto:
                display = Url.Page("/Photos/Media", new { id = row.Id, variant = "preview", v = row.CacheVersion }) ?? string.Empty;
                thumbnail = Url.Page("/Photos/Media", new { id = row.Id, variant = "thumb", v = row.CacheVersion }) ?? display;
                original = Url.Page("/Photos/Media", new { id = row.Id, variant = "original", v = row.CacheVersion }) ?? display;
                download = Url.Page("/Photos/Media", new { id = row.Id, variant = "original", download = true, v = row.CacheVersion });
                sourceUrl = Url.Page("/Activities/Details", new { id = parentId });
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
            SortOrder = row.SortOrder,
            VersionToken = row.VersionToken
        };
    }

    private async Task LoadPrismFallbackAsync(Guid? prismSourceId, CancellationToken cancellationToken)
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
            if (Source is "all" or "activities") items.AddRange(await LoadActivityPhotosAsync(cancellationToken));
        }

        // Do not resurrect historical rows whose physical source was already proven
        // unavailable. Restoration is performed only by the availability recovery workflow
        // after the content provider successfully opens the underlying file.
        if (prismSourceId.HasValue && items.Count > 0)
        {
            try
            {
                var sourceStates = await _mediaDb.Assets
                    .AsNoTracking()
                    .Where(asset => asset.SourceId == prismSourceId.Value && !asset.IsDeleted)
                    .Select(asset => new CatalogueAssetState(
                        asset.SourceEntityId,
                        asset.IsAvailable && asset.AvailabilityStatus == MediaAvailabilityStatus.Available))
                    .ToDictionaryAsync(state => state.SourceEntityId, StringComparer.Ordinal, cancellationToken);

                items = items
                    .Where(item => !sourceStates.TryGetValue(item.Id, out var state)
                                   || state.IsAvailable)
                    .ToList();
            }
            catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
            {
                _logger.LogWarning(ex, "Unable to read catalogue availability while rendering live PRISM media.");
            }
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
                Width = row.Width, Height = row.Height, IsCover = row.IsCover, SortOrder = row.Ordinal, VersionToken = row.Version.ToString(CultureInfo.InvariantCulture)
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
            DurationSeconds = row.DurationSeconds, IsCover = row.IsFeatured, SortOrder = row.Ordinal, VersionToken = row.Version.ToString(CultureInfo.InvariantCulture)
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
                Width = row.Width, Height = row.Height, SortOrder = row.CreatedAtUtc.UtcDateTime.Ticks, VersionToken = row.VersionStamp
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
                Width = row.Width, Height = row.Height, IsCover = row.IsCover, SortOrder = row.CreatedAtUtc.UtcDateTime.Ticks, VersionToken = row.VersionStamp
            };
        }).ToList();
    }

    private async Task<List<MediaItem>> LoadActivityPhotosAsync(CancellationToken cancellationToken)
    {
        if (ProjectId.HasValue) return new();
        var rows = await _db.ActivityAttachments
            .AsNoTracking()
            .Where(attachment => !attachment.Activity.IsDeleted)
            .Where(ActivityAttachmentClassifier.IsPhotoExpression)
            .Select(attachment => new
            {
                attachment.Id,
                attachment.ActivityId,
                ActivityTitle = attachment.Activity.Title,
                ActivityType = attachment.Activity.ActivityType.Name,
                attachment.Activity.Location,
                attachment.Activity.ScheduledStartUtc,
                attachment.StorageKey,
                attachment.OriginalFileName,
                attachment.ContentType,
                attachment.UploadedAtUtc,
                attachment.RowVersion
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var version = Convert.ToHexString(row.RowVersion ?? Array.Empty<byte>());
            var inlineUrl = _fileUrlBuilder.CreateInlineUrl(
                row.StorageKey,
                row.OriginalFileName,
                row.ContentType);
            return new MediaItem
            {
                Id = $"activity-photo:{row.Id}",
                Kind = MediaKind.Photo,
                Source = MediaSource.Activity,
                SourceLabel = "Activity",
                ContextKey = $"activity:{row.ActivityId}",
                CollectionKey = $"activity:{row.ActivityId}",
                ContextTitle = row.ActivityTitle,
                ContextSubtitle = string.IsNullOrWhiteSpace(row.ActivityType)
                    ? "Institutional activity"
                    : row.ActivityType,
                Title = row.ActivityTitle,
                Caption = row.Location,
                OriginalFileName = row.OriginalFileName,
                MediaDate = (row.ScheduledStartUtc ?? row.UploadedAtUtc).LocalDateTime,
                ThumbnailUrl = inlineUrl,
                DisplayUrl = inlineUrl,
                OriginalUrl = inlineUrl,
                DownloadUrl = _fileUrlBuilder.CreateDownloadUrl(
                    row.StorageKey,
                    row.OriginalFileName,
                    row.ContentType),
                SourceUrl = Url.Page("/Activities/Details", new { id = row.ActivityId }),
                SortOrder = row.UploadedAtUtc.UtcDateTime.Ticks,
                VersionToken = version
            };
        }).ToList();
    }

    private bool MatchesSearch(MediaItem item)
        => Q is null || Contains(item.Title, Q) || Contains(item.Caption, Q)
           || Contains(item.ContextTitle, Q) || Contains(item.ContextSubtitle, Q)
           || Contains(item.OriginalFileName, Q) || Contains(item.SourceLabel, Q);

    private bool MatchesClassification(MediaItem item)
        // The fallback reads source-owned media without classification state. The request
        // is normalized to "all" before this path is entered; no classification is inferred.
        => Classification == "all";

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
            "activities" => "activities",
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

    public static string PeopleBadgeTitle(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.People.Count > 0)
        {
            var confirmed = string.Join(", ", item.People.Select(person => person.Name));
            return item.UnidentifiedFaceCount > 0
                ? $"{confirmed}; {item.UnidentifiedFaceCount} unidentified"
                : confirmed;
        }

        return item.UnidentifiedFaceCount == 1
            ? "1 unidentified person"
            : $"{item.UnidentifiedFaceCount} unidentified people";
    }

    public static string PeopleBadgeLabel(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.People.Count == 1 && item.UnidentifiedFaceCount == 0)
        {
            return item.People[0].Name;
        }

        if (item.People.Count > 0)
        {
            var additional = item.People.Count - 1 + item.UnidentifiedFaceCount;
            return additional > 0
                ? $"{item.People[0].Name} +{additional}"
                : item.People[0].Name;
        }

        return item.UnidentifiedFaceCount == 1
            ? "1 unidentified"
            : $"{item.UnidentifiedFaceCount} unidentified";
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

    private sealed record CatalogueAssetState(
        string SourceEntityId,
        bool IsAvailable);

    private sealed record PrismCatalogueFreshness(
        Guid? SourceId,
        bool HasSource,
        bool IsFresh,
        string? CatalogueFingerprint,
        string? ScanStatus,
        DateTimeOffset? LastSuccessfulScanAtUtc,
        long IndexedAssetCount)
    {
        public static PrismCatalogueFreshness Unavailable { get; } = new(null, false, false, null, null, null, 0);
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
        public string? VersionToken { get; init; }
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
    public enum MediaSource { Project, Visit, Event, Activity, ExternalFolder }
}
