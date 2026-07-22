using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingSlideComposerTests
{
    [Fact]
    public void Compose_CreatesOpenableEditableWidescreenDeck()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));

        var (content, slideCount) = composer.Compose(BuildData());

        Assert.True(content.Length > 10_000);
        Assert.Equal(7, slideCount);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var presentationPart = Assert.IsType<PresentationPart>(document.PresentationPart);
        var slides = presentationPart.SlideParts.ToArray();
        Assert.Equal(slideCount, slides.Length);
        Assert.Equal(12192000, presentationPart.Presentation.SlideSize?.Cx?.Value);
        Assert.Equal(6858000, presentationPart.Presentation.SlideSize?.Cy?.Value);

        var text = string.Join("\n", slides
            .SelectMany(slide => slide.Slide.Descendants<A.Text>())
            .Select(node => node.Text));
        Assert.Contains("QUARTERLY COMMAND REVIEW", text, StringComparison.Ordinal);
        Assert.Contains("COST (R&D)", text, StringComparison.Ordinal);
        Assert.Contains("PROLIFERATION COST", text, StringComparison.Ordinal);
        Assert.Contains("Latest external status for AURA", text, StringComparison.Ordinal);
        Assert.Contains("Stage-wise summary", text, StringComparison.Ordinal);
        Assert.Contains("Stage-wise project distribution", text, StringComparison.Ordinal);
        Assert.Contains("PRESENT STATUS", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PROJECT POSITION", text, StringComparison.Ordinal);
        Assert.Contains("CAPABILITY OVERVIEW", text, StringComparison.Ordinal);

        Assert.DoesNotContain("reverse workflow order", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bars are native editable PowerPoint shapes", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Cost (R&D) resolves L1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("STATUS: LATEST EXTERNAL REMARK ONLY", text, StringComparison.Ordinal);

        var nativeTables = slides
            .SelectMany(slide => slide.Slide.Descendants<A.Table>())
            .Count();
        Assert.True(nativeTables >= 2, "The stage and executive project tables must remain native editable PowerPoint tables.");

        var firstTableXml = slides
            .SelectMany(slide => slide.Slide.Descendants<A.Table>())
            .First()
            .OuterXml;
        Assert.Contains("marL=\"100584\"", firstTableXml, StringComparison.Ordinal);
        Assert.Contains("marR=\"100584\"", firstTableXml, StringComparison.Ordinal);
    }


    [Fact]
    public void Compose_AppliesEditorialLightToTheCoverAndBodySlides()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));

        var (content, _) = composer.Compose(BuildData(
            ProjectBriefingPresentationTheme.EditorialLight,
            ProjectBriefingBrandingScope.AllSlides));

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();

        Assert.Contains("F7F7F5", slides[0].Slide.OuterXml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("F7F7F5", slides[1].Slide.OuterXml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("181D25", slides[0].Slide.OuterXml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_AppliesGraphiteThemeAndEmbedsBothHeaderInsigniaOnEverySlide()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));

        var (content, slideCount) = composer.Compose(BuildData(
            ProjectBriefingPresentationTheme.GraphiteDark,
            ProjectBriefingBrandingScope.AllSlides));

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();

        Assert.Equal(slideCount, slides.Length);
        Assert.All(slides, slide => Assert.True(
            slide.ImageParts.Count() >= 2,
            "All-slide branding must embed both header insignia on every slide."));
        Assert.All(slides.Skip(1), slide => Assert.Contains(
            "15181E",
            slide.Slide.OuterXml,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compose_PaginatesFortyNineShortRowsIntoNineReadableProjectTables()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var projects = Enumerable.Range(1, 49)
            .Select(index => new ProjectBriefingPresentationProject
            {
                ProjectId = index,
                ProjectName = $"Project {index:00}",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                LifecycleDisplay = "Ongoing",
                PresentStageCode = "DEV",
                PresentStage = "Development",
                PresentStageOrder = ProjectBriefingStageOrder.Development,
                CostRd = new ProjectBriefingCostValue(1_000_000m, ProjectBriefingCostBasis.L1, "₹10 Lakh", "L1"),
                ProliferationCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
                ExternalStatus = "Development in progress.",
                BriefDescription = "Brief capability description.",
                SortOrder = index
            })
            .ToArray();

        var data = new ProjectBriefingPresentationData
        {
            DeckId = 9,
            DeckName = "Project Update Review",
            PresentationMode = ProjectBriefingPresentationMode.ExecutiveTable,
            CostMode = ProjectBriefingCostMode.CostRdOnly,
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 22, 3, 30, 0, TimeSpan.Zero),
            Projects = projects,
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = projects.Length,
                OngoingCount = projects.Length,
                CostRdRecordedCount = projects.Length
            }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(11, slideCount); // cover + portfolio + nine project-table slides
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        var tables = slides.SelectMany(slide => slide.Slide.Descendants<A.Table>()).ToArray();
        Assert.Equal(9, tables.Length);

        var text = string.Join("\n", slides
            .SelectMany(slide => slide.Slide.Descendants<A.Text>())
            .Select(node => node.Text));
        Assert.Contains("Project status summary (9/9)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_UsesTheSameMaturityOrderForExecutiveRowsAndProjectSlides()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var projects = new[]
        {
            Project(1, "IPA PROJECT", ProjectLifecycleStatus.Active, "IPA", "In-Principle Approval", ProjectBriefingStageOrder.InPrincipleApproval, 10),
            Project(2, "DEVELOPMENT PROJECT", ProjectLifecycleStatus.Active, "DEVP", "Development", ProjectBriefingStageOrder.Development, 30),
            Project(3, "COMPLETED PROJECT", ProjectLifecycleStatus.Completed, "COMPLETED", "Completed", ProjectBriefingStageOrder.Completed, 20),
            Project(4, "SUPPLY ORDER PROJECT", ProjectLifecycleStatus.Active, "SO", "Supply Order", ProjectBriefingStageOrder.SupplyOrder, 40)
        };

        var data = new ProjectBriefingPresentationData
        {
            DeckId = 51,
            DeckName = "Maturity Order Review",
            PresentationMode = ProjectBriefingPresentationMode.Combined,
            CostMode = ProjectBriefingCostMode.None,
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero),
            Projects = projects,
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = projects.Length,
                OngoingCount = 3,
                CompletedCount = 1
            }
        };

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();

        var tableTexts = slides
            .SelectMany(slide => slide.Slide.Descendants<A.Table>())
            .SelectMany(table => table.Descendants<A.Text>())
            .Select(node => node.Text)
            .ToArray();
        Assert.True(Array.IndexOf(tableTexts, "COMPLETED PROJECT") < Array.IndexOf(tableTexts, "DEVELOPMENT PROJECT"));
        Assert.True(Array.IndexOf(tableTexts, "DEVELOPMENT PROJECT") < Array.IndexOf(tableTexts, "SUPPLY ORDER PROJECT"));
        Assert.True(Array.IndexOf(tableTexts, "SUPPLY ORDER PROJECT") < Array.IndexOf(tableTexts, "IPA PROJECT"));

        var slideTitles = slides
            .Select(slide => slide.Slide.Descendants<A.Text>().Select(node => node.Text).FirstOrDefault())
            .Where(value => value is not null)
            .ToArray();
        Assert.True(Array.IndexOf(slideTitles, "COMPLETED PROJECT") < Array.IndexOf(slideTitles, "DEVELOPMENT PROJECT"));
        Assert.True(Array.IndexOf(slideTitles, "DEVELOPMENT PROJECT") < Array.IndexOf(slideTitles, "SUPPLY ORDER PROJECT"));
        Assert.True(Array.IndexOf(slideTitles, "SUPPLY ORDER PROJECT") < Array.IndexOf(slideTitles, "IPA PROJECT"));
    }

    [Fact]
    public void Compose_PreservesCompleteCapabilityContentAcrossContinuationSlides()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var paragraphs = Enumerable.Range(1, 18)
            .Select(index =>
                $"Capability paragraph {index} explains the complete operational function, training application, system behaviour, user interaction, safety consideration and expected employment of the project without omitting audience-relevant information.")
            .ToArray();
        var description = string.Join(
            "\n\n",
            new[]
            {
                "Capability Overview",
                paragraphs[0],
                "Key Deliverables",
                string.Join("\n", paragraphs.Skip(1).Take(8).Select((value, index) => $"• Deliverable {index + 1}: {value}")),
                "Operational Impact",
                string.Join("\n", paragraphs.Skip(9))
            });

        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 91,
            ProjectName = "LONG CAPABILITY PROJECT",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = "DEV",
            PresentStage = "Development",
            PresentStageOrder = ProjectBriefingStageOrder.Development,
            ProjectCategory = "Other R&D Projects",
            TechnicalCategory = "AR / VR",
            CostRd = new ProjectBriefingCostValue(10_000_000m, ProjectBriefingCostBasis.L1, "₹1 Cr", "L1"),
            ProliferationCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
            ExternalStatus = "Development in progress.",
            BriefDescription = description,
            SortOrder = 1
        };

        var data = new ProjectBriefingPresentationData
        {
            DeckId = 91,
            DeckName = "Full Capability Review",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            CostMode = ProjectBriefingCostMode.CostRdOnly,
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 22, 6, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 1,
                OngoingCount = 1,
                CostRdRecordedCount = 1,
                TotalCostRdInRupees = 10_000_000m
            }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.True(slideCount > 3, "Long capability content should create one or more continuation slides.");
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        var text = string.Join("\n", slides
            .SelectMany(slide => slide.Slide.Descendants<A.Text>())
            .Select(node => node.Text));

        Assert.Contains("CAPABILITY OVERVIEW — CONTINUED", text, StringComparison.Ordinal);
        Assert.Contains(paragraphs[^1], text, StringComparison.Ordinal);
        Assert.DoesNotContain("operational function,…", text, StringComparison.Ordinal);
    }

    private static ProjectBriefingPresentationProject Project(
        int projectId,
        string name,
        ProjectLifecycleStatus lifecycleStatus,
        string stageCode,
        string stage,
        int stageOrder,
        int sortOrder)
        => new()
        {
            ProjectId = projectId,
            ProjectName = name,
            LifecycleStatus = lifecycleStatus,
            LifecycleDisplay = lifecycleStatus == ProjectLifecycleStatus.Completed ? "Completed" : "Ongoing",
            PresentStageCode = stageCode,
            PresentStage = stage,
            PresentStageOrder = stageOrder,
            CostRd = ProjectBriefingCostValue.Missing(),
            ProliferationCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
            ExternalStatus = "Status available.",
            BriefDescription = "Capability overview.",
            SortOrder = sortOrder
        };

    private static ProjectBriefingPresentationData BuildData(
        ProjectBriefingPresentationTheme presentationTheme = ProjectBriefingPresentationTheme.EditorialLight,
        ProjectBriefingBrandingScope brandingScope = ProjectBriefingBrandingScope.AllSlides)
    {
        var projects = new[]
        {
            new ProjectBriefingPresentationProject
            {
                ProjectId = 1,
                ProjectName = "AURA",
                LifecycleStatus = ProjectLifecycleStatus.Active,
                LifecycleDisplay = "Ongoing",
                PresentStageCode = "AON",
                PresentStage = "Acceptance of Necessity",
                PresentStageOrder = ProjectBriefingStageOrder.AcceptanceOfNecessity,
                ProjectCategory = "CoE",
                TechnicalCategory = "AR / VR",
                CostRd = new ProjectBriefingCostValue(39_530_000m, ProjectBriefingCostBasis.AoN, "₹3.95 Cr", "AoN"),
                ProliferationCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
                ExternalStatus = "Latest external status for AURA",
                ExternalStatusDate = new DateOnly(2026, 7, 20),
                BriefDescription = "Augmented-reality situational-awareness capability for dismounted users.",
                SortOrder = 10
            },
            new ProjectBriefingPresentationProject
            {
                ProjectId = 2,
                ProjectName = "ASTRAE",
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                LifecycleDisplay = "Completed",
                PresentStageCode = "COMPLETED",
                PresentStage = "Completed",
                PresentStageOrder = ProjectBriefingStageOrder.Completed,
                ProjectCategory = "CoE",
                TechnicalCategory = "AI",
                CostRd = new ProjectBriefingCostValue(28_000_000m, ProjectBriefingCostBasis.L1, "₹2.8 Cr", "L1"),
                ProliferationCost = new ProjectBriefingCostValue(1_850_000m, ProjectBriefingCostBasis.Proliferation, "₹18.5 Lakh", "Proliferation"),
                ExternalStatus = "Trials completed and project available for briefing.",
                ExternalStatusDate = new DateOnly(2026, 7, 18),
                BriefDescription = "AI-enabled target acquisition and engagement system.",
                SortOrder = 20
            }
        };

        return new ProjectBriefingPresentationData
        {
            DeckId = 7,
            DeckName = "Quarterly Command Review",
            DeckDescription = "Selected development and completed projects",
            PresentationMode = ProjectBriefingPresentationMode.Combined,
            CostMode = ProjectBriefingCostMode.Both,
            PresentationTheme = presentationTheme,
            BrandingScope = brandingScope,
            IncludeStageSummary = true,
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 21, 10, 0, 0, TimeSpan.Zero),
            Projects = projects,
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 2,
                OngoingCount = 1,
                CompletedCount = 1,
                TotalCostRdInRupees = 67_530_000m,
                CostRdRecordedCount = 2,
                TotalProliferationCostInRupees = 1_850_000m,
                ProliferationCostRecordedCount = 1,
                MissingExternalStatusCount = 0,
                MissingPhotoCount = 2,
                StageSummary = new[]
                {
                    new ProjectBriefingSummaryPoint("Completed", 1, ProjectBriefingStageOrder.Completed),
                    new ProjectBriefingSummaryPoint("Acceptance of Necessity", 1, ProjectBriefingStageOrder.AcceptanceOfNecessity)
                }
            }
        };
    }

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
