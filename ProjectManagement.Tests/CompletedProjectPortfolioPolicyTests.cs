using ProjectManagement.Models;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class CompletedProjectPortfolioPolicyTests
{
    [Fact]
    public void FullyReady_RequiresCurrentTechnologyAvailabilityAndResolvedTot()
    {
        var item = CreateCompleteItem();

        Assert.True(CompletedProjectPortfolioPolicy.IsFullyReady(item));

        item.TotStatus = ProjectTotStatus.InProgress;
        Assert.False(CompletedProjectPortfolioPolicy.IsFullyReady(item));
        Assert.True(CompletedProjectPortfolioPolicy.IsAvailableButBlocked(item));
        Assert.Contains("ToT in progress", CompletedProjectPortfolioPolicy.GetReadinessBlockers(item));
    }

    [Theory]
    [InlineData(ProjectTechStatusCodes.Outdated)]
    [InlineData(ProjectTechStatusCodes.Obsolete)]
    public void TechnologyAction_IncludesOutdatedAndObsolete(string status)
    {
        var item = CreateCompleteItem();
        item.TechStatus = status;

        Assert.True(CompletedProjectPortfolioPolicy.RequiresTechnologyAction(item));
        Assert.True(CompletedProjectPortfolioPolicy.MatchesPortfolioStatus(
            item,
            CompletedProjectPortfolioStatusCodes.TechnologyAction));
    }

    [Fact]
    public void DataQuality_SeparatesCriticalAndSupplementaryFields()
    {
        var item = CreateCompleteItem();
        item.TechStatus = null;
        item.TotStatus = null;
        item.ApproxProductionCost = null;
        item.LatestLpp = null;

        var critical = CompletedProjectPortfolioPolicy.GetCriticalMissingFields(item);
        var supplementary = CompletedProjectPortfolioPolicy.GetSupplementaryMissingFields(item);

        Assert.Equal(new[] { "Technology assessment", "ToT status" }, critical);
        Assert.Equal(new[] { "Production cost", "Latest LPP" }, supplementary);
        Assert.Equal(2, CompletedProjectPortfolioPolicy.GetCriticalMissingCount(item));
        Assert.Equal(2, CompletedProjectPortfolioPolicy.GetSupplementaryMissingCount(item));
    }

    [Fact]
    public void Overview_UsesSharedPolicyAndPrioritisesRecentReadyProjects()
    {
        var older = CreateCompleteItem(1, "Older", 2020, 80m);
        var newer = CreateCompleteItem(2, "Newer", 2025, 60m);
        var blocked = CreateCompleteItem(3, "Blocked", 2026, 100m);
        blocked.TotStatus = ProjectTotStatus.NotStarted;
        var incomplete = CreateCompleteItem(4, "Incomplete", 2024, 50m);
        incomplete.TechStatus = null;

        var overview = CompletedProjectsPortfolioOverview.Build(
            new[] { older, newer, blocked, incomplete },
            currentYear: 2026,
            queueSize: 3);

        Assert.Equal(4, overview.TotalCount);
        Assert.Equal(4, overview.AvailableCount);
        Assert.Equal(2, overview.FullyReadyCount);
        Assert.Equal(2, overview.AvailableBlockedCount);
        Assert.Equal(1, overview.TotActionPendingCount);
        Assert.Equal(1, overview.TechnologyAssessmentPendingCount);
        Assert.Equal("Newer", overview.ReadyProjects[0].Name);
    }

    private static CompletedProjectSummaryDto CreateCompleteItem(
        int id = 1,
        string name = "Project",
        int completedYear = 2025,
        decimal developmentCost = 120m) => new()
    {
        ProjectId = id,
        Name = name,
        CompletedYear = completedYear,
        RdCostLakhs = developmentCost,
        ApproxProductionCost = 40m,
        TechStatus = ProjectTechStatusCodes.Current,
        AvailableForProliferation = true,
        TotStatus = ProjectTotStatus.Completed,
        LatestLpp = new LatestLppViewModel
        {
            Amount = 45m,
            Date = new DateOnly(2026, 1, 1)
        }
    };
}
