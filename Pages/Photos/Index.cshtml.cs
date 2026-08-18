using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
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
public sealed partial class IndexModel : PageModel
{
    private const int PageSize = 120;
    private const int CollectionPageSize = 48;
    private const int AlbumPageSize = 48;
    private readonly ApplicationDbContext _db;
    private readonly MediaLibraryDbContext _mediaDb;
    private readonly IMediaLibraryQueryService _library;
    private readonly IMediaCollectionQueryService _collections;
    private readonly IMediaAlbumService _albums;
    private readonly IPrismMediaSourceSnapshotService _sourceSnapshot;
    private readonly MediaLibraryOptions _mediaOptions;
    private readonly IProtectedFileUrlBuilder _fileUrlBuilder;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext db,
        MediaLibraryDbContext mediaDb,
        IMediaLibraryQueryService library,
        IMediaCollectionQueryService collections,
        IMediaAlbumService albums,
        IPrismMediaSourceSnapshotService sourceSnapshot,
        IOptions<MediaLibraryOptions> mediaOptions,
        IProtectedFileUrlBuilder fileUrlBuilder,
        ILogger<IndexModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _mediaDb = mediaDb ?? throw new ArgumentNullException(nameof(mediaDb));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _collections = collections ?? throw new ArgumentNullException(nameof(collections));
        _albums = albums ?? throw new ArgumentNullException(nameof(albums));
        _sourceSnapshot = sourceSnapshot ?? throw new ArgumentNullException(nameof(sourceSnapshot));
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
    // PersonId is retained for backwards-compatible links. New links use PersonIds so
    // a user can browse one person or photographs containing a selected group.
    [BindProperty(SupportsGet = true)] public Guid? PersonId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid[] PersonIds { get; set; } = Array.Empty<Guid>();
    [BindProperty(SupportsGet = true)] public string PeopleMatch { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "newest";
    [BindProperty(SupportsGet = true)] public string? Collection { get; set; }
    [BindProperty(SupportsGet = true)] public string CollectionTab { get; set; } = "source";
    [BindProperty(SupportsGet = true)] public Guid? AlbumId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? AddToAlbumId { get; set; }
    [BindProperty(SupportsGet = true)] public bool IncludeSingletonCollections { get; set; }
    [BindProperty(SupportsGet = true)] public bool IncludeArchivedAlbums { get; set; }
    [BindProperty(SupportsGet = true)] public bool OrganizeAlbum { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public IReadOnlyList<MediaItem> Items { get; private set; } = Array.Empty<MediaItem>();
    public IReadOnlyList<MediaGroup> Groups { get; private set; } = Array.Empty<MediaGroup>();
    public IReadOnlyList<CollectionCard> Collections { get; private set; } = Array.Empty<CollectionCard>();
    public IReadOnlyList<AlbumCard> Albums { get; private set; } = Array.Empty<AlbumCard>();
    public IReadOnlyList<MediaAlbumOption> ManageableAlbums { get; private set; } = Array.Empty<MediaAlbumOption>();
    public MediaAlbumDetails? CurrentAlbum { get; private set; }
    public MediaAlbumDetails? AddMediaTargetAlbum { get; private set; }
    public IReadOnlySet<long> AddMediaExistingAssetIds { get; private set; } = new HashSet<long>();
    public string? CurrentAlbumCreatorDisplayName { get; private set; }
    public string? AddMediaTargetWarning { get; private set; }
    public IReadOnlyList<ProjectOption> Projects { get; private set; } = Array.Empty<ProjectOption>();
    public IReadOnlyList<PersonOption> People { get; private set; } = Array.Empty<PersonOption>();
    public IReadOnlyList<PersonOption> SelectedPeople { get; private set; } = Array.Empty<PersonOption>();
    public IReadOnlyList<int> Years { get; private set; } = Array.Empty<int>();
    public LibraryStats Stats { get; private set; } = new();
    public bool HasPreviousPage { get; private set; }
    public bool HasNextPage { get; private set; }
    public int CurrentPage => Math.Max(1, PageNumber);
    public bool ExternalSourcesEnabled => _mediaOptions.IsExternalSourceFeatureEnabled;
    public bool PeopleFeatureEnabled => _mediaOptions.People.Enabled;
    public bool CanManagePeople => User.IsInRole("Admin") || User.IsInRole("HoD");
    public bool CanManageAnyAlbum => User.IsInRole("Admin") || User.IsInRole("HoD") || User.IsInRole("Comdt");
    public bool CanEditEditorialMetadata => CanManageAnyAlbum;
    public bool IsPeopleGallery => PersonIds.Length > 0;
    public bool IsCollectionDetail => !string.IsNullOrWhiteSpace(Collection);
    public bool IsAlbumsWorkspace => View == "collections" && CollectionTab == "albums" && !AlbumId.HasValue;
    public bool IsAlbumDetail => View == "album" && AlbumId.HasValue;
    public bool CanManageCurrentAlbum => CurrentAlbum?.CanManage == true;
    public bool CanAddMediaToCurrentAlbum => PhotosCurationPresentation.CanAddMedia(CurrentAlbum);
    public bool CanOrganizeCurrentAlbum => PhotosCurationPresentation.CanOrganize(CurrentAlbum);
    public bool IsAddMediaMode => View == "photos" && AddMediaTargetAlbum is not null;
    public bool MatchAllSelectedPeople => !string.Equals(PeopleMatch, "any", StringComparison.OrdinalIgnoreCase);
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
        await LoadSelectedPeopleAsync(cancellationToken);
        await LoadAddMediaTargetAsync(cancellationToken);

        var sourceSnapshot = await _sourceSnapshot.GetSnapshotAsync(cancellationToken);
        var catalogueFreshness = await GetCatalogueFreshnessAsync(sourceSnapshot, cancellationToken);
        SourceVisibleCount = sourceSnapshot.TotalCount;
        CatalogueBackedCount = catalogueFreshness.IndexedAssetCount;
        LibraryRevision = BuildLibraryRevision(sourceSnapshot, catalogueFreshness);

        if (IsAlbumsWorkspace)
        {
            await LoadAlbumsWorkspaceAsync(cancellationToken);
            return;
        }

        if (IsAlbumDetail)
        {
            await LoadAlbumDetailAsync(cancellationToken);
            return;
        }

        var result = await _library.SearchAsync(
            new MediaLibraryQuery(
                Q, Source, Kind, Classification, ProjectId,
                null,
                Year, PageNumber, PageSize, PeopleFeatureEnabled,
                PeopleFeatureEnabled ? PersonIds : Array.Empty<Guid>(),
                PeopleMatch,
                CanManagePeople,
                Sort,
                Collection),
            cancellationToken);

        var identityFilterRequiresCatalogue = PeopleFeatureEnabled && PersonIds.Length > 0;
        if (result.IsAvailable
            && result.HasPrismCatalogue
            && (catalogueFreshness.IsFresh || Source == "external" || identityFilterRequiresCatalogue))
        {
            if (!catalogueFreshness.IsFresh && identityFilterRequiresCatalogue)
            {
                CatalogueCatchUpPending = true;
                await RequestCatalogueCatchUpAsync(catalogueFreshness.SourceId, cancellationToken);
            }

            if (View == "collections" && !IsPeopleGallery)
            {
                ApplyCatalogueResult(result, includeTimeline: false);
                await LoadCatalogueCollectionsAsync(cancellationToken);
            }
            else
            {
                ApplyCatalogueResult(result, includeTimeline: true);
                await LoadManageableAlbumsSafeAsync(cancellationToken);
            }
            return;
        }

        // A catalogue row existing is not proof that it represents the latest PRISM
        // uploads. When the source revision is newer than the last successful catalogue
        // pass, read PRISM-owned media directly so uploads remain visible immediately.
        // The catalogue continues catching up in the background and later restores
        // classification and people enrichment.
        CatalogueCatchUpPending = result.HasPrismCatalogue && !catalogueFreshness.IsFresh;
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
        if (PersonIds.Length > 0)
        {
            // Never clear a people filter and show unrelated source media. Identity filters
            // depend on the durable catalogue, so fail closed while it is unavailable.
            ExternalLibraryAvailable = false;
            ExternalLibraryWarning = "The people gallery is temporarily unavailable while identity intelligence is being catalogued. No unrelated photographs have been substituted.";
            Items = Array.Empty<MediaItem>();
            Groups = Array.Empty<MediaGroup>();
            Collections = Array.Empty<CollectionCard>();
            Stats = new LibraryStats();
            return;
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

        var revision = BuildLibraryRevision(sourceSnapshot, catalogueFreshness);
        if (string.Equals(CollectionTab, "albums", StringComparison.OrdinalIgnoreCase) && !AlbumId.HasValue)
        {
            try
            {
                var albumRevision = await _mediaDb.Albums
                    .AsNoTracking()
                    .GroupBy(_ => 1)
                    .Select(group => new { Count = group.Count(), UpdatedAt = group.Max(album => album.UpdatedAtUtc) })
                    .SingleOrDefaultAsync(cancellationToken);
                if (albumRevision is not null)
                {
                    revision = string.Concat(
                        revision,
                        ":albums:",
                        albumRevision.Count.ToString(CultureInfo.InvariantCulture),
                        ":",
                        albumRevision.UpdatedAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
            {
                _logger.LogDebug(exception, "Albums revision could not be included in Photos polling.");
            }
        }

        if (AlbumId.HasValue && AlbumId.Value != Guid.Empty)
        {
            try
            {
                var albumUpdatedAt = await _mediaDb.Albums
                    .AsNoTracking()
                    .Where(album => album.Id == AlbumId.Value)
                    .Select(album => (DateTimeOffset?)album.UpdatedAtUtc)
                    .SingleOrDefaultAsync(cancellationToken);
                if (albumUpdatedAt.HasValue)
                {
                    revision = string.Concat(
                        revision,
                        ":album:",
                        AlbumId.Value.ToString("N"),
                        ":",
                        albumUpdatedAt.Value.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
            {
                _logger.LogDebug(exception, "Album revision could not be included in Photos polling.");
            }
        }

        return new JsonResult(new
        {
            revision,
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
            catalogueFreshness.CatalogueFingerprint ?? "none");

    private void NormalizeRequest()
    {
        View = View?.Trim().ToLowerInvariant() switch
        {
            "collections" => "collections",
            "album" => "album",
            _ => "photos"
        };
        CollectionTab = string.Equals(CollectionTab?.Trim(), "albums", StringComparison.OrdinalIgnoreCase)
            ? "albums"
            : "source";
        Sort = string.Equals(Sort?.Trim(), "oldest", StringComparison.OrdinalIgnoreCase) ? "oldest" : "newest";
        Collection = string.IsNullOrWhiteSpace(Collection) ? null : Collection.Trim();
        if (Collection is { Length: > 300 }) Collection = Collection[..300];
        if (AlbumId == Guid.Empty) AlbumId = null;
        if (AddToAlbumId == Guid.Empty) AddToAlbumId = null;

        if (AlbumId.HasValue)
        {
            View = "album";
            Collection = null;
            CollectionTab = "albums";
            // Album membership defines the media set. Mixing source/project/person filters
            // into a curated album would make its contents appear to change unexpectedly.
            Source = "all";
            Kind = "all";
            Classification = "all";
            ProjectId = null;
            Year = null;
            PersonId = null;
            PersonIds = Array.Empty<Guid>();
            PeopleMatch = "all";
            Q = null;
            AddToAlbumId = null;
        }
        else if (View == "album")
        {
            View = "collections";
            CollectionTab = "albums";
        }

        if (Collection is not null)
        {
            View = "photos";
            AlbumId = null;
            AddToAlbumId = null;
            CollectionTab = "source";
        }

        if (AddToAlbumId.HasValue)
        {
            // Target-album curation is a focused Photos workflow. Album membership is
            // applied only after explicit media selection; source records remain unchanged.
            View = "photos";
            AlbumId = null;
            Collection = null;
            CollectionTab = "source";
            IncludeArchivedAlbums = false;
            OrganizeAlbum = false;
        }

        if (View != "collections")
        {
            IncludeArchivedAlbums = false;
        }
        if (!IsAlbumDetail)
        {
            OrganizeAlbum = false;
        }

        PageNumber = Math.Max(1, PageNumber);
        Source = NormalizeSource(Source);
        if (!ExternalSourcesEnabled && Source == "external")
        {
            Source = "all";
        }

        Kind = NormalizeKind(Kind);
        Classification = NormalizeClassification(Classification);
        Q = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();
        PeopleMatch = string.Equals(PeopleMatch?.Trim(), "any", StringComparison.OrdinalIgnoreCase)
            ? "any"
            : "all";

        if (!PeopleFeatureEnabled || IsAlbumDetail)
        {
            PersonId = null;
            PersonIds = Array.Empty<Guid>();
            return;
        }

        var selected = new List<Guid>(capacity: 10);
        if (PersonId.HasValue && PersonId.Value != Guid.Empty)
        {
            selected.Add(PersonId.Value);
        }

        foreach (var personId in PersonIds ?? Array.Empty<Guid>())
        {
            if (personId == Guid.Empty || selected.Contains(personId))
            {
                continue;
            }

            selected.Add(personId);
            if (selected.Count == 10)
            {
                break;
            }
        }

        PersonIds = selected.ToArray();
        PersonId = null;
        if (PersonIds.Length < 2)
        {
            PeopleMatch = "all";
        }
    }

    private async Task LoadSelectedPeopleAsync(CancellationToken cancellationToken)
    {
        if (!PeopleFeatureEnabled || PersonIds.Length == 0)
        {
            SelectedPeople = Array.Empty<PersonOption>();
            return;
        }

        try
        {
            var selectedIds = PersonIds;
            var rows = await _mediaDb.Persons
                .AsNoTracking()
                .Where(person => selectedIds.Contains(person.Id)
                                 && person.Status == MediaPersonStatus.Confirmed
                                 && !person.IsHidden)
                .Select(person => new PersonOption(
                    person.Id,
                    person.DisplayName,
                    person.FaceAssignments
                        .Where(assignment => assignment.RemovedAtUtc == null
                                             && !assignment.MediaFace.IsSuppressed
                                             && assignment.MediaFace.MediaAsset.IsAvailable
                                             && !assignment.MediaFace.MediaAsset.IsDeleted
                                             && !assignment.MediaFace.MediaAsset.IsArchived)
                        .Select(assignment => assignment.MediaFace.MediaAssetId)
                        .Distinct()
                        .Count(),
                    person.RepresentativeFaceId))
                .ToListAsync(cancellationToken);

            var rowsById = rows.ToDictionary(person => person.Id);
            SelectedPeople = selectedIds
                .Where(rowsById.ContainsKey)
                .Select(personId => rowsById[personId])
                .ToList();
            PersonIds = SelectedPeople.Select(person => person.Id).ToArray();
            if (PersonIds.Length < 2)
            {
                PeopleMatch = "all";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            _logger.LogWarning(exception, "Unable to load the selected people gallery header.");
            SelectedPeople = Array.Empty<PersonOption>();
        }
    }

    private void ApplyCatalogueResult(MediaLibraryQueryResult result, bool includeTimeline)
    {
        IsUsingCatalogue = true;
        if (includeTimeline)
        {
            PageNumber = result.PageNumber;
            HasPreviousPage = result.HasPreviousPage;
            HasNextPage = result.HasNextPage;
        }
        ExternalLibraryAvailable = true;
        ExternalLibraryWarning = result.Warning;
        Projects = result.Projects.Select(project => new ProjectOption(project.Id, project.Name)).ToList();
        People = result.People
            .Select(person => new PersonOption(
                person.Id,
                person.Name,
                person.PhotoCount,
                person.RepresentativeFaceId))
            .ToList();

        var peopleById = People.ToDictionary(person => person.Id);
        var selectedById = SelectedPeople.ToDictionary(person => person.Id);
        SelectedPeople = PersonIds
            .Select(personId => peopleById.GetValueOrDefault(personId)
                                ?? selectedById.GetValueOrDefault(personId))
            .Where(person => person is not null)
            .Select(person => person!)
            .ToList();
        PersonIds = SelectedPeople.Select(person => person.Id).ToArray();
        if (PersonIds.Length < 2)
        {
            PeopleMatch = "all";
        }

        Years = result.Years;
        Stats = new LibraryStats
        {
            Total = result.Statistics.Total,
            Photos = result.Statistics.Photos,
            Videos = result.Statistics.Videos,
            Collections = result.Statistics.Collections
        };
        if (includeTimeline)
        {
            Items = result.Items.Select(MapCatalogueItem).ToList();
            Collections = Array.Empty<CollectionCard>();
            BuildGroups();
        }
        else
        {
            Items = Array.Empty<MediaItem>();
            Groups = Array.Empty<MediaGroup>();
        }
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

        var displayMetadata = MediaDisplayMetadataFormatter.Format(
            row.Origin,
            row.Title,
            row.Caption,
            row.EditorialCaption,
            row.ContextTitle,
            row.ContextSubtitle);

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
            ContextTitle = MediaCollectionTitleFormatter.FormatCollectionTitle(row.Origin, row.ContextTitle),
            ContextSubtitle = row.ContextSubtitle,
            OriginalTitle = row.Title,
            Title = displayMetadata.DisplayTitle,
            DisplayContext = displayMetadata.DisplayContext,
            DisplaySubtitle = displayMetadata.DisplaySubtitle,
            Caption = displayMetadata.EffectiveCaption,
            EditorialCaption = row.EditorialCaption,
            EditorialConcurrencyToken = row.EditorialConcurrencyToken,
            OriginalFileName = row.OriginalFileName,
            FileSizeBytes = row.FileSizeBytes,
            Albums = row.Albums.Select(album => new AlbumSummary(album.Id, album.Name)).ToList(),
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

    private async Task LoadCatalogueCollectionsAsync(CancellationToken cancellationToken)
    {
        var result = await _collections.SearchAsync(
            new MediaCollectionQuery(
                Q,
                Source,
                Kind,
                Classification,
                ProjectId,
                Year,
                PageNumber,
                CollectionPageSize,
                PeopleFeatureEnabled,
                PeopleFeatureEnabled ? PersonIds : Array.Empty<Guid>(),
                PeopleMatch,
                IncludeSingletonCollections,
                Sort),
            cancellationToken);

        PageNumber = result.PageNumber;
        HasPreviousPage = result.HasPreviousPage;
        HasNextPage = result.HasNextPage;
        Collections = result.Collections.Select(MapCollection).ToList();
        Stats = new LibraryStats
        {
            Total = result.TotalItems,
            Photos = result.TotalPhotos,
            Videos = result.TotalVideos,
            Collections = result.TotalCollections
        };
    }

    private CollectionCard MapCollection(MediaCollectionSummary row)
    {
        var coverUrl = row.CoverAssetId.HasValue
            ? Url.Page("/Photos/Media", new { id = row.CoverAssetId.Value, variant = "thumb" })
            : null;

        return new CollectionCard(
            row.CollectionKey,
            row.ContextTitle,
            row.ContextSubtitle,
            CollectionTypeLabel(row.Origin),
            row.ItemCount,
            row.PhotoCount,
            row.VideoCount,
            row.LatestMediaDateUtc.ToLocalTime().DateTime,
            coverUrl,
            BuildSourceRecordUrl(row.Origin, row.ContextKey));
    }

    private LibraryStats BuildFallbackCollections(IReadOnlyList<MediaItem> filtered)
    {
        var query = filtered
            .GroupBy(item => item.CollectionKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var rows = group.ToList();
                var latest = rows.Max(item => item.MediaDate);
                var representative = rows
                    .OrderByDescending(item => item.MediaDate)
                    .ThenBy(item => item.SortOrder)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .First();
                var cover = rows
                    .Where(item => item.Kind == MediaKind.Photo)
                    .OrderByDescending(item => item.IsCover)
                    .ThenByDescending(item => item.MediaDate)
                    .ThenBy(item => item.SortOrder)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                var isProjectOnly = rows.All(item => item.Source == MediaSource.Project);

                return new
                {
                    Key = group.Key,
                    Rows = rows,
                    Representative = representative,
                    Cover = cover,
                    Latest = latest,
                    IsProjectOnly = isProjectOnly
                };
            })
            .Where(group => IncludeSingletonCollections || group.Rows.Count > 1 || !group.IsProjectOnly);

        var ordered = Sort == "oldest"
            ? query.OrderBy(group => group.Latest).ThenBy(group => group.Representative.ContextTitle, StringComparer.OrdinalIgnoreCase)
            : query.OrderByDescending(group => group.Latest).ThenBy(group => group.Representative.ContextTitle, StringComparer.OrdinalIgnoreCase);

        var allCollections = ordered.ToList();
        var totalCollections = allCollections.Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCollections / (double)CollectionPageSize));
        PageNumber = Math.Clamp(PageNumber, 1, pageCount);
        var skip = (PageNumber - 1) * CollectionPageSize;
        HasPreviousPage = PageNumber > 1;
        HasNextPage = totalCollections > skip + CollectionPageSize;

        Collections = allCollections
            .Skip(skip)
            .Take(CollectionPageSize)
            .Select(group => new CollectionCard(
                group.Key,
                group.Representative.ContextTitle,
                group.Representative.ContextSubtitle,
                CollectionTypeLabel(group.Representative.Source),
                group.Rows.Count,
                group.Rows.Count(item => item.Kind == MediaKind.Photo),
                group.Rows.Count(item => item.Kind == MediaKind.Video),
                group.Latest,
                group.Cover?.ThumbnailUrl,
                group.Representative.SourceUrl))
            .ToList();

        return new LibraryStats
        {
            Total = allCollections.Sum(group => group.Rows.Count),
            Photos = allCollections.Sum(group => group.Rows.Count(item => item.Kind == MediaKind.Photo)),
            Videos = allCollections.Sum(group => group.Rows.Count(item => item.Kind == MediaKind.Video)),
            Collections = totalCollections
        };
    }

    private string? BuildSourceRecordUrl(MediaAssetOrigin origin, string contextKey)
    {
        var entityId = ExtractParentId(contextKey);
        return origin switch
        {
            MediaAssetOrigin.ProjectPhoto or MediaAssetOrigin.ProjectVideo
                => int.TryParse(entityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var projectId)
                    ? Url.Page("/Projects/Photos/Index", new { id = projectId })
                    : null,
            MediaAssetOrigin.VisitPhoto
                => Guid.TryParse(entityId, out var visitId)
                    ? Url.Page("/Visits/Details", new { area = "ProjectOfficeReports", id = visitId })
                    : null,
            MediaAssetOrigin.SocialMediaEventPhoto
                => Guid.TryParse(entityId, out var eventId)
                    ? Url.Page("/SocialMedia/Details", new { area = "ProjectOfficeReports", id = eventId })
                    : null,
            MediaAssetOrigin.ActivityPhoto
                => int.TryParse(entityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var activityId)
                    ? Url.Page("/Activities/Details", new { id = activityId })
                    : null,
            _ => null
        };
    }

    private static string CollectionTypeLabel(MediaAssetOrigin origin)
        => origin switch
        {
            MediaAssetOrigin.VisitPhoto => "Visit",
            MediaAssetOrigin.SocialMediaEventPhoto => "Event",
            MediaAssetOrigin.ActivityPhoto => "Activity",
            MediaAssetOrigin.ProjectPhoto or MediaAssetOrigin.ProjectVideo => "Project",
            _ => "External"
        };

    private static string CollectionTypeLabel(MediaSource source)
        => source switch
        {
            MediaSource.Visit => "Visit",
            MediaSource.Event => "Event",
            MediaSource.Activity => "Activity",
            MediaSource.Project => "Project",
            _ => "External"
        };

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
        // unavailable. A changed version is allowed through because it represents a new
        // upload or replacement that the catalogue has not processed yet.
        if (prismSourceId.HasValue && items.Count > 0)
        {
            try
            {
                var sourceStates = await _mediaDb.Assets
                    .AsNoTracking()
                    .Where(asset => asset.SourceId == prismSourceId.Value && !asset.IsDeleted)
                    .Select(asset => new CatalogueAssetState(
                        asset.SourceEntityId,
                        asset.VersionToken,
                        asset.IsAvailable
                        && asset.AvailabilityStatus == MediaAvailabilityStatus.Available
                        && !asset.IsArchived))
                    .ToDictionaryAsync(state => state.SourceEntityId, StringComparer.Ordinal, cancellationToken);

                items = items
                    .Where(item => !sourceStates.TryGetValue(item.Id, out var state)
                                   || state.IsAvailable
                                   || (state.VersionToken is not null
                                       && !string.Equals(state.VersionToken, item.VersionToken, StringComparison.Ordinal)))
                    .ToList();
            }
            catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
            {
                _logger.LogWarning(ex, "Unable to read catalogue availability while rendering live PRISM media.");
            }
        }

        var filteredQuery = items
            .Where(MatchesSearch)
            .Where(MatchesClassification)
            .Where(item => !Year.HasValue || item.MediaDate.Year == Year.Value)
            .Where(item => Collection is null || string.Equals(item.CollectionKey, Collection, StringComparison.Ordinal));

        var filtered = (Sort == "oldest"
                ? filteredQuery.OrderBy(item => item.MediaDate)
                    .ThenBy(item => item.ContextTitle, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.SortOrder)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                : filteredQuery.OrderByDescending(item => item.MediaDate)
                    .ThenBy(item => item.ContextTitle, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.SortOrder)
                    .ThenBy(item => item.Id, StringComparer.Ordinal))
            .ToList();

        var total = filtered.Count;
        var visibleCollectionCount = filtered.Select(item => item.CollectionKey).Distinct(StringComparer.Ordinal).Count();
        LibraryStats? fallbackCollectionStats = null;
        if (View == "collections" && Collection is null)
        {
            fallbackCollectionStats = BuildFallbackCollections(filtered);
            visibleCollectionCount = fallbackCollectionStats.Collections;
            Items = Array.Empty<MediaItem>();
            Groups = Array.Empty<MediaGroup>();
        }
        else
        {
            var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            PageNumber = Math.Clamp(PageNumber, 1, pageCount);
            var skip = (PageNumber - 1) * PageSize;
            HasPreviousPage = PageNumber > 1;
            HasNextPage = total > skip + PageSize;
            Items = filtered.Skip(skip).Take(PageSize).ToList();
            Collections = Array.Empty<CollectionCard>();
        }

        Years = items.Where(MatchesSearch).Where(MatchesClassification)
            .Select(item => item.MediaDate.Year).Distinct().OrderByDescending(year => year).ToList();
        Projects = await _db.Projects.AsNoTracking()
            .Where(project => !project.IsDeleted)
            .Where(project => _db.ProjectPhotos.Any(photo => photo.ProjectId == project.Id)
                              || _db.ProjectVideos.Any(video => video.ProjectId == project.Id))
            .OrderBy(project => project.Name)
            .Select(project => new ProjectOption(project.Id, project.Name))
            .ToListAsync(cancellationToken);
        Stats = fallbackCollectionStats ?? new LibraryStats
        {
            Total = total,
            Photos = filtered.Count(item => item.Kind == MediaKind.Photo),
            Videos = filtered.Count(item => item.Kind == MediaKind.Video),
            Collections = visibleCollectionCount
        };
        if (View != "collections" || Collection is not null)
        {
            BuildGroups();
        }
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
            .OrderBy(group => Sort == "oldest" ? group.Date.Ticks : -group.Date.Ticks)
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
                ContextKey = $"visit:{row.VisitId}", CollectionKey = $"visit:{row.VisitId}", ContextTitle = MediaCollectionTitleFormatter.FormatVisitTitle(row.VisitorName),
                ContextSubtitle = string.IsNullOrWhiteSpace(row.VisitType) ? "Visit to SDD" : row.VisitType,
                Title = string.IsNullOrWhiteSpace(row.Caption) ? MediaCollectionTitleFormatter.FormatVisitTitle(row.VisitorName) : row.Caption,
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

    public string BuildMediaTypeUrl(string kind)
        => BuildPhotosUrl(PersonIds, kind: kind, pageNumber: 1);

    public string BuildPageUrl(int pageNumber)
    {
        if (IsAlbumsWorkspace)
        {
            return Url.Page("/Photos/Index", new
            {
                View = "collections",
                CollectionTab = "albums",
                Q,
                Sort = Sort == "newest" ? null : Sort,
                IncludeArchivedAlbums = IncludeArchivedAlbums ? true : (bool?)null,
                PageNumber = pageNumber > 1 ? pageNumber : (int?)null
            }) ?? "/Photos?View=collections&CollectionTab=albums";
        }

        return BuildPhotosUrl(PersonIds, pageNumber: pageNumber);
    }

    public string BuildPeopleMatchUrl(string matchMode)
        => BuildPhotosUrl(PersonIds, peopleMatch: matchMode);

    public string BuildRemovePersonUrl(Guid personId)
        => BuildPhotosUrl(PersonIds.Where(id => id != personId));

    public string BuildSinglePersonUrl(Guid personId)
        => BuildPhotosUrl(new[] { personId }, view: "photos", peopleMatch: "all");

    public string BuildClearPeopleUrl()
        => BuildPhotosUrl(Array.Empty<Guid>());

    public string BuildPeoplePickerUrl()
    {
        var values = new RouteValueDictionary
        {
            ["PeopleMatch"] = PersonIds.Length > 1 ? PeopleMatch : null
        };
        for (var index = 0; index < PersonIds.Length; index++)
        {
            values[$"SelectedIds[{index}]"] = PersonIds[index];
        }

        return Url.Page("/Photos/People/Index", values) ?? "/Photos/People";
    }

    private string BuildPhotosUrl(
        IEnumerable<Guid> personIds,
        string? kind = null,
        int? pageNumber = null,
        string? peopleMatch = null,
        string? view = null,
        string? collection = null,
        string? sort = null)
    {
        var targetView = view ?? View;
        var values = new RouteValueDictionary
        {
            ["View"] = targetView,
            ["Q"] = Q,
            ["Source"] = Source == "all" ? null : Source,
            ["Kind"] = kind ?? Kind,
            ["Classification"] = Classification == "all" ? null : Classification,
            ["ProjectId"] = ProjectId,
            ["Year"] = Year,
            ["Sort"] = (sort ?? Sort) == "newest" ? null : (sort ?? Sort),
            ["Collection"] = collection ?? Collection,
            ["CollectionTab"] = targetView == "collections" ? CollectionTab : null,
            ["IncludeSingletonCollections"] = targetView == "collections" && CollectionTab == "source" && IncludeSingletonCollections ? true : (bool?)null,
            ["IncludeArchivedAlbums"] = targetView == "collections" && CollectionTab == "albums" && IncludeArchivedAlbums ? true : (bool?)null,
            ["AddToAlbumId"] = targetView == "photos" && AddMediaTargetAlbum is not null ? AddMediaTargetAlbum.Id : (Guid?)null,
            ["PeopleMatch"] = peopleMatch ?? PeopleMatch,
            ["PageNumber"] = pageNumber is > 1 ? pageNumber : (int?)null
        };

        var index = 0;
        foreach (var personId in personIds.Distinct().Take(10))
        {
            values[$"PersonIds[{index++}]"] = personId;
        }

        if (index < 2)
        {
            values["PeopleMatch"] = null;
        }

        return Url.Page("/Photos/Index", values) ?? "/Photos";
    }

    public string BuildSortUrl(string sort)
    {
        if (IsAlbumsWorkspace)
        {
            return Url.Page("/Photos/Index", new
            {
                View = "collections",
                CollectionTab = "albums",
                Q,
                Sort = sort == "newest" ? null : sort,
                IncludeArchivedAlbums = IncludeArchivedAlbums ? true : (bool?)null
            }) ?? "/Photos?View=collections&CollectionTab=albums";
        }
        return BuildPhotosUrl(PersonIds, sort: sort, pageNumber: 1);
    }

    public string BuildCollectionUrl(string collectionKey)
        => BuildPhotosUrl(PersonIds, view: "photos", collection: collectionKey, pageNumber: 1);

    public string BuildCollectionsUrl()
        => Url.Page("/Photos/Index", new { View = "collections", CollectionTab = "source" }) ?? "/Photos?View=collections";

    public string BuildAlbumsUrl(bool? includeArchived = null)
        => Url.Page("/Photos/Index", new
        {
            View = "collections",
            CollectionTab = "albums",
            IncludeArchivedAlbums = (includeArchived ?? IncludeArchivedAlbums) ? true : (bool?)null
        }) ?? "/Photos?View=collections&CollectionTab=albums";

    public string BuildAlbumUrl(Guid albumId, bool organize = false)
        => Url.Page("/Photos/Index", new
        {
            View = "album",
            AlbumId = albumId,
            OrganizeAlbum = organize ? true : (bool?)null
        }) ?? $"/Photos?View=album&AlbumId={albumId:D}";

    public string BuildAddMediaToAlbumUrl(Guid albumId)
        => Url.Page("/Photos/Index", new
        {
            View = "photos",
            AddToAlbumId = albumId
        }) ?? $"/Photos?View=photos&AddToAlbumId={albumId:D}";

    public string BuildClearCollectionUrl()
        => BuildPhotosUrl(PersonIds, view: "photos", collection: string.Empty, pageNumber: 1);

    public string BuildClearFiltersUrl()
        => Url.Page("/Photos/Index", new
        {
            View,
            CollectionTab = View == "collections" ? CollectionTab : null,
            Sort = Sort == "newest" ? null : Sort,
            AddToAlbumId = IsAddMediaMode && AddMediaTargetAlbum is not null
                ? AddMediaTargetAlbum.Id
                : (Guid?)null
        }) ?? "/Photos";

    public string CurrentReturnUrl
        => $"{Request.PathBase}{Request.Path}{Request.QueryString}";

    public string SerializeViewerAlbums(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return JsonSerializer.Serialize(item.Albums.Select(album => new
        {
            name = album.Name,
            url = BuildAlbumUrl(album.Id)
        }));
    }

    public static string FormatFileSize(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value < 0) return string.Empty;
        var value = (double)bytes.Value;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }
        var format = unit == 0 || value >= 100d ? "0" : value >= 10d ? "0.0" : "0.00";
        return $"{value.ToString(format, CultureInfo.InvariantCulture)} {units[unit]}";
    }

    public static string? DisplayContext(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.DisplayContext
               ?? MediaDisplayMetadataFormatter.DistinctOrNull(item.ContextTitle, item.Title);
    }

    public string? CurrentCollectionTitle
        => IsCollectionDetail
            ? Groups.FirstOrDefault()?.Title ?? Items.FirstOrDefault()?.ContextTitle
            : null;

    public string? SelectedProjectName
        => ProjectId.HasValue
            ? Projects.FirstOrDefault(project => project.Id == ProjectId.Value)?.Name
            : null;

    public string SerializeViewerPeople(MediaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return JsonSerializer.Serialize(item.People.Select(person => new
        {
            name = person.Name,
            url = Url.Page("/Photos/Index", new { PersonIds = person.Id, View = "photos" }) ?? string.Empty
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
        string? VersionToken,
        bool IsAvailable);

    private sealed record PrismCatalogueFreshness(
        Guid? SourceId,
        bool IsFresh,
        string? CatalogueFingerprint,
        string? ScanStatus,
        DateTimeOffset? LastSuccessfulScanAtUtc,
        long IndexedAssetCount)
    {
        public static PrismCatalogueFreshness Unavailable { get; } = new(null, false, null, null, null, 0);
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
        public string? OriginalTitle { get; init; }
        public required string Title { get; init; }
        public string? DisplayContext { get; init; }
        public string? DisplaySubtitle { get; init; }
        public string? Caption { get; init; }
        public string? EditorialCaption { get; init; }
        public Guid? EditorialConcurrencyToken { get; init; }
        public string? OriginalFileName { get; init; }
        public long? FileSizeBytes { get; init; }
        public IReadOnlyList<AlbumSummary> Albums { get; init; } = Array.Empty<AlbumSummary>();
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
        public bool IsAlbumCover { get; init; }
        public long SortOrder { get; init; }
        public string? VersionToken { get; init; }
        public double AspectRatio => Width.GetValueOrDefault() > 0 && Height.GetValueOrDefault() > 0
            ? Math.Clamp((double)Width!.Value / Height!.Value, .55d, 2.2d)
            : Kind == MediaKind.Video ? 16d / 9d : 1.35d;
    }

    public sealed record MediaGroup(string Key, string Title, string Subtitle, DateTime Date, IReadOnlyList<MediaItem> Items);
    public sealed record AlbumSummary(Guid Id, string Name);
    public sealed record AlbumCard(
        Guid Id,
        string Name,
        string? Description,
        int ItemCount,
        int PhotoCount,
        int VideoCount,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        long? CoverAssetId,
        string? CoverThumbnailUrl,
        bool IsArchived,
        bool CanManage,
        bool IsOwner,
        string CreatorDisplayName);

    public sealed record CollectionCard(
        string CollectionKey,
        string Title,
        string Subtitle,
        string TypeLabel,
        int ItemCount,
        int PhotoCount,
        int VideoCount,
        DateTime LatestDate,
        string? CoverThumbnailUrl,
        string? SourceUrl);
    public sealed record ProjectOption(int Id, string Name);
    public sealed record PersonOption(Guid Id, string Name, int PhotoCount, Guid? RepresentativeFaceId);
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
