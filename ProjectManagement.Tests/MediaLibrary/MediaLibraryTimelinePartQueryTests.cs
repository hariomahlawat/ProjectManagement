using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// A part of the Photos timeline loaded while scrolling asks for the rows alone
/// (<see cref="MediaLibraryQuery.IncludeFilterFacets"/> off). The rows, what each tile shows
/// and the paging must be the same as on the full page; only the page's lists and figures go.
/// </summary>
public sealed class MediaLibraryTimelinePartQueryTests
{
    [Fact]
    public async Task FullPage_StillLoadsFiguresYearsProjectsAndPeople()
    {
        await using var db = await CreateSeededContextAsync();
        var diagnostics = new RecordingDiagnostics();

        var result = await CreateService(db, diagnostics).SearchAsync(Query(pageNumber: 1), CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Contains(MediaLibraryQueryOperation.Statistics, diagnostics.Operations);
        Assert.Contains(MediaLibraryQueryOperation.Years, diagnostics.Operations);
        Assert.Contains(MediaLibraryQueryOperation.Projects, diagnostics.Operations);
        Assert.Contains(MediaLibraryQueryOperation.People, diagnostics.Operations);
        Assert.Equal(new[] { 2025, 2024 }, result.Years);
        Assert.Single(result.Projects);
        Assert.Single(result.People);
        Assert.Equal(5, result.Statistics.Total);
        Assert.Equal(2, result.Statistics.Collections);
    }

    [Fact]
    public async Task TimelinePart_RunsOnlyTheRowsAndTheCatalogueCheck()
    {
        await using var db = await CreateSeededContextAsync();
        var diagnostics = new RecordingDiagnostics();

        var result = await CreateService(db, diagnostics).SearchAsync(
            Query(pageNumber: 2) with { IncludeFilterFacets = false },
            CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Equal(
            new[] { MediaLibraryQueryOperation.PrimaryTimeline, MediaLibraryQueryOperation.PrismSourceStatus },
            diagnostics.Operations);
        Assert.Empty(result.Years);
        Assert.Empty(result.Projects);
        Assert.Empty(result.People);
        Assert.Empty(result.Months);
        Assert.Empty(result.Warnings);
        // The page still decides between the catalogue and PRISM media from this.
        Assert.True(result.HasPrismCatalogue);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TimelinePart_ReturnsTheSameRowsTileDetailsAndPagingAsTheFullPage(int pageNumber)
    {
        await using var db = await CreateSeededContextAsync();

        var full = await CreateService(db, new RecordingDiagnostics())
            .SearchAsync(Query(pageNumber), CancellationToken.None);
        var part = await CreateService(db, new RecordingDiagnostics())
            .SearchAsync(Query(pageNumber) with { IncludeFilterFacets = false }, CancellationToken.None);

        Assert.NotEmpty(full.Items);
        Assert.Equal(full.Items.Select(item => item.Id), part.Items.Select(item => item.Id));
        Assert.Equal(full.PageNumber, part.PageNumber);
        Assert.Equal(full.PageSize, part.PageSize);
        Assert.Equal(full.HasPreviousPage, part.HasPreviousPage);
        Assert.Equal(full.HasNextPage, part.HasNextPage);
        Assert.Equal(full.HasPrismCatalogue, part.HasPrismCatalogue);
        foreach (var (expected, actual) in full.Items.Zip(part.Items))
        {
            Assert.Equal(expected.People, actual.People);
            Assert.Equal(expected.Albums, actual.Albums);
            Assert.Equal(expected.UnidentifiedFaceCount, actual.UnidentifiedFaceCount);
            Assert.Equal(expected.Title, actual.Title);
            Assert.Equal(expected.MediaDateUtc, actual.MediaDateUtc);
        }
    }

    [Fact]
    public async Task TimelinePart_KeepsTheVisibilityRules()
    {
        await using var db = await CreateSeededContextAsync();

        var part = await CreateService(db, new RecordingDiagnostics()).SearchAsync(
            Query(pageNumber: 1) with { PageSize = 50, IncludeFilterFacets = false },
            CancellationToken.None);

        // Asset 6 is archived and asset 7 is a folder picture whose source is hidden from the library.
        Assert.Equal(new long[] { 5, 4, 3, 2, 1 }, part.Items.Select(item => item.Id));
        // The tiles still carry the people and albums the viewer shows.
        var newest = part.Items[0];
        Assert.Equal("Asha Verma", Assert.Single(newest.People).DisplayName);
        Assert.Equal("Open day", Assert.Single(newest.Albums).Name);
        Assert.Equal(1, part.Items[1].UnidentifiedFaceCount);
    }

    private static MediaLibraryQuery Query(int pageNumber)
        => new(
            Query: null,
            Source: "all",
            Kind: "all",
            Classification: "all",
            ProjectId: null,
            PersonId: null,
            Year: null,
            PageNumber: pageNumber,
            PageSize: 2,
            IncludePeople: true,
            IncludeUnidentifiedFaces: true);

    private static MediaLibraryOptions CreateOptions()
        => new()
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true },
            ExternalSources = new ExternalMediaSourcesOptions { Enabled = true },
            People = new MediaPeopleOptions { Enabled = true }
        };

    private static MediaLibraryQueryService CreateService(MediaLibraryDbContext db, IMediaLibraryDiagnostics diagnostics)
    {
        var options = Options.Create(CreateOptions());
        return new MediaLibraryQueryService(
            db,
            options,
            diagnostics,
            new MediaAssetVisibilityPolicy(options),
            NullLogger<MediaLibraryQueryService>.Instance);
    }

    private static async Task<MediaLibraryDbContext> CreateSeededContextAsync()
    {
        var db = new MediaLibraryDbContext(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"media-timeline-part-{Guid.NewGuid():N}")
            .Options);
        var now = new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero);

