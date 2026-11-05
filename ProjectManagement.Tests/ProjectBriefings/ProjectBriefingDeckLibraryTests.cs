using System;
using System.Collections.Generic;
using System.Linq;
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

// What the deck library shows for each deck: its purpose, whether it is the person's own, the
// slides of the last generated PowerPoint and the project names search matches on.
public sealed class ProjectBriefingDeckLibraryTests
{
    private const string Me = "user-me";
    private const string Colleague = "user-colleague";

    [Fact]
    public async Task List_DescribesEachDeckForTheLibrary()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        db.Projects.AddRange(
            new Project { Id = 10, Name = "VR Based Map Reading", CreatedByUserId = Me },
            new Project { Id = 11, Name = "Counter-UAS System", CreatedByUserId = Me });
        db.AddRange(
            Deck(1, "Visit deck", owner: Colleague, lastEditor: Colleague, now,
                ProjectBriefingDeckConfigurationCodec.WithPreset(null, ProjectBriefingDeckPreset.VisitOrExternalAudience),
                slideCount: 33, projectIds: new[] { 10, 11 }),
            Deck(2, "My deck", owner: Me, lastEditor: null, now.AddHours(-1), null, slideCount: null, projectIds: new[] { 10 }),
            Deck(3, "Edited by me", owner: Colleague, lastEditor: Me, now.AddHours(-2), null, slideCount: null, projectIds: Array.Empty<int>()));
        await db.SaveChangesAsync();

        var decks = (await Service(db).ListAsync(Me)).ToDictionary(deck => deck.Name);

        var visit = decks["Visit deck"];
        Assert.Equal(ProjectBriefingDeckPresets.Get(ProjectBriefingDeckPreset.VisitOrExternalAudience).Title, visit.Purpose);
        Assert.False(visit.IsMine);
        Assert.False(visit.LastEditedByMe);
        Assert.Equal(33, visit.LastGeneratedSlideCount);
        Assert.Equal(new[] { "Counter-UAS System", "VR Based Map Reading" }, visit.ProjectNames.OrderBy(name => name));

        var mine = decks["My deck"];
        Assert.Equal("Set up by hand", mine.Purpose);
        Assert.True(mine.IsMine);
        Assert.True(mine.LastEditedByMe);
        Assert.Null(mine.LastGeneratedSlideCount);

        var editedByMe = decks["Edited by me"];
        Assert.True(editedByMe.IsMine);
        Assert.True(editedByMe.LastEditedByMe);
    }

    private static ProjectBriefingDeck Deck(
        long id,
        string name,
        string owner,
        string? lastEditor,
        DateTimeOffset updated,
        string? configuration,
        int? slideCount,
        IReadOnlyList<int> projectIds)
        => new()
        {
            Id = id,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            OwnerUserId = owner,
            LastModifiedByUserId = lastEditor,
            CreatedAtUtc = updated,
            UpdatedAtUtc = updated,
            RowVersion = Guid.NewGuid().ToByteArray(),
            SelectionRulesJson = configuration,
            LastGeneratedSlideCount = slideCount,
            Items = projectIds
                .Select((projectId, index) => new ProjectBriefingDeckItem { ProjectId = projectId, SortOrder = index, AddedAtUtc = updated })
                .ToList()
        };

    private static ProjectBriefingDeckService Service(ApplicationDbContext db)
        => new(db, new NoSelection(), new NoAudit(), new FixedClock(), NullLogger<ProjectBriefingDeckService>.Instance);

    private static ApplicationDbContext CreateDb()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-deck-library-{Guid.NewGuid()}")
            .Options);
        db.Users.AddRange(
            new ApplicationUser { Id = Me, UserName = "me", FullName = "Me" },
            new ApplicationUser { Id = Colleague, UserName = "colleague", FullName = "Colleague" });
        db.SaveChanges();
        return db;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
    }

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
