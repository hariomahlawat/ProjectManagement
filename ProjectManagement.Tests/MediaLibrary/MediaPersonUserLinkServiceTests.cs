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
        await service.LinkAsync(second.Id, "user-1", "reviewer", CancellationToken.None);

        var active = await service.GetPhotoIdentityForUserAsync("user-1", CancellationToken.None);
        Assert.NotNull(active);
        Assert.Equal(second.Id, active!.PersonId);
        Assert.Equal(2, await media.PersonUserLinks.CountAsync());
        Assert.Single(await media.PersonUserLinks.Where(item => item.UnlinkedAtUtc == null).ToListAsync());
        Assert.Contains(media.IdentityAudits, audit => audit.Action == "PrismUserUnlinked" && audit.PersonId == first.Id);
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
