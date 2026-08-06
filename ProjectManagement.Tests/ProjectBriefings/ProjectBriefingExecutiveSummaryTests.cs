using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingExecutiveSummaryTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(12, 1)]
    [InlineData(13, 2)]
    [InlineData(25, 3)]
    public void EstimateCategorySlideCount_UsesSharedRules(int categories, int expected)
        => Assert.Equal(expected, ProjectBriefingSummaryPlanning.EstimateCategorySlideCount(categories));

    [Fact]
    public void PaginateCategories_BalancesThirteenAsSevenAndSix()
    {
        var points = Enumerable.Range(1, 13)
            .Select(index => new ProjectBriefingSummaryPoint($"Category {index:00}", 14 - index))
            .ToArray();

        var pages = ProjectBriefingSummaryPlanning.PaginateCategories(points);

        Assert.Equal(2, pages.Count);
        Assert.Equal(7, pages[0].Count);
        Assert.Equal(6, pages[1].Count);
    }

    [Fact]
    public void Compose_PortfolioUsesRecordedCostTerminologyAndCoverageBars()
    {
        var composer = CreateComposer();
        var (content, _) = composer.Compose(BuildData());

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        var portfolio = Assert.Single(slides.Where(slide =>
            SlideText(slide).Contains("Portfolio at a glance", StringComparison.Ordinal)));
        var text = SlideText(portfolio);

        Assert.Contains("RECORDED R&D COST", text, StringComparison.Ordinal);
        Assert.Contains("RECORDED PROLIFERATION COST", text, StringComparison.Ordinal);
        Assert.Contains("2 of 2 projects recorded", text, StringComparison.Ordinal);
        Assert.Contains("1 of 2 projects recorded", text, StringComparison.Ordinal);
        Assert.NotNull(ShapeByName(portfolio, "RECORDED R&D COST coverage fill"));
        Assert.NotNull(ShapeByName(portfolio, "RECORDED PROLIFERATION COST coverage fill"));
    }

    [Fact]
    public void Compose_SuppressesSingleProjectCategorySummary()
    {
        var composer = CreateComposer();
        var data = BuildData(
            includeProjectCategorySummary: true,
            projectCategories: new[] { new ProjectBriefingSummaryPoint("CoE", 2) });

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var text = string.Join("\n", Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts.Select(SlideText));
        Assert.DoesNotContain("Project-category summary", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_RendersFourCategoryRankedDistribution()
    {
        var composer = CreateComposer();
        var points = new[]
        {
            new ProjectBriefingSummaryPoint("Other R&D Projects", 8),
            new ProjectBriefingSummaryPoint("CoE Mid Term", 6),
            new ProjectBriefingSummaryPoint("CoE Short Term", 5),
            new ProjectBriefingSummaryPoint("CoE", 2)
        };
        var data = BuildData(includeProjectCategorySummary: true, projectCategories: points);

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = Assert.Single(Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts.Where(part => SlideText(part).Contains("Project-category summary", StringComparison.Ordinal)));
        Assert.Contains("2 SELECTED PROJECTS · 4 CATEGORIES", SlideText(slide), StringComparison.Ordinal);
        Assert.Equal(4, slide.Slide.Descendants<P.Shape>().Count(shape =>
            shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value?.EndsWith(
                "category bar",
                StringComparison.Ordinal) == true));
    }

    private static ProjectBriefingSlideComposer CreateComposer()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        return new ProjectBriefingSlideComposer(new TestEnvironment(root));
    }

    private static ProjectBriefingPresentationData BuildData(
        bool includeProjectCategorySummary = false,
        IReadOnlyList<ProjectBriefingSummaryPoint>? projectCategories = null)
    {
        var projects = new[]
        {
            new ProjectBriefingPresentationProject
            {
                ProjectId = 1,
                ProjectName = "AURA",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                LifecycleDisplay = "Ongoing",
                PresentStage = "Acceptance of Necessity",
                PresentStageOrder = ProjectBriefingStageOrder.AcceptanceOfNecessity,
                CostRd = new ProjectBriefingCostValue(39_530_000m, ProjectBriefingCostBasis.AoN, "₹3.95 Cr", "AoN"),
                ProliferationCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
                ExternalStatus = "Under consideration.",
                BriefDescription = "Capability overview.",
                SortOrder = 1
            },
            new ProjectBriefingPresentationProject
            {
                ProjectId = 2,
                ProjectName = "ASTRAE",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                LifecycleDisplay = "Completed",
                PresentStage = "Completed",
                PresentStageOrder = ProjectBriefingStageOrder.Completed,
                CostRd = new ProjectBriefingCostValue(28_000_000m, ProjectBriefingCostBasis.L1, "₹2.8 Cr", "L1"),
                ProliferationCost = new ProjectBriefingCostValue(1_850_000m, ProjectBriefingCostBasis.Proliferation, "₹18.5 Lakh", "Proliferation"),
                ExternalStatus = "Completed.",
                BriefDescription = "Capability overview.",
                SortOrder = 2
            }
        };

        return new ProjectBriefingPresentationData
        {
            DeckId = 1,
            DeckName = "Executive Summary Test",
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = true,
            IncludeStageSummary = false,
            IncludeProjectCategorySummary = includeProjectCategorySummary,
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.CapabilityOverview,
            CostMode = ProjectBriefingCostMode.Both,
            Projects = projects,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 2,
                OngoingCount = 1,
                CompletedCount = 1,
                TotalCostRdInRupees = 67_530_000m,
                CostRdRecordedCount = 2,
                TotalProliferationCostInRupees = 1_850_000m,
                ProliferationCostRecordedCount = 1,
                ProjectCategorySummary = projectCategories ?? Array.Empty<ProjectBriefingSummaryPoint>()
            }
        };
    }

    private static string SlideText(SlidePart slide)
        => string.Join(" ", slide.Slide.Descendants<A.Text>().Select(node => node.Text));

    private static P.Shape ShapeByName(SlidePart slide, string name)
        => Assert.Single(slide.Slide.Descendants<P.Shape>().Where(shape => string.Equals(
            shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value,
            name,
            StringComparison.Ordinal)));

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string root)
        {
            ContentRootPath = root;
            WebRootPath = Path.Combine(root, "wwwroot");
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
