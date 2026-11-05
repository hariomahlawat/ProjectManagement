using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

// "Data moved since last generated": the facts a generation keeps, and how the library compares
// them with what the same resolvers say today.
public sealed class ProjectBriefingDataMovementTests
{
    private static readonly DateOnly RemarkDate = new(2026, 10, 30);

    [Fact]
    public void FactsKeptAtGeneration_MatchTodaysFactsForUnchangedData()
    {
        // Generation reads the presentation project (with the slide's "no status" text); the
        // library reads current facts. The same data must give the same fact.
        var presented = new ProjectBriefingPresentationProject
        {
            ProjectId = 7,
            PresentStageCode = "devp",
            ExternalStatus = ProjectBriefingSlideFacts.NoExternalStatus,
            CostRd = Cost(1_250_000.4m),
            IpaCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA),
            ProliferationCost = Cost(300_000m)
        };
        var current = new ProjectBriefingCurrentProjectFacts(
            7, "Counter-UAS", "DEVP", null, null,
            Cost(1_250_000m), ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA), Cost(300_000m));

        Assert.Equal(
            ProjectBriefingSlideFacts.FromPresentation(presented),
            current.ToSlideFact(ProjectBriefingLayout.StandardBriefing));
    }

    [Fact]
    public void Compare_CountsStageStatusAndCostSeparately()
    {
        var generated = new[]
        {
            Fact(1, "DEVP", "On track", 100m),
            Fact(2, "AON", "Trials", 200m),
            Fact(3, "SO", null, 300m),
            Fact(4, "DEVP", "Unchanged", 400m)
        };
        var current = new Dictionary<int, ProjectBriefingCurrentProjectFacts>
        {
            [1] = Current(1, "COMPLETED", "On track", 100m),
            [2] = Current(2, "AON", "Trials complete", 250m),
            [3] = Current(3, "SO", null, 300m),
            [4] = Current(4, "DEVP", "Unchanged", 400m)
        };

        var movement = ProjectBriefingSlideFacts.Compare(generated, ProjectBriefingLayout.StandardBriefing, current);

        Assert.Equal(1, movement.StageChanges);
        Assert.Equal(1, movement.StatusChanges);
        Assert.Equal(1, movement.CostChanges);
        Assert.Equal(3, movement.Total);
        Assert.Equal(new[] { 1, 2 }, movement.Projects.Select(project => project.ProjectId));
        Assert.Equal(
            new[] { "1 project changed stage", "1 new status remark", "1 cost revised" },
            movement.Describe());
    }

    [Fact]
    public void Compare_LeavesOutProjectsThatAreGoneAndIgnoresProliferationOnUpdateSheets()
    {
        var generated = new[]
        {
            ProjectBriefingSlideFacts.Create(1, "DEVP", "x", RemarkDate, Cost(100m), Cost(0m), ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation)),
            Fact(9, "DEVP", "deleted since", 1m)
        };
        var current = new Dictionary<int, ProjectBriefingCurrentProjectFacts>
        {
            [1] = new(1, "One", "DEVP", "x", RemarkDate, Cost(100m), Cost(0m), Cost(999m))
        };

        var movement = ProjectBriefingSlideFacts.Compare(generated, ProjectBriefingLayout.ProjectUpdateSheet, current);

        Assert.False(movement.HasMoved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void Read_ReturnsNullWhenNothingUsableWasKept(string? json)
        => Assert.Null(ProjectBriefingSlideFacts.Read(json));

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        var facts = new[] { Fact(2, "AON", "b", 2m), Fact(1, "DEVP", "a", 1m) };

        var read = ProjectBriefingSlideFacts.Read(ProjectBriefingSlideFacts.Write(facts));

        Assert.Equal(facts.OrderBy(fact => fact.ProjectId), read);
    }

    [Fact]
    public async Task Library_ShowsMovementFromTheSameResolversAsTheSlides()
    {
        await using var db = CreateDb();
        db.Projects.AddRange(
            new Project { Id = 1, Name = "Counter-UAS System", CreatedByUserId = "u" },
            new Project { Id = 2, Name = "VR Based Map Reading", CreatedByUserId = "u" });
        await db.SaveChangesAsync();
        var costs = new FakeCosts();
        var statuses = new FakeStatuses();
        costs.CostRd[1] = Cost(100m);
        costs.CostRd[2] = Cost(200m);
        statuses.Latest[2] = new ProjectBriefingExternalStatus(2, 11, "Trials planned", RemarkDate, DateTime.UtcNow);
        var service = Service(db, costs, statuses);

        // What a generation would have kept, today.
        var kept = (await service.GetCurrentSlideFactsAsync(new[] { 1, 2 }))
            .Values.Select(facts => facts.ToSlideFact(ProjectBriefingLayout.StandardBriefing))
            .ToList();
        var generatedDeck = Summary(1, "Monthly review") with { LastGeneratedAtUtc = DateTimeOffset.UtcNow, GeneratedFacts = kept };
        var oldDeck = Summary(2, "Generated before tracking") with { LastGeneratedAtUtc = DateTimeOffset.UtcNow };
        var neverGenerated = Summary(3, "Never generated");

        // Then project 1 completes, project 2 gets a new remark and a revised cost.
        var project = await db.Projects.SingleAsync(p => p.Id == 1);
        project.LifecycleStatus = ProjectLifecycleStatus.Completed;
        await db.SaveChangesAsync();
        costs.CostRd[2] = Cost(250m);
        statuses.Latest[2] = new ProjectBriefingExternalStatus(2, 12, "Trials complete", RemarkDate.AddDays(5), DateTime.UtcNow);

        var decks = await service.WithDataMovementAsync(new[] { generatedDeck, oldDeck, neverGenerated });

        var movement = decks[0].DataMovement!;
        Assert.Equal((1, 1, 1), (movement.StageChanges, movement.StatusChanges, movement.CostChanges));
        Assert.Equal(new[] { "Counter-UAS System", "VR Based Map Reading" }, movement.Projects.Select(p => p.ProjectName).OrderBy(n => n));
        Assert.Null(decks[1].DataMovement);
        Assert.Null(decks[2].DataMovement);
    }

    [Fact]
    public async Task List_ReadsBackTheFactsKeptAtGeneration()
    {
        await using var db = CreateDb();
        db.Users.Add(new ApplicationUser { Id = "u", UserName = "u", FullName = "U" });
        db.Set<ProjectBriefingDeck>().Add(new ProjectBriefingDeck
        {
            Id = 5,
            Name = "Deck",
            NormalizedName = "DECK",
            OwnerUserId = "u",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        });
        await db.SaveChangesAsync();
        var facts = ProjectBriefingSlideFacts.Write(new[] { Fact(1, "DEVP", "a", 1m) });
        var deckService = new ProjectBriefingDeckService(db, Stub<IProjectBriefingSelectionService>(), new NoAudit(), new FixedClock(), NullLogger<ProjectBriefingDeckService>.Instance);

        // ExecuteUpdate is not supported by the in-memory provider; the stored value is what the
        // list reads, so set it as MarkGeneratedAsync does.
        var deck = await db.Set<ProjectBriefingDeck>().SingleAsync();
        deck.LastGeneratedAtUtc = DateTimeOffset.UtcNow;
        deck.LastGeneratedFactsJson = facts;
        await db.SaveChangesAsync();

        var listed = Assert.Single(await deckService.ListAsync("u"));

        Assert.Equal(ProjectBriefingSlideFacts.Read(facts), listed.GeneratedFacts);
    }

    private static ProjectBriefingSlideFact Fact(int id, string stage, string? status, decimal cost)
        => Current(id, stage, status, cost).ToSlideFact(ProjectBriefingLayout.StandardBriefing);

    private static ProjectBriefingCurrentProjectFacts Current(int id, string stage, string? status, decimal cost)
        => new(id, $"Project {id}", stage, status, status is null ? null : RemarkDate,
            Cost(cost), ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA),
            ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation));

    private static ProjectBriefingCostValue Cost(decimal rupees)
        => new(rupees, ProjectBriefingCostBasis.None, rupees.ToString(), string.Empty);

    private static ProjectBriefingDeckSummaryVm Summary(long id, string name)
        => new(id, name, null, default, default, 2, DateTimeOffset.UtcNow, null, "U", "U", string.Empty);

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-data-movement-{Guid.NewGuid()}")
            .Options);

    private static ProjectBriefingDataService Service(ApplicationDbContext db, FakeCosts costs, FakeStatuses statuses)
        => new(
            db,
            costs,
            statuses,
            Stub<IProjectBriefingPhotoLoader>(),
            Stub<IProjectBriefingUpdateSheetFactsResolver>(),
            Stub<IProjectBriefingInstitutionalProfileService>(),
            Stub<ProjectManagement.Services.Ffc.IFfcFootprintService>(),
            Stub<ProjectManagement.Services.Ffc.Presentation.IFfcPresentationMapRenderer>(),
            new FixedClock(),
            NullLogger<ProjectBriefingDataService>.Instance);

    // A stand-in for a dependency these tests never reach.
    private static T Stub<T>() where T : class => DispatchProxy.Create<T, Unused>();

    public class Unused : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new NotSupportedException($"{targetMethod?.Name} is not used by this test.");
    }

    private sealed class FakeCosts : IProjectBriefingCostResolver
    {
        public Dictionary<int, ProjectBriefingCostValue> CostRd { get; } = new();

        public Task<ProjectBriefingResolvedCosts> ResolveCostsAsync(IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default)
            => Task.FromResult(new ProjectBriefingResolvedCosts(
                projectIds.Where(CostRd.ContainsKey).ToDictionary(id => id, id => CostRd[id]),
                new Dictionary<int, ProjectBriefingCostValue>()));

        public async Task<IReadOnlyDictionary<int, ProjectBriefingCostValue>> ResolveCostRdAsync(IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default)
            => (await ResolveCostsAsync(projectIds, cancellationToken)).CostRd;

        public Task<IReadOnlyDictionary<int, ProjectBriefingCostValue>> ResolveProliferationCostAsync(IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, ProjectBriefingCostValue>>(new Dictionary<int, ProjectBriefingCostValue>());
    }

    private sealed class FakeStatuses : IProjectBriefingExternalStatusService
    {
        public Dictionary<int, ProjectBriefingExternalStatus> Latest { get; } = new();

        public Task<IReadOnlyDictionary<int, ProjectBriefingExternalStatus>> GetLatestAsync(IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, ProjectBriefingExternalStatus>>(
                projectIds.Where(Latest.ContainsKey).ToDictionary(id => id, id => Latest[id]));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null)
            => Task.CompletedTask;
    }
}
