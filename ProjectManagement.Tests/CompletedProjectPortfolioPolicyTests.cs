using ProjectManagement.Models;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class CompletedProjectPortfolioPolicyTests
{
    [Fact]
    public void ProliferationAssessmentPending_MatchesOnlyUnrecordedDecisions()
    {
        var item = CreateCompleteItem();

        Assert.False(CompletedProjectPortfolioPolicy.IsProliferationAssessmentPending(item));

        item.AvailableForProliferation = null;

        Assert.True(CompletedProjectPortfolioPolicy.IsProliferationAssessmentPending(item));
        Assert.True(CompletedProjectPortfolioPolicy.MatchesPortfolioStatus(
            item,
            CompletedProjectPortfolioStatusCodes.ProliferationAssessmentPending));
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
    public void ActionItems_KeepTechnologyProliferationAndTotActionsSeparate()
    {
        var item = CreateCompleteItem();
        item.TechStatus = ProjectTechStatusCodes.Outdated;
        item.AvailableForProliferation = null;
        item.TotStatus = ProjectTotStatus.NotStarted;

        var actions = CompletedProjectPortfolioPolicy.GetActionItems(item);

        Assert.Contains("Review technology refresh requirements", actions);
        Assert.Contains("Record the proliferation decision", actions);
        Assert.Contains("Initiate the required ToT action", actions);
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
        Assert.Equal(new[] { "Proliferation cost", "Latest LPP" }, supplementary);
        Assert.Equal(2, CompletedProjectPortfolioPolicy.GetCriticalMissingCount(item));
        Assert.Equal(2, CompletedProjectPortfolioPolicy.GetSupplementaryMissingCount(item));
    }

    [Fact]
    public void Overview_PrioritisesAvailabilityAndIndependentActionQueues()
    {
        var olderAvailable = CreateCompleteItem(1, "Older available", 2020, 80m);
        var newerAvailable = CreateCompleteItem(2, "Newer available", 2025, 60m);
        var totAction = CreateCompleteItem(3, "ToT action", 2026, 100m);
        totAction.TotStatus = ProjectTotStatus.NotStarted;
        var pendingDecision = CreateCompleteItem(4, "Pending decision", 2024, 50m);
        pendingDecision.AvailableForProliferation = null;

        var overview = CompletedProjectsPortfolioOverview.Build(
            new[] { olderAvailable, newerAvailable, totAction, pendingDecision },
            currentYear: 2026,
            queueSize: 3);

        Assert.Equal(4, overview.TotalCount);
        Assert.Equal(3, overview.AvailableCount);
        Assert.Equal(1, overview.AvailabilityPendingCount);
        Assert.Equal(1, overview.TotActionPendingCount);
        Assert.Equal("ToT action", overview.AvailableProjects[0].Name);
        Assert.Contains(overview.AvailabilityPendingProjects, x => x.ProjectId == pendingDecision.ProjectId);
        Assert.Contains(overview.TotActionProjects, x => x.ProjectId == totAction.ProjectId);
    }


    [Fact]
    public void RepeatBuild_TotIsNotApplicableAndDoesNotCreateActionOrCriticalGap()
    {
        var item = CreateCompleteItem();
        item.IsBuild = true;
        item.TotStatus = null;

        Assert.False(CompletedProjectPortfolioPolicy.HasTotActionPending(item));
        Assert.DoesNotContain("ToT status", CompletedProjectPortfolioPolicy.GetCriticalMissingFields(item));
        Assert.DoesNotContain(
            CompletedProjectPortfolioPolicy.GetActionItems(item),
            action => action.Contains("ToT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Not applicable", CompletedProjectPortfolioPolicy.GetTotLabel(item.TotStatus, item.IsBuild));
    }

    [Fact]
    public void Overview_DoesNotCountLegacyRepeatBuildTotAsCompleted()
    {
        var original = CreateCompleteItem(1, "Original", 2025, 100m);
        var repeatBuild = CreateCompleteItem(2, "Repeat build", 2025, 100m);
        repeatBuild.IsBuild = true;
        repeatBuild.TotStatus = ProjectTotStatus.Completed;

        var overview = CompletedProjectsPortfolioOverview.Build(
            new[] { original, repeatBuild },
            currentYear: 2026);

        Assert.Equal(1, overview.TotCompletedCount);
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
