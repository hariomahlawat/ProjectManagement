using ProjectManagement.Models;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Tests;

public sealed class ProjectPortfolioPresentationVmTests
{
    [Fact]
    public void Create_UsesOneMissingFactsCollectionForCountAndPercentage()
    {
        var project = new Project
        {
            Name = "Project",
            CategoryId = 1,
            TechnicalCategoryId = 2,
            HodUserId = "hod",
            LeadPoUserId = "po",
            SponsoringLineDirectorateId = 3
        };

        var presentation = ProjectPortfolioPresentationVm.Create(
            project,
            new TimelineVm(),
            hasBackfill: false);

        Assert.Equal(
            new[] { "project type", "sponsoring unit", "project description" },
            presentation.MissingProfileFacts);
        Assert.Equal(67, presentation.CompletenessPercent);
        Assert.Equal("3 recommended details missing", presentation.ProfileCompletenessDetail);
    }
}