        var prism = new MediaLibrarySource
        {
            Id = Guid.NewGuid(),
            Key = MediaSourceBootstrapper.PrismSourceKey,
            Name = "PRISM",
            SourceType = MediaLibrarySourceType.Prism,
            IsEnabled = true,
            IsVisibleInLibrary = true,
            ScanStatus = "Healthy",
            LastSuccessfulScanAtUtc = now
        };
        var hidden = new MediaLibrarySource
        {
            Id = Guid.NewGuid(),
            Key = "hidden-folder",
            Name = "Hidden folder",
            SourceType = MediaLibrarySourceType.FileSystem,
            IsEnabled = true,
            IsVisibleInLibrary = false
        };
        db.Sources.AddRange(prism, hidden);

        var assets = new[]
        {
            CreateAsset(1, prism, "project:1", now.AddYears(-1).AddDays(-2), projectId: 11),
            CreateAsset(2, prism, "project:1", now.AddYears(-1).AddDays(-1), projectId: 11),
            CreateAsset(3, prism, "visit:1", now.AddDays(-3)),
            CreateAsset(4, prism, "visit:1", now.AddDays(-2)),
            CreateAsset(5, prism, "visit:1", now.AddDays(-1)),
            CreateAsset(6, prism, "visit:1", now, isArchived: true),
            CreateAsset(7, hidden, "folder:2", now, origin: MediaAssetOrigin.ExternalFile)
        };
        db.Assets.AddRange(assets);

