using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// The project list that comes with a search. Collage Studio's "From PRISM Photos" asks for photos
/// and <see cref="MediaLibraryQuery.ProjectsOfKindOnly"/>, so a project with only videos is not
/// offered. The PRISM Photos page does not ask for it and keeps every project with media.
/// </summary>
public sealed class MediaLibraryProjectFilterTests
{
    [Fact]
    public async Task Photos_only_with_projects_of_kind_lists_only_projects_that_have_photos()
    {
        await using var db = await SeedAsync();

        var result = await CreateService(db).SearchAsync(Query("photo") with { ProjectsOfKindOnly = true }, CancellationToken.None);

        Assert.Equal(new[] { 11 }, result.Projects.Select(p => p.Id));
    }

    [Fact]
    public async Task The_photos_page_keeps_every_project_with_media()
    {
        await using var db = await SeedAsync();

        var photos = await CreateService(db).SearchAsync(Query("photo"), CancellationToken.None);
        var all = await CreateService(db).SearchAsync(Query("all"), CancellationToken.None);

        Assert.Equal(new[] { 11, 12 }, photos.Projects.Select(p => p.Id));
        Assert.Equal(new[] { 11, 12 }, all.Projects.Select(p => p.Id));
    }

    private static MediaLibraryQuery Query(string kind) =>
        new(Query: null, Source: "all", Kind: kind, Classification: "all", ProjectId: null, PersonId: null,
            Year: null, PageNumber: 1, PageSize: 10, IncludePeople: false);

    private static MediaLibraryQueryService CreateService(MediaLibraryDbContext db)
    {
        var options = Options.Create(new MediaLibraryOptions
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true }
        });
        return new MediaLibraryQueryService(
            db,
            options,
            new NullDiagnostics(),
            new MediaAssetVisibilityPolicy(options),
            NullLogger<MediaLibraryQueryService>.Instance);
    }

    private static async Task<MediaLibraryDbContext> SeedAsync()
    {
        var db = new MediaLibraryDbContext(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"media-project-filter-{Guid.NewGuid():N}")
            .Options);
        var now = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
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
        db.Sources.Add(prism);
        db.Assets.AddRange(
            Asset(1, prism, 11, "Alpha", MediaAssetKind.Photo, MediaAssetOrigin.ProjectPhoto, now),
            Asset(2, prism, 12, "Bravo", MediaAssetKind.Video, MediaAssetOrigin.ProjectVideo, now));
        await db.SaveChangesAsync();
        return db;
    }

    private static MediaAsset Asset(long id, MediaLibrarySource source, int projectId, string title, MediaAssetKind kind, MediaAssetOrigin origin, DateTimeOffset now) =>
        new()
        {
            Id = id,
            SourceId = source.Id,
            Source = source,
            Origin = origin,
            Kind = kind,
            ProjectId = projectId,
            IsAvailable = true,
            AvailabilityStatus = MediaAvailabilityStatus.Available,
            SourceEntityId = $"asset:{id}",
            ContextKey = $"project:{projectId}",
            CollectionKey = $"project:{projectId}",
            ContextTitle = title,
            ContextSubtitle = "Project media",
            SourceLabel = "Project",
            Title = $"Item {id}",
            OriginalFileName = $"item-{id}",
            ContentType = kind == MediaAssetKind.Photo ? "image/jpeg" : "video/mp4",
            MediaDateUtc = now,
            IndexedAtUtc = now,
            LastSeenAtUtc = now
        };

    private sealed class NullDiagnostics : IMediaLibraryDiagnostics
    {
        public MediaLibraryDiagnosticEvent RecordSuccess(MediaLibraryQueryOperation operation, long durationMilliseconds) =>
            new(operation, true, "TEST", "Healthy", DateTimeOffset.UtcNow, durationMilliseconds);

        public MediaLibraryDiagnosticEvent RecordFailure(MediaLibraryQueryOperation operation, Exception exception, long durationMilliseconds) =>
            new(operation, false, "TEST", exception.Message, DateTimeOffset.UtcNow, durationMilliseconds);

        public IReadOnlyList<MediaLibraryDiagnosticEvent> GetLatest() => Array.Empty<MediaLibraryDiagnosticEvent>();
    }
}
