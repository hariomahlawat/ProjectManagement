using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using P = DocumentFormat.OpenXml.Presentation;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingExecutiveSummaryTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    public void EstimateCategorySlideCount_IsOneDonutSlideForEitherOrBoth(bool project, bool technical, int expected)
        => Assert.Equal(expected, ProjectBriefingSummaryPlanning.EstimateCategorySlideCount(project, technical));

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

    [Fact]
    public void Movement_SeesAMissingProliferationCostAppearWhenAProjectIsCompleted()
    {
        // Codex review on #2938: an ongoing project without a proliferation cost has none drawn;
        // once completed its slide says "Not recorded", so the library must see the change.
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 1,
            DeckName = "Movement",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.ProliferationOnly,
            IncludePortfolioSummarySlide = false,
            IncludeStageSummary = false,
            StandardSlideOptions = ProjectBriefingStandardSlideOptions.Default with { ShowPresentStage = false, ShowPresentStatus = false },
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 7,
            ProjectName = "AURA",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            PresentStageCode = "DEVP",
            CostRd = ProjectBriefingCostValue.Missing(),
            ProliferationCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
            ProjectBrief = "A short brief.",
            SortOrder = 1
        };
        data = new ProjectBriefingPresentationData
        {
            DeckId = data.DeckId, DeckName = data.DeckName, PresentationMode = data.PresentationMode,
            NarrativeMode = data.NarrativeMode, CostMode = data.CostMode,
            IncludePortfolioSummarySlide = false, IncludeStageSummary = false,
            StandardSlideOptions = data.StandardSlideOptions, GeneratedAtUtc = data.GeneratedAtUtc,
            Projects = new[] { project }
        };
        var generatedFact = ProjectBriefingSlideFacts.FromPresentation(project, data);
        var completedToday = new Dictionary<int, ProjectBriefingCurrentProjectFacts>
        {
            [7] = new(7, "AURA", ProjectLifecycleStatus.Completed, "DEVP", null, null,
                ProjectBriefingCostValue.Missing(), ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA),
                ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation))
        };

        var moved = ProjectBriefingSlideFacts.Compare(
            new ProjectBriefingGeneratedFacts(data.Layout, data.CostMode, new[] { generatedFact }), completedToday);
        Assert.True(moved.HasMoved);

        // A deck generated before this rule kept facts without the lifecycle: not flagged by it.
        var olderFact = generatedFact with { Shown = generatedFact.Shown & ~ProjectBriefingShownFacts.Lifecycle, Stage = null };
        var olderMoved = ProjectBriefingSlideFacts.Compare(
            new ProjectBriefingGeneratedFacts(data.Layout, data.CostMode, new[] { olderFact }), completedToday);
        Assert.False(olderMoved.HasMoved);
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
        Assert.DoesNotContain("Project-category distribution", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_DrawsACategoryDistributionAsANativeDoughnutWithItsFigures()
    {
        var points = new[]
        {
            new ProjectBriefingSummaryPoint("Other R&D Projects", 8),
            new ProjectBriefingSummaryPoint("CoE Mid Term", 6),
            new ProjectBriefingSummaryPoint("CoE Short Term", 5),
            new ProjectBriefingSummaryPoint("CoE", 2)
        };
        var data = BuildData(includeProjectCategorySummary: true, projectCategories: points);

        var (content, slideCount) = CreateComposer().Compose(data);

        ProjectBriefingPresentationIntegrityValidator.Validate(content, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = Assert.Single(Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts.Where(part => SlideText(part).Contains("Project-category distribution", StringComparison.Ordinal)));
        var text = SlideText(slide);
        Assert.Contains("21 projects · 4 categories", text, StringComparison.Ordinal);
        Assert.Contains("is the largest: 8 of 21 projects (38%)", text, StringComparison.Ordinal);
        Assert.Contains("CoE Short Term", text, StringComparison.Ordinal);

        // A real chart: PowerPoint's Edit Data opens its embedded workbook.
        var chart = Assert.Single(slide.ChartParts);
        Assert.NotNull(chart.EmbeddedPackagePart);
        var doughnut = Assert.Single(chart.ChartSpace!.Descendants<C.DoughnutChart>());
        var values = doughnut.Descendants<C.NumericPoint>().Select(point => point.NumericValue!.Text).ToArray();
        Assert.Equal(new[] { "8", "6", "5", "2" }, values);
        Assert.Equal(4, doughnut.Descendants<C.DataPoint>().Count());
    }

    [Fact]
    public void Compose_PutsBothCategoryDistributionsOnOneSlide()
    {
        var data = BuildData(
            includeProjectCategorySummary: true,
            projectCategories: new[] { new ProjectBriefingSummaryPoint("DCD Projects", 1), new ProjectBriefingSummaryPoint("CoE", 1) },
            technicalCategories: new[] { new ProjectBriefingSummaryPoint("AR/ VR", 1), new ProjectBriefingSummaryPoint("Drone", 1) });

        var (content, slideCount) = CreateComposer().Compose(data);

        ProjectBriefingPresentationIntegrityValidator.Validate(content, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        var slide = Assert.Single(slides.Where(part => part.ChartParts.Any()));
        Assert.Equal(2, slide.ChartParts.Count());
        Assert.Contains(ProjectBriefingSlideComposer.CombinedCategoryTitle, SlideText(slide), StringComparison.Ordinal);
        Assert.DoesNotContain(slides, part => SlideText(part).Contains("Technical-category distribution", StringComparison.Ordinal));

        // The builder's preview draws the rings too.
        var preview = ProjectBriefingSlidePreviewRenderer.Render(content, DateTimeOffset.UtcNow);
        var html = Assert.Single(preview.Slides.Where(item => item.Html.Contains("pbd-slide-chart", StringComparison.Ordinal))).Html;
        Assert.Equal(2, CountOf(html, "class=\"pbd-slide-chart\""));
        Assert.Equal(4, CountOf(html, "<path "));
    }

    [Fact]
    public void Compose_GathersTheSmallestCategoriesBeyondNineSlices()
    {
        var points = Enumerable.Range(1, 12)
            .Select(index => new ProjectBriefingSummaryPoint($"Category {index:00}", 20 - index))
            .Append(new ProjectBriefingSummaryPoint("Not categorised", 4))
            .ToArray();
        var data = BuildData(includeProjectCategorySummary: true, projectCategories: points);

        var (content, _) = CreateComposer().Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = Assert.Single(Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.Where(part => part.ChartParts.Any()));
        var labels = Assert.Single(slide.ChartParts).ChartSpace!
            .Descendants<C.CategoryAxisData>()
            .SelectMany(category => category.Descendants<C.StringPoint>())
            .Select(point => point.NumericValue!.Text)
            .ToArray();
        // Seven named categories, the other five together, then the uncategorised last.
        Assert.Equal(ProjectBriefingSlideComposer.MaximumCategorySlices, labels.Length);
        Assert.Equal("Other categories (5)", labels[^2]);
        Assert.Equal("Not categorised", labels[^1]);
    }

    [Fact]
    public void Summary_CountsProjectCategoriesByTheirParent()
    {
        // Owner, 17 Nov 2026: the project-category distribution counts parent categories only.
        ProjectBriefingPresentationProject Project(int id, string category, string? parent) => new()
        {
            ProjectId = id,
            ProjectName = $"Project {id}",
            ProjectCategory = category,
            ParentProjectCategory = parent,
            CostRd = ProjectBriefingCostValue.Missing(),
            ProliferationCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation)
        };

        var summary = ProjectBriefingDataService.BuildPresentationSummary(new[]
        {
            Project(1, "CoE Mid Term", "CoE"),
            Project(2, "CoE Short Term", "CoE"),
            Project(3, "CoE", "CoE"),
            Project(4, "DCD Projects", "DCD Projects"),
            Project(5, "Legacy", null)
        });

        Assert.Equal(
            new[] { ("CoE", 3), ("DCD Projects", 1), ("Legacy", 1) },
            summary.ProjectCategorySummary.Select(point => (point.Label, point.Count)).ToArray());
    }

    [Fact]
    public void Compose_FitsLongCategoryNamesInsideTheirLegendRows()
    {
        // Codex review on #3408: nine long names in a half-slide legend must not be clipped.
        // Long names force the smallest size; short ones beside them must still hold their counts.
        ProjectBriefingSummaryPoint[] Long(string prefix) => Enumerable.Range(1, 9)
            .Select(index => new ProjectBriefingSummaryPoint(
                index % 3 == 0
                    ? $"{prefix} {index}"
                    : $"{prefix} {index} with a very long descriptive category name that runs on for many words across the legend",
                20 - index))
            .ToArray();
        var data = BuildData(includeProjectCategorySummary: true, projectCategories: Long("Project"), technicalCategories: Long("Technical"));

        var (content, _) = CreateComposer().Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = Assert.Single(Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.Where(part => part.ChartParts.Any()));
        var names = slide.Slide.Descendants<P.Shape>()
            .Where(shape => shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value?.EndsWith(" name", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(18, names.Length);
        foreach (var shape in names)
        {
            var text = string.Concat(shape.Descendants<A.Text>().Select(node => node.Text));
            var size = shape.Descendants<A.RunProperties>().First().FontSize!.Value / 100d;
            var extents = shape.ShapeProperties!.Transform2D!.Extents!;
            var width = extents.Cx!.Value / 914400d;
            var height = extents.Cy!.Value / 914400d;
            var lines = ProjectManagement.Services.Analytics.Presentation.AnalyticsSlideFrame.LineCount(text, width - .12, size);
            Assert.True(lines * size * 1.22 / 72d <= height + .01, $"{text} ({lines} lines at {size} pt) needs more than {height:0.00} in.");
            Assert.True(size >= 9.0);
        }

        // Every count fits its row (the text box keeps .05 in of insets top and bottom).
        foreach (var shape in slide.Slide.Descendants<P.Shape>()
            .Where(shape => shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value?.EndsWith(" count", StringComparison.Ordinal) == true))
        {
            var size = shape.Descendants<A.RunProperties>().First().FontSize!.Value / 100d;
            var height = shape.ShapeProperties!.Transform2D!.Extents!.Cy!.Value / 914400d;
            Assert.True((size * 1.22 / 72d) + .05 <= height + .005, $"A {size} pt count does not fit a {height:0.00} in row.");
        }
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static ProjectBriefingSlideComposer CreateComposer()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        return new ProjectBriefingSlideComposer(new TestEnvironment(root));
    }

    private static ProjectBriefingPresentationData BuildData(
        bool includeProjectCategorySummary = false,
        IReadOnlyList<ProjectBriefingSummaryPoint>? projectCategories = null,
        IReadOnlyList<ProjectBriefingSummaryPoint>? technicalCategories = null)
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
            IncludeTechnicalCategorySummary = technicalCategories is not null,
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
                ProjectCategorySummary = projectCategories ?? Array.Empty<ProjectBriefingSummaryPoint>(),
                TechnicalCategorySummary = technicalCategories ?? Array.Empty<ProjectBriefingSummaryPoint>()
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