        var person = new MediaPerson
        {
            Id = Guid.NewGuid(),
            DisplayName = "Asha Verma",
            NormalizedName = "ASHA VERMA",
            Status = MediaPersonStatus.Confirmed,
            CreatedByUserId = "reviewer",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Persons.Add(person);
        var namedFace = CreateFace(assets[4], now);
        var unnamedFace = CreateFace(assets[3], now);
        db.Faces.AddRange(namedFace, unnamedFace);
        db.PersonFaces.Add(new MediaPersonFace
        {
            MediaPersonId = person.Id,
            MediaFaceId = namedFace.Id,
            AssignmentType = FaceAssignmentType.HumanConfirmed,
            ReferenceStatus = FaceReferenceStatus.NotReference,
            AssignedByUserId = "reviewer",
            AssignedAtUtc = now
        });

        var album = new MediaAlbum
        {
            Name = "Open day",
            CreatedByUserId = "curator",
            UpdatedByUserId = "curator",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Albums.Add(album);
        db.AlbumItems.Add(new MediaAlbumItem
        {
            MediaAlbumId = album.Id,
            MediaAssetId = assets[4].Id,
            SortOrder = 1,
            AddedByUserId = "curator",
            AddedAtUtc = now
        });

        await db.SaveChangesAsync();
        return db;
    }

    private static MediaFace CreateFace(MediaAsset asset, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            MediaAsset = asset,
            MediaAssetId = asset.Id,
            SequenceNumber = 1,
            Left = .2,
            Top = .2,
            Width = .3,
            Height = .3,
            DetectionConfidence = .95,
            QualityScore = .9,
            QualityStatus = FaceQualityStatus.EmbeddingEligible,
            DetectorModelKey = "detector",
            DetectorModelVersion = "v1",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private static MediaAsset CreateAsset(
        long id,
        MediaLibrarySource source,
        string collectionKey,
        DateTimeOffset mediaDate,
        int? projectId = null,
        bool isArchived = false,
        MediaAssetOrigin? origin = null)
        => new()
        {
            Id = id,
            SourceId = source.Id,
            Source = source,
            Origin = origin ?? (projectId.HasValue ? MediaAssetOrigin.ProjectPhoto : MediaAssetOrigin.VisitPhoto),
            Kind = MediaAssetKind.Photo,
            ProjectId = projectId,
            IsAvailable = true,
            IsArchived = isArchived,
            AvailabilityStatus = MediaAvailabilityStatus.Available,
            SourceEntityId = $"asset:{id}",
            ContextKey = collectionKey,
            CollectionKey = collectionKey,
            ContextTitle = projectId.HasValue ? "Project Alpha" : "Visit of Test",
            ContextSubtitle = projectId.HasValue ? "Project media" : "Civil Dignitaries",
            SourceLabel = projectId.HasValue ? "Project" : "Visit",
            Title = $"Photo {id}",
            OriginalFileName = $"photo-{id}.jpg",
            ContentType = "image/jpeg",
            MediaDateUtc = mediaDate,
            IndexedAtUtc = mediaDate,
            LastSeenAtUtc = mediaDate
        };

    private sealed class RecordingDiagnostics : IMediaLibraryDiagnostics
    {
        private readonly List<MediaLibraryDiagnosticEvent> _events = new();

        public IReadOnlyList<MediaLibraryQueryOperation> Operations
            => _events.Select(item => item.Operation).ToList();

        public MediaLibraryDiagnosticEvent RecordSuccess(MediaLibraryQueryOperation operation, long durationMilliseconds)
            => Record(operation, true, "Healthy", durationMilliseconds);

        public MediaLibraryDiagnosticEvent RecordFailure(
            MediaLibraryQueryOperation operation,
            Exception exception,
            long durationMilliseconds)
            => Record(operation, false, exception.Message, durationMilliseconds);

        public IReadOnlyList<MediaLibraryDiagnosticEvent> GetLatest() => _events;

        private MediaLibraryDiagnosticEvent Record(
            MediaLibraryQueryOperation operation,
            bool succeeded,
            string message,
            long durationMilliseconds)
        {
            var value = new MediaLibraryDiagnosticEvent(
                operation,
                succeeded,
                $"TEST-{_events.Count + 1}",
                message,
                DateTimeOffset.UtcNow,
                durationMilliseconds);
            _events.Add(value);
            return value;
        }
    }
}
