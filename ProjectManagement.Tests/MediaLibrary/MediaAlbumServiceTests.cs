using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaAlbumServiceTests
{
    [Fact]
    public async Task Album_IsOrganisationWide_ButCreatorOwnsRoutineMutation()
    {
        await using var db = CreateContext();
        await SeedVisibleAssetAsync(db, 1);
        var service = CreateService(db);
        var creator = new MediaAlbumActor("user-a", false);
        var other = new MediaAlbumActor("user-b", false);

        var created = await service.CreateAsync("Annual highlights", null, new long[] { 1 }, creator, CancellationToken.None);
        Assert.True(created.Succeeded);
        Assert.NotNull(created.AlbumId);

        var pageForOther = await service.SearchAsync(
            new MediaAlbumListQuery(null, "newest", 1, 48, false, other),
            CancellationToken.None);
        var album = Assert.Single(pageForOther.Albums);
        Assert.Equal(created.AlbumId, album.Id);
        Assert.False(album.CanManage);

        var blocked = await service.UpdateMetadataAsync(
            album.Id,
            "Changed by someone else",
            null,
            (await service.GetDetailsAsync(album.Id, creator, CancellationToken.None))!.ConcurrencyToken,
            other,
            CancellationToken.None);
        Assert.False(blocked.Succeeded);
        Assert.Equal(MediaAlbumMutationFailure.Forbidden, blocked.Failure);
    }

    [Fact]
    public async Task ElevatedCurator_CanManageAnyOrganisationAlbum()
    {
        await using var db = CreateContext();
        await SeedVisibleAssetAsync(db, 1);
        var service = CreateService(db);
        var creator = new MediaAlbumActor("user-a", false);
        var curator = new MediaAlbumActor("comdt", true);
        var created = await service.CreateAsync("Visit highlights", null, new long[] { 1 }, creator, CancellationToken.None);
        var details = await service.GetDetailsAsync(created.AlbumId!.Value, curator, CancellationToken.None);

        Assert.NotNull(details);
        Assert.True(details.CanManage);

        var updated = await service.UpdateMetadataAsync(
            details.Id,
            "Visit highlights — final",
            "Curated organisation-wide set",
            details.ConcurrencyToken,
            curator,
            CancellationToken.None);
        Assert.True(updated.Succeeded);
    }

    [Fact]
    public async Task AddItems_IsIdempotent_AndDoesNotDuplicateMembership()
    {
        await using var db = CreateContext();
        await SeedVisibleAssetAsync(db, 1);
        var service = CreateService(db);
        var actor = new MediaAlbumActor("user-a", false);
        var created = await service.CreateAsync("Briefing set", null, new long[] { 1 }, actor, CancellationToken.None);

        var result = await service.AddItemsAsync(created.AlbumId!.Value, new long[] { 1, 1 }, actor, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(await db.AlbumItems.ToListAsync());
    }

    [Fact]
    public async Task AddItems_AddsOnlyNewMemberships_WhenSelectionContainsExistingMedia()
    {
        await using var db = CreateContext();
        await SeedVisibleAssetAsync(db, 1);
        await SeedVisibleAssetAsync(db, 2);
        var service = CreateService(db);
        var actor = new MediaAlbumActor("user-a", false);
        var created = await service.CreateAsync("Target album", null, new long[] { 1 }, actor, CancellationToken.None);

        var result = await service.AddItemsAsync(created.AlbumId!.Value, new long[] { 1, 2 }, actor, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AffectedCount);
        Assert.Equal(new long[] { 1, 2 }, await db.AlbumItems.OrderBy(item => item.MediaAssetId).Select(item => item.MediaAssetId).ToArrayAsync());
    }

    [Fact]
    public async Task ArchivedAlbum_RejectsTargetedAddItems()
    {
        await using var db = CreateContext();
        await SeedVisibleAssetAsync(db, 1);
        await SeedVisibleAssetAsync(db, 2);
        var service = CreateService(db);
        var actor = new MediaAlbumActor("user-a", false);
        var created = await service.CreateAsync("Archived target", null, new long[] { 1 }, actor, CancellationToken.None);
        var details = await service.GetDetailsAsync(created.AlbumId!.Value, actor, CancellationToken.None);
        Assert.NotNull(details);
        var archived = await service.SetArchivedAsync(details.Id, true, details.ConcurrencyToken, actor, CancellationToken.None);
        Assert.True(archived.Succeeded);

        var result = await service.AddItemsAsync(details.Id, new long[] { 2 }, actor, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(MediaAlbumMutationFailure.InvalidRequest, result.Failure);
        Assert.Contains("Restore", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditorialCaption_RequiresElevatedCurationAuthority()
    {
        await using var db = CreateContext();
        await SeedVisibleAssetAsync(db, 1);
        var service = CreateService(db);

        var denied = await service.UpdateEditorialCaptionAsync(
            1,
            "Institutional caption",
            null,
            new MediaAlbumActor("user-a", false),
            CancellationToken.None);
        Assert.Equal(MediaAlbumMutationFailure.Forbidden, denied.Failure);

        var allowed = await service.UpdateEditorialCaptionAsync(
            1,
            "Institutional caption",
            null,
            new MediaAlbumActor("hod", true),
            CancellationToken.None);
        Assert.True(allowed.Succeeded);
        Assert.Equal("Institutional caption", (await db.Assets.SingleAsync()).EditorialCaption);
    }

    private static MediaLibraryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"media-albums-{Guid.NewGuid():N}")
            .Options;
        return new MediaLibraryDbContext(options);
    }

    private static MediaAlbumService CreateService(MediaLibraryDbContext db)
    {
        var options = new MediaLibraryOptions
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true },
            ExternalSources = new ExternalMediaSourcesOptions { Enabled = true }
        };
        return new MediaAlbumService(
            db,
            new MediaAssetVisibilityPolicy(Options.Create(options)),
            NullLogger<MediaAlbumService>.Instance);
    }

    private static async Task SeedVisibleAssetAsync(MediaLibraryDbContext db, long assetId)
    {
        var source = new MediaLibrarySource
        {
            Id = Guid.NewGuid(),
            Key = $"test-{assetId}",
            Name = "Test source",
            SourceType = MediaLibrarySourceType.Prism,
            IsEnabled = true,
            IsVisibleInLibrary = true
        };
        db.Sources.Add(source);
        db.Assets.Add(new MediaAsset
        {
            Id = assetId,
            SourceId = source.Id,
            Source = source,
            Origin = MediaAssetOrigin.ProjectPhoto,
            Kind = MediaAssetKind.Photo,
            IsAvailable = true,
            AvailabilityStatus = MediaAvailabilityStatus.Available,
            SourceEntityId = $"project-photo:{assetId}",
            ContextKey = "project:1",
            CollectionKey = "project:1",
            ContextTitle = "Test project",
            ContextSubtitle = "Project media",
            SourceLabel = "Project",
            Title = "Photo",
            OriginalFileName = "photo.jpg",
            ContentType = "image/jpeg",
            MediaDateUtc = DateTimeOffset.UtcNow,
            IndexedAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
