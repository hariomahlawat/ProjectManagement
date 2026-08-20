using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Models;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class MediaPersonUserLinkServiceTests
{
    [Fact]
    public async Task Link_IsExplicitOneToOne_AndResolvesLightweightPhotoIdentity()
    {
        await using var media = CreateMediaContext();
        await using var app = CreateApplicationContext();
        var person = ConfirmedPerson("Colonel Test User");
        var otherPerson = ConfirmedPerson("Another Person");
        media.Persons.AddRange(person, otherPerson);
        app.Users.AddRange(
            ActiveUser("user-1", "Colonel Test User", "Col"),
            ActiveUser("user-2", "Second User", "Lt Col"));
        await media.SaveChangesAsync();
        await app.SaveChangesAsync();

        var service = new MediaPersonUserLinkService(media, app, NullLogger<MediaPersonUserLinkService>.Instance);
        var linked = await service.LinkAsync(person.Id, "user-1", "reviewer", CancellationToken.None);
        var lightweight = await service.GetPhotoIdentityForUserAsync("user-1", CancellationToken.None);

        Assert.Equal(person.Id, linked.PersonId);
        Assert.NotNull(lightweight);
        Assert.Equal(person.Id, lightweight!.PersonId);
        Assert.Equal(person.DisplayName, lightweight.PersonDisplayName);
        Assert.False(lightweight.UsePortraitAsAvatar);
        Assert.False(linked.UsePortraitAsAvatar);
        Assert.Single(media.PersonUserLinks.Where(item => item.UnlinkedAtUtc == null));
        Assert.Contains(media.IdentityAudits, audit => audit.Action == "PrismUserLinked" && audit.PersonId == person.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkAsync(otherPerson.Id, "user-1", "reviewer", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkAsync(person.Id, "user-2", "reviewer", CancellationToken.None));
    }

    [Fact]
    public async Task HiddenPerson_LinkRemainsGovernableButIsNotExposedAsMyPhotos()
    {
        await using var media = CreateMediaContext();
        await using var app = CreateApplicationContext();
        var person = ConfirmedPerson("Hidden Identity");
        media.Persons.Add(person);
        app.Users.Add(ActiveUser("user-1", "Hidden User", "Col"));
        await media.SaveChangesAsync();
        await app.SaveChangesAsync();

        var service = new MediaPersonUserLinkService(media, app, NullLogger<MediaPersonUserLinkService>.Instance);
        await service.LinkAsync(person.Id, "user-1", "reviewer", CancellationToken.None);
        person.IsHidden = true;
        await media.SaveChangesAsync();

        Assert.NotNull(await service.GetForPersonAsync(person.Id, CancellationToken.None));
        Assert.Null(await service.GetPhotoIdentityForUserAsync("user-1", CancellationToken.None));

        await service.UnlinkAsync(person.Id, "reviewer", "Identity hidden during review", CancellationToken.None);
        Assert.Empty(await media.PersonUserLinks.Where(item => item.UnlinkedAtUtc == null).ToListAsync());
    }

    [Fact]
    public async Task Link_RejectsNonHumanPrismAccounts()
    {
        await using var media = CreateMediaContext();
        await using var app = CreateApplicationContext();
        var person = ConfirmedPerson("Human Identity");
        media.Persons.Add(person);
        app.Users.Add(new ApplicationUser
        {
            Id = "service-user",
            UserName = "service-user",
            NormalizedUserName = "SERVICE-USER",
            FullName = "Service Account",
            Rank = string.Empty,
            AccountKind = UserAccountKind.Service
        });
        await media.SaveChangesAsync();
        await app.SaveChangesAsync();

        var service = new MediaPersonUserLinkService(media, app, NullLogger<MediaPersonUserLinkService>.Instance);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.LinkAsync(person.Id, "service-user", "reviewer", CancellationToken.None));
    }

    [Fact]
    public async Task Unlink_PreservesHistory_AndAllowsANewActiveLink()
    {
        await using var media = CreateMediaContext();
        await using var app = CreateApplicationContext();
        var first = ConfirmedPerson("First Identity");
        var second = ConfirmedPerson("Second Identity");
        media.Persons.AddRange(first, second);
        app.Users.Add(ActiveUser("user-1", "Test User", "Col"));
        await media.SaveChangesAsync();
        await app.SaveChangesAsync();

        var service = new MediaPersonUserLinkService(media, app, NullLogger<MediaPersonUserLinkService>.Instance);
        await service.LinkAsync(first.Id, "user-1", "reviewer", CancellationToken.None);
        await service.UnlinkAsync(first.Id, "reviewer", "Wrong media identity", CancellationToken.None);
        Assert.Null(await service.GetPhotoIdentityForUserAsync("user-1", CancellationToken.None));
        await service.LinkAsync(second.Id, "user-1", "reviewer", CancellationToken.None);

        var active = await service.GetPhotoIdentityForUserAsync("user-1", CancellationToken.None);
        Assert.NotNull(active);
        Assert.Equal(second.Id, active!.PersonId);
        Assert.Equal(2, await media.PersonUserLinks.CountAsync());
        Assert.Single(await media.PersonUserLinks.Where(item => item.UnlinkedAtUtc == null).ToListAsync());
        Assert.Contains(media.IdentityAudits, audit => audit.Action == "PrismUserUnlinked" && audit.PersonId == first.Id);
    }


    [Fact]
    public async Task AvatarPreference_IsExplicit_Verified_AndRequiresAUsableRepresentativePortrait()
    {
        await using var media = CreateMediaContext();
        await using var app = CreateApplicationContext();
        var person = ConfirmedPerson("Portrait Preference");
        media.Persons.Add(person);
        app.Users.Add(ActiveUser("user-1", "Portrait User", "Col"));
        await media.SaveChangesAsync();
        await app.SaveChangesAsync();

        var service = new MediaPersonUserLinkService(media, app, NullLogger<MediaPersonUserLinkService>.Instance);
        await service.LinkAsync(person.Id, "user-1", "reviewer", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetAvatarPreferenceAsync("user-1", true, CancellationToken.None));

        var link = await media.PersonUserLinks.SingleAsync();
        Assert.False(link.UsePortraitAsAvatar);

        var firstFace = AddUsablePortrait(media, person, "faces/portrait-1.webp");
        await media.SaveChangesAsync();

        var enabled = await service.SetAvatarPreferenceAsync("user-1", true, CancellationToken.None);
        Assert.True(enabled.UsePortraitAsAvatar);
        Assert.True(enabled.CanUsePortraitAsAvatar);
        Assert.True(enabled.ShouldUsePortraitAsAvatar);
        var managerView = await service.GetForPersonAsync(person.Id, CancellationToken.None);
        Assert.NotNull(managerView);
        Assert.True(managerView!.ShouldUsePortraitAsAvatar);

        var secondFace = AddUsablePortrait(media, person, "faces/portrait-2.webp", makeRepresentative: false);
        person.RepresentativeFaceId = secondFace.Id;
        await media.SaveChangesAsync();

        var afterRepresentativeChange = await service.GetPhotoIdentityForUserAsync("user-1", CancellationToken.None);
        Assert.NotNull(afterRepresentativeChange);
        Assert.True(afterRepresentativeChange!.ShouldUsePortraitAsAvatar);
        Assert.NotEqual(firstFace.Id, person.RepresentativeFaceId);
        managerView = await service.GetForPersonAsync(person.Id, CancellationToken.None);
        Assert.NotNull(managerView);
        Assert.True(managerView!.ShouldUsePortraitAsAvatar);

        var disabled = await service.SetAvatarPreferenceAsync("user-1", false, CancellationToken.None);
        Assert.False(disabled.UsePortraitAsAvatar);
        Assert.False(disabled.ShouldUsePortraitAsAvatar);
        managerView = await service.GetForPersonAsync(person.Id, CancellationToken.None);
        Assert.NotNull(managerView);
        Assert.False(managerView!.ShouldUsePortraitAsAvatar);
        Assert.Equal(2, await media.IdentityAudits.CountAsync(audit => audit.Action == "PrismUserAvatarPreferenceChanged"));
    }

    [Fact]
    public async Task IncorrectIdentityReport_DisablesAvatarAndCanBeResolvedByManager()
    {
        await using var media = CreateMediaContext();
        await using var app = CreateApplicationContext();
        var person = ConfirmedPerson("Reported Identity");
        media.Persons.Add(person);
        app.Users.Add(ActiveUser("user-1", "Reported User", "Col"));
        await media.SaveChangesAsync();
        await app.SaveChangesAsync();

        var service = new MediaPersonUserLinkService(media, app, NullLogger<MediaPersonUserLinkService>.Instance);
        await service.LinkAsync(person.Id, "user-1", "reviewer", CancellationToken.None);
        var stored = await media.PersonUserLinks.SingleAsync();
        stored.UsePortraitAsAvatar = true;
        await media.SaveChangesAsync();

        await service.ReportIncorrectLinkAsync("user-1", "This is a different person", CancellationToken.None);

        stored = await media.PersonUserLinks.SingleAsync();
        Assert.False(stored.UsePortraitAsAvatar);
        Assert.NotNull(stored.ConcernRaisedAtUtc);
        Assert.Null(stored.ConcernResolvedAtUtc);
        var lightweight = await service.GetPhotoIdentityForUserAsync("user-1", CancellationToken.None);
        Assert.NotNull(lightweight);
        Assert.True(lightweight!.HasOpenConcern);
        Assert.False(lightweight.ShouldUsePortraitAsAvatar);
        var managerView = await service.GetForPersonAsync(person.Id, CancellationToken.None);
        Assert.NotNull(managerView);
        Assert.True(managerView!.HasOpenConcern);
        Assert.False(managerView!.ShouldUsePortraitAsAvatar);
        Assert.Contains(media.IdentityAudits, audit => audit.Action == "PrismUserLinkConcernRaised");

        await service.ResolveLinkConcernAsync(person.Id, "reviewer", "Account holder and identity were re-verified", CancellationToken.None);
        stored = await media.PersonUserLinks.SingleAsync();
        Assert.NotNull(stored.ConcernResolvedAtUtc);
        Assert.False(stored.UsePortraitAsAvatar);
        lightweight = await service.GetPhotoIdentityForUserAsync("user-1", CancellationToken.None);
        Assert.NotNull(lightweight);
        Assert.False(lightweight!.HasOpenConcern);
        Assert.False(lightweight.ShouldUsePortraitAsAvatar);
        managerView = await service.GetForPersonAsync(person.Id, CancellationToken.None);
        Assert.NotNull(managerView);
        Assert.False(managerView!.HasOpenConcern);
        Assert.False(managerView!.ShouldUsePortraitAsAvatar);
        Assert.Contains(media.IdentityAudits, audit => audit.Action == "PrismUserLinkConcernResolved");
    }


    private static MediaFace AddUsablePortrait(
        MediaLibraryDbContext media,
        MediaPerson person,
        string thumbnailPath,
        bool makeRepresentative = true)
    {
        var now = DateTimeOffset.UtcNow;
        var face = new MediaFace
        {
            Id = Guid.NewGuid(),
            MediaAssetId = 0,
            SequenceNumber = 1,
            Left = 0.1,
            Top = 0.1,
            Width = 0.5,
            Height = 0.5,
            DetectionConfidence = 0.99,
            QualityScore = 0.95,
            DetectorModelKey = "test",
            DetectorModelVersion = "1",
            ReviewThumbnailPath = thumbnailPath,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var assignment = new MediaPersonFace
        {
            MediaPersonId = person.Id,
            MediaPerson = person,
            MediaFaceId = face.Id,
            MediaFace = face,
            AssignmentType = FaceAssignmentType.HumanConfirmed,
            AssignedByUserId = "reviewer",
            AssignedAtUtc = now
        };
        media.Faces.Add(face);
        media.PersonFaces.Add(assignment);
        if (makeRepresentative)
        {
            person.RepresentativeFaceId = face.Id;
        }
        return face;
    }

    private static MediaLibraryDbContext CreateMediaContext()
        => new(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"media-person-link-{Guid.NewGuid():N}")
            .Options);

    private static ApplicationDbContext CreateApplicationContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"application-person-link-{Guid.NewGuid():N}")
            .Options);

    private static MediaPerson ConfirmedPerson(string name)
        => new()
        {
            Id = Guid.NewGuid(),
            DisplayName = name,
            NormalizedName = name.ToUpperInvariant(),
            Status = MediaPersonStatus.Confirmed,
            CreatedByUserId = "reviewer",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

    private static ApplicationUser ActiveUser(string id, string fullName, string rank)
        => new()
        {
            Id = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            FullName = fullName,
            Rank = rank,
            AccountKind = UserAccountKind.Human,
            IsDisabled = false,
            PendingDeletion = false
        };
}
