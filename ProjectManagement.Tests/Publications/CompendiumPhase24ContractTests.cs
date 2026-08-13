using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase24ContractTests
{
    [Fact]
    public void Planner_ProducesExplicitCoverIndexProjectsAndBackCover()
    {
        var context = Context(
            Category("AI", Project(1, "ASTRAE"), Project(2, "Swarm Drones")),
            Category("AR / VR", Project(3, "VR MaRS"), Project(4, "VR AD")));

        var plan = new CompendiumPagePlanner().Plan(context);

        Assert.Equal(7, plan.ExpectedPageCount);
        Assert.Equal(CompendiumPageKind.Cover, plan.Pages[0].Kind);
        Assert.Equal(CompendiumPageKind.Index, plan.Pages[1].Kind);
        Assert.Equal(CompendiumPageKind.BackCover, plan.Pages[^1].Kind);
        Assert.Equal(3, plan.ProjectStartPages[1]);
        Assert.Equal(6, plan.ProjectStartPages[4]);
        Assert.Equal(4, plan.ProjectStartPages.Count);
    }

    [Fact]
    public void Planner_LongDescriptionCreatesContinuationPagesWithoutChangingProjectIdentity()
    {
        var description = string.Join("\n\n", Enumerable.Repeat(
            "This is a deliberately long project-description paragraph used to verify deterministic Compendium continuation planning without emergency body-font shrinking.",
            80));
        var project = Project(7, "Long narrative project") with { DescriptionMarkdown = description };
        var context = Context(Category("AI", project));

        var plan = new CompendiumPagePlanner().Plan(context);
        var projectPages = plan.Pages.Where(page => page.Project?.ProjectId == 7).ToArray();

        Assert.True(projectPages.Length > 1);
        Assert.Equal(CompendiumPageKind.Project, projectPages[0].Kind);
        Assert.All(projectPages.Skip(1), page => Assert.Equal(CompendiumPageKind.ProjectContinuation, page.Kind));
        Assert.All(projectPages, page => Assert.Equal("Long narrative project", page.Project?.ProjectName));
    }

    [Fact]
    public void Planner_LargeCatalogueCanUseMultipleIndexPages()
    {
        var projects = Enumerable.Range(1, 40)
            .Select(id => Project(id, $"Project {id:00}"))
            .ToArray();
        var context = Context(Category("AI", projects));

        var plan = new CompendiumPagePlanner().Plan(context);

        Assert.True(plan.IndexPageCount >= 2);
        Assert.Equal(40, plan.ProjectStartPages.Count);
        Assert.Equal(CompendiumPageKind.Cover, plan.Pages.First().Kind);
        Assert.Equal(CompendiumPageKind.BackCover, plan.Pages.Last().Kind);
    }

    [Fact]
    public void Planner_ProjectWithoutPhotoUsesTextLedLayout()
    {
        var context = Context(Category("Robotics", Project(12, "No photo project")));

        var plan = new CompendiumPagePlanner().Plan(context);
        var projectPage = Assert.Single(plan.Pages.Where(page => page.Kind == CompendiumPageKind.Project));

        Assert.Equal(CompendiumProjectLayoutVariant.NoPhoto, projectPage.ProjectLayout);
    }

    private static CompendiumPdfReportContext Context(params CompendiumPdfCategorySection[] categories)
        => new(
            "SDD Simulators Compendium",
            "Detailed Project Reference",
            "Simulator Development Division",
            "Simulator Development Division",
            null,
            DateTimeOffset.UtcNow,
            categories,
            ShowMissingPhotoPlaceholder: false)
        {
            Edition = "Capability Edition · 2026"
        };

    private static CompendiumPdfCategorySection Category(
        string name,
        params CompendiumPdfProjectSection[] projects)
        => new(name, projects);

    private static CompendiumPdfProjectSection Project(int id, string name)
        => new(
            id,
            name,
            null,
            "AI",
            "2026",
            "Not recorded",
            "Not recorded",
            null,
            "Short project description.",
            null,
            PhotoWasSelected: false)
        {
            LifecycleDisplay = "Ongoing",
            ProjectCategoryDisplay = "Other R&D Projects",
            ProliferationAvailability = null
        };
}
