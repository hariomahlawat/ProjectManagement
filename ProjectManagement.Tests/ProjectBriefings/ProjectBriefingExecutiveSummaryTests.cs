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
    public void Compose_LeavesProliferationOffAnOngoingProjectWithNone()
    {
        // Owner, 6 Nov 2026: a proliferation cost is expected only once a project is completed,
        // so an ongoing project with none recorded shows no "Not recorded" proliferation figure.
        var (content, _) = CreateComposer().Compose(BuildData());

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.Select(SlideText).ToArray();
        var ongoing = Assert.Single(slides.Where(text => text.Contains("AURA", StringComparison.Ordinal) && !text.Contains("ASTRAE", StringComparison.Ordinal)));
        var completed = Assert.Single(slides.Where(text => text.Contains("ASTRAE", StringComparison.Ordinal) && !text.Contains("AURA", StringComparison.Ordinal)));

        Assert.DoesNotContain("PROLIFERATION", ongoing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COST (R&D)", ongoing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PROLIFERATION", completed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("₹18.5 Lakh", completed, StringComparison.Ordinal);
    }

    [Fact]
    public void BriefPagination_ReservesNoRoomForAProliferationCardTheSlideLeavesOut()
    {
        // Codex review on #2938: pagination must count the cost cards the slide draws.
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 1,
            DeckName = "Pagination",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.ProliferationOnly,
            StandardSlideOptions = ProjectBriefingStandardSlideOptions.Default with
            {
                ShowPresentStage = false,
                ShowPresentStatus = false
            },
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
        ProjectBriefingPresentationProject Ongoing(bool recorded, string brief) => new()
        {
            ProjectId = 1,
            ProjectName = "AURA",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            CostRd = ProjectBriefingCostValue.Missing(),
            ProliferationCost = recorded
                ? new ProjectBriefingCostValue(1_000_000m, ProjectBriefingCostBasis.Proliferation, "₹10 Lakh", "Proliferation")
                : ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
            ProjectBrief = brief,
            BriefDescription = "Capability overview.",
            SortOrder = 1
        };

        var sentence = "The trainer reproduces the full engagement sequence for the crew in realistic conditions. ";
        var fewer = false;
        for (var sentences = 4; sentences <= 60; sentences++)
        {
            var brief = string.Concat(Enumerable.Repeat(sentence, sentences));
            var slidesWithout = ProjectBriefingSlideComposer.ProjectBriefSlideCount(data, Ongoing(recorded: false, brief));
            var slidesWith = ProjectBriefingSlideComposer.ProjectBriefSlideCount(data, Ongoing(recorded: true, brief));
            Assert.True(slidesWithout <= slidesWith, $"{sentences} sentences: {slidesWithout} > {slidesWith}");
            fewer |= slidesWithout < slidesWith;
        }

        Assert.True(fewer, "Some brief length should fit on fewer slides once the strip is not drawn.");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void KeyPointSlide_TracksTheLifecycleSubtitleWhenTheProliferationLineIsLeftOut(bool recorded, bool drawsSubtitle)
    {
        // Codex review on #2938: with no meta line left (proliferation only, stage hidden, an
        // ongoing project without the cost) the slide draws the lifecycle subtitle, and the facts
        // tracking must know so.
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 1,
            DeckName = "Key points",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.CapabilityOverview,
            CostMode = ProjectBriefingCostMode.ProliferationOnly,
            StandardSlideOptions = ProjectBriefingStandardSlideOptions.Default with
            {
                ShowPresentStage = false,
                KeyPoints = ProjectBriefingKeyPointPlacement.CapabilitySlide
            },
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 1,
            ProjectName = "AURA",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            CostRd = ProjectBriefingCostValue.Missing(),
            ProliferationCost = recorded
                ? new ProjectBriefingCostValue(1_000_000m, ProjectBriefingCostBasis.Proliferation, "₹10 Lakh", "Proliferation")
                : ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
            BriefDescription = "A short capability overview.",
            SortOrder = 1
        };

        Assert.Equal(drawsSubtitle, ProjectBriefingSlideComposer.DrawsLifecycleSubtitle(data, project));
    }

    [Theory]
    [InlineData(ProjectLifecycleStatus.Active, false, false)]
    [InlineData(ProjectLifecycleStatus.Active, true, true)]
    [InlineData(ProjectLifecycleStatus.Completed, false, true)]
    [InlineData(ProjectLifecycleStatus.Completed, true, true)]
    public void ProliferationCost_ShowsWhenRecordedOrExpected(ProjectLifecycleStatus status, bool recorded, bool shows)
    {
        var cost = recorded
            ? new ProjectBriefingCostValue(1_000_000m, ProjectBriefingCostBasis.Proliferation, "₹10 Lakh", "Proliferation")
            : ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation);

        Assert.Equal(shows, ProjectBriefingContentGaps.ShowsProliferationCost(status, cost));
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
