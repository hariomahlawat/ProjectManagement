using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// How one project appears in its deck: its own slides, a table row only, or left out.
public sealed class ProjectBriefingItemPresentationTests
{
    private static readonly ProjectBriefingSlideRole[] ProjectRoles =
    {
        ProjectBriefingSlideRole.ProjectCapability,
        ProjectBriefingSlideRole.ProjectCapabilityContinuation,
        ProjectBriefingSlideRole.ProjectBrief,
        ProjectBriefingSlideRole.ProjectUpdateSheet,
        ProjectBriefingSlideRole.ProjectUpdateSheetContinuation
    };

    [Fact]
    public void DetailedDeck_ListsTableRowOnlyProjectsInATableAfterTheProjectSlides()
    {
        var composition = Composer().ComposeWithOutline(Data(ProjectBriefingPresentationMode.DetailedProjects));

        Assert.Equal(new[] { 1 }, ProjectsWithSlides(composition));
        var table = Assert.Single(composition.Outline, item => item.Role == ProjectBriefingSlideRole.StatusTable);
        Assert.Equal(new[] { 2 }, table.ListedProjectIds);
        var lastProjectSlide = composition.Outline.Last(item => item.ProjectId.HasValue).Number;
        Assert.True(table.Number > lastProjectSlide, "The table follows the project slides.");
    }

    [Fact]
    public void CombinedDeck_KeepsEveryProjectInTheStatusTableAndSlidesOnlyForTheOthers()
    {
        var composition = Composer().ComposeWithOutline(Data(ProjectBriefingPresentationMode.Combined));

        Assert.Equal(new[] { 1 }, ProjectsWithSlides(composition));
        var table = Assert.Single(composition.Outline, item => item.Role == ProjectBriefingSlideRole.StatusTable);
        Assert.Equal(new[] { 1, 2 }, table.ListedProjectIds.OrderBy(id => id));
    }

    [Fact]
    public void DetailedDeck_WithoutTableRowOnlyProjects_AddsNoTable()
    {
        var composition = Composer().ComposeWithOutline(Data(ProjectBriefingPresentationMode.DetailedProjects, tableRowOnly: false));

        Assert.Equal(new[] { 1, 2 }, ProjectsWithSlides(composition));
        Assert.DoesNotContain(composition.Outline, item => item.Role == ProjectBriefingSlideRole.StatusTable);
    }

    [Fact]
    public void UpdateSheetDeck_ListsTableRowOnlyProjectsAfterTheSheets()
    {
        var data = Data(ProjectBriefingPresentationMode.DetailedProjects, layout: ProjectBriefingLayout.ProjectUpdateSheet);

        var composition = Composer().ComposeWithOutline(data);

        Assert.Equal(new[] { 1 }, ProjectsWithSlides(composition));
        var table = Assert.Single(composition.Outline, item => item.Role == ProjectBriefingSlideRole.StatusTable);
        Assert.Equal(new[] { 2 }, table.ListedProjectIds);
    }

    [Fact]
    public void Checks_AskOnlyForWhatATableRowShows_AndSkipLeftOutProjects()
    {
        var settings = new ProjectBriefingContentGapSettings(
            ProjectBriefingLayout.StandardBriefing,
            ProjectBriefingPresentationMode.Combined,
            ProjectBriefingCostMode.CostRdOnly,
            ProjectBriefingNarrativeMode.Both,
            ProjectBriefingStandardSlideOptions.Default,
            ProjectBriefingUpdateSheetOptions.Default);

        var gaps = ProjectBriefingContentGaps.Resolve(
            new[]
            {
                EmptyProject(1, ProjectBriefingItemPresentation.Slides),
                EmptyProject(2, ProjectBriefingItemPresentation.TableRowOnly),
                EmptyProject(3, ProjectBriefingItemPresentation.LeftOut)
            },
            settings);

        Assert.Contains(ProjectBriefingContentGaps.Photo, gaps[1]);
        Assert.Equal(new[] { ProjectBriefingContentGaps.Status, ProjectBriefingContentGaps.CostRd }, gaps[2]);
        Assert.False(gaps.ContainsKey(3));
    }

    [Fact]
    public void LeftOutAndDeletedProjects_AreNotPresented()
    {
        Assert.True(EmptyProject(1, ProjectBriefingItemPresentation.Slides).IsPresented);
        Assert.True(EmptyProject(2, ProjectBriefingItemPresentation.TableRowOnly).IsPresented);
        Assert.False(EmptyProject(3, ProjectBriefingItemPresentation.LeftOut).IsPresented);
        Assert.False(new ProjectBriefingProjectVm { ProjectId = 4, IsDeletedOrArchived = true }.IsPresented);
    }

    private static int[] ProjectsWithSlides(ProjectBriefingComposition composition)
        => composition.Outline
            .Where(item => ProjectRoles.Contains(item.Role) && item.ProjectId.HasValue)
            .Select(item => item.ProjectId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

    private static ProjectBriefingSlideComposer Composer()
        => new(new TestEnvironment(Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot")));

    private static ProjectBriefingPresentationData Data(
        ProjectBriefingPresentationMode mode,
        bool tableRowOnly = true,
        ProjectBriefingLayout layout = ProjectBriefingLayout.StandardBriefing)
        => new()
        {
            DeckId = 700,
            DeckName = "Per-project presentation",
            Layout = layout,
            PresentationMode = mode,
            CostMode = ProjectBriefingCostMode.CostRdOnly,
            NarrativeMode = ProjectBriefingNarrativeMode.CapabilityOverview,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero),
            Projects = new[]
            {
                Project(1, "Slides project", 10, tableRowOnly: false),
                Project(2, "Table row project", 20, tableRowOnly)
            },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 2, OngoingCount = 2 }
        };

    private static ProjectBriefingPresentationProject Project(int id, string name, int sortOrder, bool tableRowOnly)
        => new()
        {
            ProjectId = id,
            ProjectName = name,
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = "DEVP",
            PresentStage = "Development",
            PresentStageOrder = ProjectBriefingStageOrder.Development,
            CostRd = ProjectBriefingCostValue.Missing(),
            ExternalStatus = "Status recorded.",
            BriefDescription = "Capability description.",
            SortOrder = sortOrder,
            TableRowOnly = tableRowOnly
        };

    private static ProjectBriefingProjectVm EmptyProject(int id, ProjectBriefingItemPresentation presentation)
        => new()
        {
            ProjectId = id,
            ProjectName = $"Project {id}",
            Presentation = presentation,
            BriefDescription = "Capability overview not recorded.",
            ProjectBrief = "Project brief not recorded."
        };

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = Path.Combine(contentRootPath, "wwwroot");
            Directory.CreateDirectory(WebRootPath);
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; } = "ProjectManagement.Tests";
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
