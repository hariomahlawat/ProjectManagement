using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.Projects;

/// <summary>
/// The one project-cost rule: a completed project uses PNC, then L1, then its recorded cost;
/// any other project uses PNC, then L1, then AoN, then IPA. Zero counts as not recorded.
/// </summary>
public sealed class ProjectCostResolverTests
{
    private static readonly DateTime At = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ProjectLifecycleStatus.Active)]
    [InlineData(ProjectLifecycleStatus.Completed)]
    public async Task Pnc_WinsOverEverything(ProjectLifecycleStatus lifecycle)
    {
        await using var db = CreateContext();
        AddProject(db, 1, lifecycle, costLakhs: 90m);
        AddPnc(db, 1, 2_500_000m);
        AddL1(db, 1, 3_000_000m);
        AddAon(db, 1, 4_000_000m);
        await db.SaveChangesAsync();

        var cost = await ResolveAsync(db, 1);

        Assert.Equal((ProjectCostSource.Pnc, 25m), (cost.Source, cost.CostInLakhs));
    }

    [Theory]
    [InlineData(ProjectLifecycleStatus.Active)]
    [InlineData(ProjectLifecycleStatus.Completed)]
    public async Task L1_IsUsedWhenThereIsNoPnc_AndAZeroPncCountsAsNone(ProjectLifecycleStatus lifecycle)
    {
        await using var db = CreateContext();
        AddProject(db, 1, lifecycle, costLakhs: 90m);
        AddPnc(db, 1, 0m);
        AddL1(db, 1, 2_599_000m);
        AddAon(db, 1, 4_000_000m);
        await db.SaveChangesAsync();

        var cost = await ResolveAsync(db, 1);

        Assert.Equal((ProjectCostSource.L1, 25.99m), (cost.Source, cost.CostInLakhs));
    }

    [Fact]
    public async Task CompletedProject_FallsBackToItsRecordedCost_NotToAonOrIpa()
    {
        await using var db = CreateContext();
        AddProject(db, 1, ProjectLifecycleStatus.Completed, costLakhs: 42.5m);
        AddAon(db, 1, 4_000_000m);
        AddIpa(db, 1, 5_000_000m);
        await db.SaveChangesAsync();

        var cost = await ResolveAsync(db, 1);

        Assert.Equal((ProjectCostSource.CostLakhs, 42.5m, "Recorded"), (cost.Source, cost.CostInLakhs, cost.SourceLabel));
    }

    [Fact]
    public async Task CompletedProject_WithoutPncL1OrRecordedCost_HasNoCost()
    {
        await using var db = CreateContext();
        AddProject(db, 1, ProjectLifecycleStatus.Completed, costLakhs: 0m);
        AddAon(db, 1, 4_000_000m);
        AddIpa(db, 1, 5_000_000m);
        await db.SaveChangesAsync();

        var cost = await ResolveAsync(db, 1);

        Assert.Equal(ProjectCostSource.None, cost.Source);
        Assert.Null(cost.CostInCr);
    }

    [Fact]
    public async Task OngoingProject_IgnoresItsRecordedCost_AndFallsBackToAonThenIpa()
    {
        await using var db = CreateContext();
        AddProject(db, 1, ProjectLifecycleStatus.Active, costLakhs: 90m);
        AddAon(db, 1, 4_000_000m);
        AddIpa(db, 1, 5_000_000m);
        AddProject(db, 2, ProjectLifecycleStatus.Active, costLakhs: 90m);
        AddIpa(db, 2, 5_000_000m);
        await db.SaveChangesAsync();

        var costs = await new ProjectCostResolver(db).ResolveCostInCrAsync(new[] { 1, 2 });

        Assert.Equal((ProjectCostSource.Aon, 40m), (costs[1].Source, costs[1].CostInLakhs));
        Assert.Equal((ProjectCostSource.Ipa, 50m), (costs[2].Source, costs[2].CostInLakhs));
    }

    [Fact]
    public async Task BriefingCostRd_FollowsTheSameRule_WithItsBasisLabel()
    {
        await using var db = CreateContext();
        AddProject(db, 1, ProjectLifecycleStatus.Active);
        AddPnc(db, 1, 2_500_000m);
        AddProject(db, 2, ProjectLifecycleStatus.Completed, costLakhs: 42.5m);
        await db.SaveChangesAsync();

        var costRd = await new ProjectBriefingCostResolver(db).ResolveCostRdAsync(new[] { 1, 2 });

        Assert.Equal((ProjectBriefingCostBasis.PNC, 2_500_000m, "PNC"), (costRd[1].Basis, costRd[1].AmountInRupees!.Value, costRd[1].BasisDisplay));
        Assert.Equal((ProjectBriefingCostBasis.Recorded, 4_250_000m, "Recorded"), (costRd[2].Basis, costRd[2].AmountInRupees!.Value, costRd[2].BasisDisplay));
    }

    [Fact]
    public async Task BriefingCosts_UseOneIpaSnapshotForCostRdAndTheIpaAmount()
    {
        await using var db = CreateContext();
        AddProject(db, 1, ProjectLifecycleStatus.Active);
        AddIpa(db, 1, 5_000_000m);
        await db.SaveChangesAsync();
        var ipaResolver = new CountingIpaResolver(new AuthoritativeIpaPositionResolver(db));

        var costs = await new ProjectBriefingCostResolver(db, ipaResolver).ResolveCostsAsync(new[] { 1 });

        Assert.Equal(1, ipaResolver.ManyCalls);
        Assert.Equal(costs.Ipa[1].AmountInRupees, costs.CostRd[1].AmountInRupees);
        Assert.Equal(ProjectBriefingCostBasis.IPA, costs.CostRd[1].Basis);
    }

    private sealed class CountingIpaResolver : IAuthoritativeIpaPositionResolver
    {
        private readonly IAuthoritativeIpaPositionResolver _inner;

        public CountingIpaResolver(IAuthoritativeIpaPositionResolver inner) => _inner = inner;

        public int ManyCalls { get; private set; }

        public Task<AuthoritativeIpaPosition?> ResolveAsync(int projectId, CancellationToken cancellationToken = default)
            => _inner.ResolveAsync(projectId, cancellationToken);

        public Task<IReadOnlyDictionary<int, AuthoritativeIpaPosition>> ResolveManyAsync(
            IReadOnlyCollection<int> projectIds,
            CancellationToken cancellationToken = default)
        {
            ManyCalls++;
            return _inner.ResolveManyAsync(projectIds, cancellationToken);
        }
    }

    private static async Task<ProjectCostResolution> ResolveAsync(ApplicationDbContext db, int projectId)
        => (await new ProjectCostResolver(db).ResolveCostInCrAsync(new[] { projectId }))[projectId];

    private static void AddProject(ApplicationDbContext db, int id, ProjectLifecycleStatus lifecycle, decimal? costLakhs = null)
        => db.Projects.Add(new Project
        {
            Id = id,
            Name = $"Project {id}",
            CreatedByUserId = "seed",
            LifecycleStatus = lifecycle,
            CostLakhs = costLakhs
        });

    private static void AddPnc(ApplicationDbContext db, int projectId, decimal rupees)
        => db.ProjectPncFacts.Add(new ProjectPncFact { ProjectId = projectId, PncCost = rupees, CreatedByUserId = "seed", CreatedOnUtc = At });

    private static void AddL1(ApplicationDbContext db, int projectId, decimal rupees)
        => db.ProjectCommercialFacts.Add(new ProjectCommercialFact { ProjectId = projectId, L1Cost = rupees, CreatedByUserId = "seed", CreatedOnUtc = At });

    private static void AddAon(ApplicationDbContext db, int projectId, decimal rupees)
        => db.ProjectAonFacts.Add(new ProjectAonFact { ProjectId = projectId, AonCost = rupees, CreatedByUserId = "seed", CreatedOnUtc = At });

    private static void AddIpa(ApplicationDbContext db, int projectId, decimal rupees)
        => db.ProjectIpaFacts.Add(new ProjectIpaFact { ProjectId = projectId, IpaCost = rupees, CreatedByUserId = "seed", CreatedOnUtc = At });

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
