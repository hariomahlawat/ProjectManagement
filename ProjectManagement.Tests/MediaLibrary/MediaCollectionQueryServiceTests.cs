using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaCollectionQueryServiceTests
{
    [Fact]
    public async Task SuppressedSingletonProjectCollections_DoNotInflateDisplayedCollectionTotals()
    {
        await using var db = CreateContext();
        var source = CreateSource();
        db.Sources.Add(source);
        db.Assets.AddRange(
            CreateAsset(1, source, MediaAssetOrigin.ProjectPhoto, "project:1", "Project singleton", DateTimeOffset.UtcNow.AddMinutes(-3)),
            CreateAsset(2, source, MediaAssetOrigin.VisitPhoto, "visit:1", "Visit of Test", DateTimeOffset.UtcNow.AddMinutes(-2)),
            CreateAsset(3, source, MediaAssetOrigin.VisitPhoto, "visit:1", "Visit of Test", DateTimeOffset.UtcNow.AddMinutes(-1)));
        await db.SaveChangesAsync();

        var service = new MediaCollectionQueryService(db, CreateVisibilityPolicy());
        var result = await service.SearchAsync(
            new MediaCollectionQuery(
                Query: null,
                Source: "all",
                Kind: "all",
                Classification: "all",
                ProjectId: null,
                Year: null,
                PageNumber: 1,
                PageSize: 48,
                IncludePeople: false,
                IncludeSingletons: false),
            CancellationToken.None);

        var collection = Assert.Single(result.Collections);
        Assert.Equal("visit:1", collection.CollectionKey);
        Assert.Equal(1, result.TotalCollections);
        Assert.Equal(2, result.TotalItems);
        Assert.Equal(2, result.TotalPhotos);
        Assert.Equal(0, result.TotalVideos);
    }

    [Fact]
    public async Task IncludingSingletons_RecalculatesCollectionTotalsForTheVisibleCollectionSet()
    {
        await using var db = CreateContext();
        var source = CreateSource();
        db.Sources.Add(source);
        db.Assets.AddRange(
            CreateAsset(1, source, MediaAssetOrigin.ProjectPhoto, "project:1", "Project singleton", DateTimeOffset.UtcNow.AddMinutes(-2)),
            CreateAsset(2, source, MediaAssetOrigin.VisitPhoto, "visit:1", "Visit of Test", DateTimeOffset.UtcNow.AddMinutes(-1)));
        await db.SaveChangesAsync();

        var service = new MediaCollectionQueryService(db, CreateVisibilityPolicy());
        var result = await service.SearchAsync(
            new MediaCollectionQuery(
                Query: null,
                Source: "all",
                Kind: "all",
                Classification: "all",
                ProjectId: null,
                Year: null,
                PageNumber: 1,
                PageSize: 48,
                IncludePeople: false,
                IncludeSingletons: true),
            CancellationToken.None);

        Assert.Equal(2, result.TotalCollections);
        Assert.Equal(2, result.TotalItems);
        Assert.Equal(2, result.TotalPhotos);
    }

    private static MediaLibraryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"media-collections-{Guid.NewGuid():N}")
            .Options;
        return new MediaLibraryDbContext(options);
    }

    private static IMediaAssetVisibilityPolicy CreateVisibilityPolicy()
    {
        var options = new MediaLibraryOptions
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true },
            ExternalSources = new ExternalMediaSourcesOptions { Enabled = true }
        };
        return new MediaAssetVisibilityPolicy(Options.Create(options));
    }

    private static MediaLibrarySource CreateSource()
        => new()
        {
            Id = Guid.NewGuid(),
            Key = "prism-test",
            Name = "PRISM test source",
            SourceType = MediaLibrarySourceType.Prism,
            IsEnabled = true,
            IsVisibleInLibrary = true
        };

    private static MediaAsset CreateAsset(
        long id,
        MediaLibrarySource source,
        MediaAssetOrigin origin,
        string collectionKey,
        string contextTitle,
        DateTimeOffset mediaDate)
        => new()
        {
            Id = id,
            SourceId = source.Id,
            Source = source,
            Origin = origin,
            Kind = MediaAssetKind.Photo,
            IsAvailable = true,
            AvailabilityStatus = MediaAvailabilityStatus.Available,
            SourceEntityId = $"asset:{id}",
            ContextKey = collectionKey,
            CollectionKey = collectionKey,
            ContextTitle = contextTitle,
            ContextSubtitle = origin == MediaAssetOrigin.VisitPhoto ? "Civil Dignitaries" : "Project media",
            SourceLabel = origin == MediaAssetOrigin.VisitPhoto ? "Visit" : "Project",
            Title = $"Photo {id}",
            OriginalFileName = $"photo-{id}.jpg",
            ContentType = "image/jpeg",
            MediaDateUtc = mediaDate,
            IndexedAtUtc = mediaDate,
            LastSeenAtUtc = mediaDate
        };
}
