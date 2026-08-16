using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests.Reports;

public sealed class ProjectSupplyOrderValueResolverTests
{
    [Fact]
    public async Task Pnc_cost_wins_over_l1_cost()
    {
        await using var db = CreateContext();
        db.ProjectCommercialFacts.Add(new ProjectCommercialFact
        {
            ProjectId = 1,
            L1Cost = 25_000_000m,
            CreatedByUserId = "test",
            CreatedOnUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        db.ProjectPncFacts.Add(new ProjectPncFact
        {
            ProjectId = 1,
            PncCost = 23_500_000m,
            CreatedByUserId = "test",
            CreatedOnUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var result = await new ProjectSupplyOrderValueResolver(db).ResolveAsync([1]);

        Assert.Equal(23_500_000m, result[1].AmountInRupees);
        Assert.Equal(ProjectSupplyOrderValueBasis.Pnc, result[1].Basis);
    }

    [Fact]
    public async Task L1_is_used_only_when_positive_pnc_is_absent()
    {
        await using var db = CreateContext();
        db.ProjectCommercialFacts.Add(new ProjectCommercialFact
        {
            ProjectId = 1,
            L1Cost = 17_000_000m,
            CreatedByUserId = "test",
            CreatedOnUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await new ProjectSupplyOrderValueResolver(db).ResolveAsync([1]);

        Assert.Equal(17_000_000m, result[1].AmountInRupees);
        Assert.Equal(ProjectSupplyOrderValueBasis.L1, result[1].Basis);
    }

    [Fact]
    public async Task Resolver_does_not_fallback_to_aon_or_ipa()
    {
        await using var db = CreateContext();
        db.ProjectAonFacts.Add(new ProjectAonFact
        {
            ProjectId = 1,
            AonCost = 40_000_000m,
            CreatedByUserId = "test",
            CreatedOnUtc = DateTime.UtcNow
        });
        db.ProjectIpaFacts.Add(new ProjectIpaFact
        {
            ProjectId = 1,
            IpaCost = 45_000_000m,
            CreatedByUserId = "test",
            CreatedOnUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await new ProjectSupplyOrderValueResolver(db).ResolveAsync([1]);

        Assert.False(result[1].IsAvailable);
        Assert.Equal(ProjectSupplyOrderValueBasis.None, result[1].Basis);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
