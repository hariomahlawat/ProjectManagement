using System;
using System.Collections.Generic;
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

// The block list (PR 3 of the blocks plan): what each deck's settings give, and that the composer
// follows a given list (its order, switched-off blocks) rather than a fixed sequence.
public sealed class ProjectBriefingDeckBlocksTests
{
    private static readonly IReadOnlyList<ProjectBriefingAdditionalSlideType> Institution = new[]
    {
        ProjectBriefingAdditionalSlideType.FfcGlobalFootprint,
        ProjectBriefingAdditionalSlideType.RoleAndCharter,
        ProjectBriefingAdditionalSlideType.InstitutionalProfile
    };

    [Fact]
    public void StandardSettings_GiveTheBlocksInTheOrderDecksHaveAlwaysHad()
    {
        var blocks = ProjectBriefingDeckBlocks.FromSettings(
            ProjectBriefingLayout.StandardBriefing,
            ProjectBriefingPresentationMode.Combined,
            includeCoverSlide: true,
            includePortfolioSummarySlide: true,
            includeStageSummary: true,
            includeProjectCategorySummary: true,
            includeTechnicalCategorySummary: false,
            Institution);

        Assert.Equal(
            new[]
            {
                ProjectBriefingDeckBlockType.Cover,
                ProjectBriefingDeckBlockType.RoleAndCharter,
                ProjectBriefingDeckBlockType.InstitutionalProfile,
                ProjectBriefingDeckBlockType.PortfolioSummary,
                ProjectBriefingDeckBlockType.StageSummary,
                ProjectBriefingDeckBlockType.ProjectCategorySummary,
                ProjectBriefingDeckBlockType.StatusTable,
                ProjectBriefingDeckBlockType.ProjectSlides,
                ProjectBriefingDeckBlockType.FfcGlobalFootprint
            },
            blocks.Select(block => block.Type));
    }

    [Theory]
    [InlineData(ProjectBriefingPresentationMode.ExecutiveTable, true, false)]
    [InlineData(ProjectBriefingPresentationMode.DetailedProjects, false, true)]
    [InlineData(ProjectBriefingPresentationMode.Combined, true, true)]
    public void WhatTheDeckShows_BecomesTheStatusTableAndProjectSlidesBlocks(
        ProjectBriefingPresentationMode mode,
        bool statusTable,
        bool projectSlides)
    {
        var types = ProjectBriefingDeckBlocks.FromSettings(
                ProjectBriefingLayout.StandardBriefing, mode, false, false, false, false, false,
                Array.Empty<ProjectBriefingAdditionalSlideType>())
            .Select(block => block.Type)
            .ToArray();

        Assert.Equal(statusTable, types.Contains(ProjectBriefingDeckBlockType.StatusTable));
        Assert.Equal(projectSlides, types.Contains(ProjectBriefingDeckBlockType.ProjectSlides));
    }

    [Fact]
    public void UpdateSheets_IgnoreTheStandardSummariesAndAlwaysHaveProjectSlides()
    {
        var types = ProjectBriefingDeckBlocks.FromSettings(
                ProjectBriefingLayout.ProjectUpdateSheet,
                ProjectBriefingPresentationMode.ExecutiveTable,
                includeCoverSlide: false,
                includePortfolioSummarySlide: true,
                includeStageSummary: true,
                includeProjectCategorySummary: true,
                includeTechnicalCategorySummary: true,
                Array.Empty<ProjectBriefingAdditionalSlideType>())
            .Select(block => block.Type);

        Assert.Equal(new[] { ProjectBriefingDeckBlockType.PortfolioSummary, ProjectBriefingDeckBlockType.ProjectSlides }, types);
    }

    [Fact]
    public void EveryBlock_HasAZone()
    {
        foreach (var type in Enum.GetValues<ProjectBriefingDeckBlockType>())
        {
            Assert.True(Enum.IsDefined(ProjectBriefingDeckBlocks.ZoneOf(type)));
        }

        Assert.Equal(ProjectBriefingDeckZone.Overview, ProjectBriefingDeckBlocks.ZoneOf(ProjectBriefingDeckBlockType.StatusTable));
        Assert.Equal(ProjectBriefingDeckZone.Closing, ProjectBriefingDeckBlocks.ZoneOf(ProjectBriefingDeckBlockType.FfcGlobalFootprint));
    }

    [Fact]
    public void GivenBlocks_TheComposerFollowsTheirOrder()
    {
        var data = Data(new[]
        {
            new ProjectBriefingDeckBlock(ProjectBriefingDeckBlockType.StatusTable),
            new ProjectBriefingDeckBlock(ProjectBriefingDeckBlockType.StageSummary),
            new ProjectBriefingDeckBlock(ProjectBriefingDeckBlockType.ProjectSlides)
        });

        var roles = Composer().ComposeWithOutline(data).Outline.Select(item => item.Role).Distinct().ToArray();

        Assert.Equal(ProjectBriefingSlideRole.StatusTable, roles[0]);
        Assert.Equal(ProjectBriefingSlideRole.StageSummary, roles[1]);
        Assert.Equal(ProjectBriefingSlideRole.Closing, roles[^1]);
        Assert.True(Array.IndexOf(roles, ProjectBriefingSlideRole.ProjectCapability) > 1);
    }

    [Fact]
    public void ASwitchedOffBlock_DrawsNothing_AndWithoutAStatusTableTableRowsFollowTheProjects()
    {
        var data = Data(new[]
        {
            new ProjectBriefingDeckBlock(ProjectBriefingDeckBlockType.Cover, Enabled: false),
            new ProjectBriefingDeckBlock(ProjectBriefingDeckBlockType.StatusTable, Enabled: false),
            new ProjectBriefingDeckBlock(ProjectBriefingDeckBlockType.ProjectSlides)
        });

        var outline = Composer().ComposeWithOutline(data).Outline;

        Assert.DoesNotContain(outline, item => item.Role == ProjectBriefingSlideRole.Cover);
        var table = Assert.Single(outline, item => item.Role == ProjectBriefingSlideRole.StatusTable);
        Assert.Equal(new[] { 2 }, table.ListedProjectIds);
        Assert.True(table.Number > outline.Last(item => item.Role == ProjectBriefingSlideRole.ProjectCapability).Number);
    }

    private static ProjectBriefingPresentationData Data(IReadOnlyList<ProjectBriefingDeckBlock> blocks)
        => new()
        {
            DeckId = 901,
            DeckName = "Blocks",
            Layout = ProjectBriefingLayout.StandardBriefing,
            PresentationMode = ProjectBriefingPresentationMode.Combined,
            CostMode = ProjectBriefingCostMode.CostRdOnly,
            NarrativeMode = ProjectBriefingNarrativeMode.CapabilityOverview,
            IncludeCoverSlide = true,
            IncludePortfolioSummarySlide = true,
            IncludeStageSummary = true,
            AdditionalSlideOrder = Array.Empty<ProjectBriefingAdditionalSlideType>(),
            Blocks = blocks,
            GeneratedAtUtc = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero),
            Projects = new[]
            {
                Project(1, "Slides project", 10, tableRowOnly: false),
                Project(2, "Table row project", 20, tableRowOnly: true)
            },
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 2,
                OngoingCount = 2,
                StageSummary = new[] { new ProjectBriefingSummaryPoint("Development", 2, 1) },
                OngoingStageSummary = new[] { new ProjectBriefingSummaryPoint("Development", 2, 1) }
            }
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

    private static ProjectBriefingSlideComposer Composer()
        => new(new TestEnvironment(Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot")));

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
