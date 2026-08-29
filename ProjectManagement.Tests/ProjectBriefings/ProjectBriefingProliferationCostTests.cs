using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingProliferationCostTests
{
    [Fact]
    public async Task ResolveProliferationCostAsync_ExplicitZero_IsRecordedAndAvailable()
    {
        await using var db = CreateContext();
        db.ProjectProductionCostFacts.Add(new ProjectProductionCostFact
        {
            ProjectId = 42,
            ApproxProductionCost = 0m,
            UpdatedAtUtc = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
            UpdatedByUserId = "user-42"
        });
        await db.SaveChangesAsync();

        var resolver = new ProjectBriefingCostResolver(db);

        var result = await resolver.ResolveProliferationCostAsync(new[] { 42 });

        Assert.True(result.TryGetValue(42, out var value));
        Assert.NotNull(value);
        Assert.True(value!.IsAvailable);
        Assert.Equal(0m, value.AmountInRupees);
        Assert.Equal(ProjectBriefingCostBasis.Proliferation, value.Basis);
        Assert.Equal("₹0", value.DisplayValue);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
