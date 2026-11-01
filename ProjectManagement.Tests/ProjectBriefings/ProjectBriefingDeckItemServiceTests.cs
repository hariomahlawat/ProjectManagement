using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// Per-project choices stored on a deck item: the photograph (any of the project's photos, or
// its cover) and the speaker notes.
public sealed class ProjectBriefingDeckItemServiceTests
{
    private const string UserId = "user-1";

    [Fact]
    public async Task ChoosingAPhoto_StoresItAndRotatesTheDeckVersion()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db);
        var service = Service(db);
        var before = Convert.ToBase64String(deck.RowVersion);

        var after = await service.UpdateItemPhotoAsync(deck.Id, 10, UserId, 102, before);

        Assert.NotEqual(before, after);
        Assert.Equal(102, (await db.Set<ProjectBriefingDeckItem>().SingleAsync()).PhotoId);
    }

    [Fact]
    public async Task APhotoOfAnotherProject_IsRefused()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(db).UpdateItemPhotoAsync(deck.Id, 10, UserId, 201, Convert.ToBase64String(deck.RowVersion)));

        Assert.Contains("not one of the project's photos", error.Message);
        Assert.Null((await db.Set<ProjectBriefingDeckItem>().SingleAsync()).PhotoId);
    }

    [Fact]
    public async Task ClearingTheChoice_GoesBackToTheCover()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db, chosenPhotoId: 102);

        await Service(db).UpdateItemPhotoAsync(deck.Id, 10, UserId, null, Convert.ToBase64String(deck.RowVersion));

        Assert.Null((await db.Set<ProjectBriefingDeckItem>().SingleAsync()).PhotoId);
    }

    [Fact]
    public async Task AStaleVersion_IsAConflict()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            Service(db).UpdateItemPhotoAsync(deck.Id, 10, UserId, 102, Convert.ToBase64String(Guid.NewGuid().ToByteArray())));
    }

    [Fact]
    public async Task SpeakerNotes_AreStoredWithTidiedLineEnds_AndClearedWhenEmpty()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db);
        var service = Service(db);

        var version = await service.UpdateItemSpeakerNotesAsync(
            deck.Id, 10, UserId, "  First point.\r\nSecond point.  ", Convert.ToBase64String(deck.RowVersion));
        Assert.Equal("First point.\nSecond point.", (await db.Set<ProjectBriefingDeckItem>().SingleAsync()).SpeakerNotes);

        await service.UpdateItemSpeakerNotesAsync(deck.Id, 10, UserId, "   ", version);
        Assert.Null((await db.Set<ProjectBriefingDeckItem>().SingleAsync()).SpeakerNotes);
    }

    [Fact]
    public async Task SpeakerNotes_LongerThanTheLimit_AreRefused()
    {
        await using var db = CreateDb();
        var deck = await SeedAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).UpdateItemSpeakerNotesAsync(
            deck.Id, 10, UserId, new string('x', ProjectBriefingDeckService.SpeakerNotesMaxLength + 1), Convert.ToBase64String(deck.RowVersion)));
    }

    private static async Task<ProjectBriefingDeck> SeedAsync(ApplicationDbContext db, int? chosenPhotoId = null)
    {
        db.Projects.AddRange(Project(10, "Project ten"), Project(20, "Project twenty"));
        db.ProjectPhotos.AddRange(Photo(101, 10, isCover: true), Photo(102, 10, isCover: false), Photo(201, 20, isCover: true));
        var now = DateTimeOffset.UtcNow;
        var deck = new ProjectBriefingDeck
        {
            Name = "Photo deck",
            NormalizedName = "PHOTO DECK",
            OwnerUserId = UserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = Guid.NewGuid().ToByteArray(),
            Items = new List<ProjectBriefingDeckItem>
            {
                new() { ProjectId = 10, SortOrder = 10, AddedAtUtc = now, PhotoId = chosenPhotoId }
            }
        };
        db.Add(deck);
        await db.SaveChangesAsync();
        return deck;
    }

    private static Project Project(int id, string name) => new()
    {
        Id = id,
        Name = name,
        CreatedByUserId = UserId
    };

    private static ProjectPhoto Photo(int id, int projectId, bool isCover) => new()
    {
        Id = id,
        ProjectId = projectId,
        StorageKey = $"photos/{id}",
        OriginalFileName = $"{id}.jpg",
        ContentType = "image/jpeg",
        Width = 1600,
        Height = 900,
        IsCover = isCover,
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow
    };

    private static ProjectBriefingDeckService Service(ApplicationDbContext db)
        => new(db, new NoSelection(), new NoAudit(), new FixedClock(), NullLogger<ProjectBriefingDeckService>.Instance);

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-deck-item-{Guid.NewGuid()}")
            .Options);

    private sealed class NoAudit : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
            => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoSelection : IProjectBriefingSelectionService
    {
        public Task<ProjectBriefingSelectionOptionsVm> GetOptionsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ProjectBriefingSelectionResult> ResolveAsync(ProjectBriefingSelectionRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ProjectBriefingSearchResultVm>> SearchAsync(string? query, int take = 25, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<int>> ValidateProjectIdsAsync(IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
