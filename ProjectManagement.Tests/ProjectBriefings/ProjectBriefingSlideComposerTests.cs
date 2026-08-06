using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

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
        Assert.Equal(8, slideCount);

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
    }


    [Fact]
    public void Compose_StageSummary_UsesExecutiveLifecycleBandAndOngoingStageInfographic()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 601,
            DeckName = "Stage Summary Review",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            CostMode = ProjectBriefingCostMode.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            IncludeStageSummary = true,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 6, 15, 30, 0, TimeSpan.Zero),
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 25,
                CompletedCount = 15,
                OngoingCount = 10,
                StageSummary = new[]
                {
                    new ProjectBriefingSummaryPoint("Completed", 15, ProjectBriefingStageOrder.Completed),
                    new ProjectBriefingSummaryPoint("Development", 2, ProjectBriefingStageOrder.Development),
                    new ProjectBriefingSummaryPoint("Technical Evaluation", 1, ProjectBriefingStageOrder.TechnicalEvaluation),
                    new ProjectBriefingSummaryPoint("Bidding / Tendering", 1, ProjectBriefingStageOrder.BiddingTendering),
                    new ProjectBriefingSummaryPoint("Acceptance of Necessity", 5, ProjectBriefingStageOrder.AcceptanceOfNecessity),
                    new ProjectBriefingSummaryPoint("Scope of Work Vetting", 1, ProjectBriefingStageOrder.SowVetting)
                },
                OngoingStageSummary = new[]
                {
                    new ProjectBriefingSummaryPoint("Development", 2, ProjectBriefingStageOrder.Development),
                    new ProjectBriefingSummaryPoint("Technical Evaluation", 1, ProjectBriefingStageOrder.TechnicalEvaluation),
                    new ProjectBriefingSummaryPoint("Bidding / Tendering", 1, ProjectBriefingStageOrder.BiddingTendering),
                    new ProjectBriefingSummaryPoint("Acceptance of Necessity", 5, ProjectBriefingStageOrder.AcceptanceOfNecessity),
                    new ProjectBriefingSummaryPoint("Scope of Work Vetting", 1, ProjectBriefingStageOrder.SowVetting)
                }
            }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(2, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var stageSlide = Assert.Single(Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts
            .Where(slide => SlideText(slide).Contains("Stage-wise summary", StringComparison.Ordinal)));
        var text = SlideText(stageSlide);

        Assert.Contains("Completed", text, StringComparison.Ordinal);
        Assert.Contains("15 (60%)", text, StringComparison.Ordinal);
        Assert.Contains("Ongoing", text, StringComparison.Ordinal);
        Assert.Contains("10 (40%)", text, StringComparison.Ordinal);
        Assert.Contains("Breakdown of ongoing projects by stage", text, StringComparison.Ordinal);
        Assert.Contains("Key takeaway:", text, StringComparison.Ordinal);
        Assert.Contains("AoN is the principal concentration", text, StringComparison.Ordinal);
        Assert.NotNull(ShapeByName(stageSlide, "Acceptance of Necessity stage column"));
        Assert.Empty(stageSlide.Slide.Descendants<P.Shape>().Where(shape => string.Equals(
            shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value,
            "Completed stage column",
            StringComparison.Ordinal)));

        Assert.True(text.IndexOf("1. Development", StringComparison.Ordinal)
                    < text.IndexOf("2. Technical Evaluation", StringComparison.Ordinal));
        Assert.True(text.IndexOf("2. Technical Evaluation", StringComparison.Ordinal)
                    < text.IndexOf("3. Bidding / Tendering", StringComparison.Ordinal));
        Assert.True(text.IndexOf("3. Bidding / Tendering", StringComparison.Ordinal)
                    < text.IndexOf("4. Acceptance of Necessity", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_StageSummary_UsesCompactGridAndCancelledPanelWhenRequired()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var points = new[]
        {
            new ProjectBriefingSummaryPoint("Transfer of Technology", 1, ProjectBriefingStageOrder.TransferOfTechnology),
            new ProjectBriefingSummaryPoint("Acceptance Testing", 1, ProjectBriefingStageOrder.AcceptanceTesting),
            new ProjectBriefingSummaryPoint("Development", 1, ProjectBriefingStageOrder.Development),
            new ProjectBriefingSummaryPoint("Supply Order", 1, ProjectBriefingStageOrder.SupplyOrder),
            new ProjectBriefingSummaryPoint("Technical Evaluation", 1, ProjectBriefingStageOrder.TechnicalEvaluation),
            new ProjectBriefingSummaryPoint("Acceptance of Necessity", 1, ProjectBriefingStageOrder.AcceptanceOfNecessity),
            new ProjectBriefingSummaryPoint("Scope of Work Vetting", 1, ProjectBriefingStageOrder.SowVetting)
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 602,
            DeckName = "Compact Stage Summary Review",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            CostMode = ProjectBriefingCostMode.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            IncludeStageSummary = true,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 6, 15, 30, 0, TimeSpan.Zero),
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 10,
                CompletedCount = 2,
                OngoingCount = 7,
                CancelledCount = 1,
                OngoingStageSummary = points
            }
        };

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var stageSlide = Assert.Single(Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts
            .Where(slide => SlideText(slide).Contains("Stage-wise summary", StringComparison.Ordinal)));
        var text = SlideText(stageSlide);

        Assert.Contains("Cancelled", text, StringComparison.Ordinal);
        Assert.Contains("1 (10%)", text, StringComparison.Ordinal);
        Assert.NotNull(ShapeByName(stageSlide, "Development compact stage card"));
        Assert.NotNull(ShapeByName(stageSlide, "Stage summary insight strip"));
    }

    [Fact]
    public void Compose_StageSummary_HandlesAllCompletedPortfolioWithoutEmptyChart()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 603,
            DeckName = "Completed Portfolio Review",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            CostMode = ProjectBriefingCostMode.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            IncludeStageSummary = true,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 6, 15, 30, 0, TimeSpan.Zero),
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 12,
                CompletedCount = 12,
                OngoingCount = 0
            }
        };

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var stageSlide = Assert.Single(Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts
            .Where(slide => SlideText(slide).Contains("Stage-wise summary", StringComparison.Ordinal)));
        var text = SlideText(stageSlide);

        Assert.Contains("12 (100%)", text, StringComparison.Ordinal);
        Assert.Contains("0 (0%)", text, StringComparison.Ordinal);
        Assert.Contains("All selected projects are completed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("No summary data is available", text, StringComparison.Ordinal);
    }


    [Fact]
    public void Compose_ProjectBriefMode_CreatesDedicatedNarrativeSlideWithoutCapabilitySlide()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = BriefingProject(
            501,
            "PROJECT BRIEF MODE",
            StageCodes.DEVP,
            ProjectBriefingStageOrder.Development,
            1,
            projectBrief: "This project brief explains the operational need, intended employment and principal outcomes in a concise narrative suitable for a briefing audience.");
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 501,
            DeckName = "Project Brief Review",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            CostMode = ProjectBriefingCostMode.None,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 27, 6, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 1,
                OngoingCount = 1
            }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(4, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        var text = string.Join("\n", slides
            .SelectMany(slide => slide.Slide.Descendants<A.Text>())
            .Select(node => node.Text));
        var projectSlide = Assert.Single(slides.Where(slide =>
            SlideText(slide).Contains("PROJECT BRIEF MODE", StringComparison.Ordinal)));

        Assert.Contains("PROJECT BRIEF", text, StringComparison.Ordinal);
        Assert.Contains("operational need", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CAPABILITY OVERVIEW", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PROJECT BRIEF ·", SlideText(projectSlide), StringComparison.Ordinal);
        Assert.Empty(projectSlide.Slide.Descendants<P.Shape>().Where(shape => string.Equals(
            shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value,
            "Slide subtitle",
            StringComparison.Ordinal)));
        Assert.Equal("7A263A", ShapeFillColor(ShapeByName(projectSlide, "Slide top accent")));
    }

    [Fact]
    public void Compose_BothNarratives_CreatesCapabilityAndProjectBriefSlides()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = BriefingProject(
            502,
            "BOTH CONTENT MODES",
            StageCodes.DEVP,
            ProjectBriefingStageOrder.Development,
            1,
            projectBrief: "A concise project brief retained independently from the structured capability overview.");
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 502,
            DeckName = "Combined Content Review",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            CostMode = ProjectBriefingCostMode.None,
            NarrativeMode = ProjectBriefingNarrativeMode.Both,
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 27, 6, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 1,
                OngoingCount = 1
            }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(5, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var text = string.Join("\n", Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts
            .SelectMany(slide => slide.Slide.Descendants<A.Text>())
            .Select(node => node.Text));

        Assert.Contains("CAPABILITY OVERVIEW", text, StringComparison.Ordinal);
        Assert.Contains("PROJECT BRIEF", text, StringComparison.Ordinal);
        Assert.Contains("retained independently", text, StringComparison.Ordinal);
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
        Assert.All(slides
            .Where(slide => !IsClosingSlide(slide))
            .Skip(1), slide => Assert.Contains(
                "15181E",
                slide.Slide.OuterXml,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compose_PaginatesFortyNineShortRowsIntoSevenBalancedProjectTables()
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
                PresentStageOrder = 70,
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

        Assert.Equal(10, slideCount); // cover + portfolio + seven project-table slides + closing
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        var tables = slides.SelectMany(slide => slide.Slide.Descendants<A.Table>()).ToArray();
        Assert.Equal(7, tables.Length);

        var text = string.Join("\n", slides
            .SelectMany(slide => slide.Slide.Descendants<A.Text>())
            .Select(node => node.Text));
        Assert.Contains("Project status summary (7/7)", text, StringComparison.Ordinal);
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
            PresentStageOrder = 70,
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

    [Fact]
    public void Compose_UsesOneSemanticCapabilityTextBoxAndKeepsLetterMarkersWithTheirText()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 101,
            ProjectName = "LETTERED CAPABILITY PROJECT",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            LifecycleDisplay = "Completed",
            PresentStageCode = "COMPLETED",
            PresentStage = "Completed",
            PresentStageOrder = 10_000,
            ProjectCategory = "Other R&D Projects",
            TechnicalCategory = "AR / VR",
            CostRd = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.L1),
            ProliferationCost = new ProjectBriefingCostValue(
                600_000m,
                ProjectBriefingCostBasis.Proliferation,
                "₹6 Lakh",
                "Proliferation"),
            ExternalStatus = "Delivered and installed.",
            BriefDescription = """
                The system has the following features:-
                (a) Portable system. The components can be carried and installed easily.
                (b) Software. The application is user friendly.
                (c) Indigenous design. Maintenance and upgrades remain cost effective.
                """,
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 101,
            DeckName = "Semantic Editing Review",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            CostMode = ProjectBriefingCostMode.Both,
            PresentationTheme = ProjectBriefingPresentationTheme.EditorialLight,
            BrandingScope = ProjectBriefingBrandingScope.AllSlides,
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 1,
                CompletedCount = 1,
                ProliferationCostRecordedCount = 1,
                TotalProliferationCostInRupees = 600_000m
            }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(4, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var projectSlide = Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts
            .Single(slide => slide.Slide.Descendants<A.Text>()
                .Any(text => text.Text == "LETTERED CAPABILITY PROJECT"));

        var capabilityShape = Assert.Single(projectSlide.Slide
            .Descendants<P.Shape>()
            .Where(shape => string.Equals(
                shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value,
                "Capability overview",
                StringComparison.Ordinal)));
        var capabilityParagraphs = capabilityShape.Descendants<A.Paragraph>().ToArray();

        Assert.Contains(capabilityParagraphs, paragraph =>
            string.Concat(paragraph.Descendants<A.Text>().Select(text => text.Text))
                .Replace("\t", string.Empty, StringComparison.Ordinal)
                .StartsWith("(a)Portable system", StringComparison.Ordinal));
        Assert.Contains(capabilityParagraphs, paragraph =>
            string.Concat(paragraph.Descendants<A.Text>().Select(text => text.Text))
                .Replace("\t", string.Empty, StringComparison.Ordinal)
                .StartsWith("(b)Software", StringComparison.Ordinal));
        Assert.Contains(capabilityParagraphs, paragraph =>
            string.Concat(paragraph.Descendants<A.Text>().Select(text => text.Text))
                .Replace("\t", string.Empty, StringComparison.Ordinal)
                .StartsWith("(c)Indigenous design", StringComparison.Ordinal));

        var textBoxCount = projectSlide.Slide.OuterXml
            .Split("txBox=\"1\"", StringSplitOptions.None)
            .Length - 1;
        Assert.True(
            textBoxCount <= 8,
            $"The project slide should remain easy to edit; found {textBoxCount} independent text boxes.");
    }

    [Fact]
    public void Compose_UsesIdenticalMaturityOrderForExecutiveAndDetailedSlides()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var projects = new[]
        {
            BriefingProject(1, "Development project", StageCodes.DEVP, ProjectBriefingStageOrder.Development, 10),
            BriefingProject(2, "Completed project B", ProjectBriefingStageOrder.CompletedCode, ProjectBriefingStageOrder.Completed, 900, ProjectLifecycleStatus.Completed),
            BriefingProject(3, "AoN project", StageCodes.AON, ProjectBriefingStageOrder.AcceptanceOfNecessity, 20),
            BriefingProject(4, "Completed project A", ProjectBriefingStageOrder.CompletedCode, ProjectBriefingStageOrder.Completed, 30, ProjectLifecycleStatus.Completed)
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 110,
            DeckName = "Ordering Regression Review",
            PresentationMode = ProjectBriefingPresentationMode.Combined,
            CostMode = ProjectBriefingCostMode.None,
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 24, 4, 0, 0, TimeSpan.Zero),
            Projects = projects,
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = projects.Length,
                OngoingCount = 2,
                CompletedCount = 2,
                StageSummary = ProjectBriefingStageOrder.BuildSummary(
                    projects.Select(project => project.PresentStageOrder))
            }
        };

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        var slideTexts = slides.Select(SlideText).ToArray();

        var executiveText = Assert.Single(slideTexts.Where(text =>
            text.Contains("Project status summary", StringComparison.Ordinal)));
        AssertProjectSequence(
            executiveText,
            "Completed project A",
            "Completed project B",
            "Development project",
            "AoN project");

        var detailTexts = slideTexts
            .Where(text => text.Contains("CAPABILITY OVERVIEW", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(4, detailTexts.Length);
        Assert.Contains("Completed project A", detailTexts[0], StringComparison.Ordinal);
        Assert.Contains("Completed project B", detailTexts[1], StringComparison.Ordinal);
        Assert.Contains("Development project", detailTexts[2], StringComparison.Ordinal);
        Assert.Contains("AoN project", detailTexts[3], StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_ProjectUpdateSheet_CreatesSingleEditableWidescreenProjectSheet()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 701,
            ProjectName = "Touch Screen Based Simulator for OSAAK Missile System",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = StageCodes.DEVP,
            PresentStage = "Development",
            PresentStageOrder = ProjectBriefingStageOrder.Development,
            CostRd = new ProjectBriefingCostValue(7_400_000m, ProjectBriefingCostBasis.L1, "₹0.74 Cr", "L1"),
            IpaCost = new ProjectBriefingCostValue(8_000_000m, ProjectBriefingCostBasis.IPA, "₹0.8 Cr", "IPA"),
            ProliferationCost = new ProjectBriefingCostValue(6_200_000m, ProjectBriefingCostBasis.Proliferation, "₹0.62 Cr", "Proliferation"),
            ArppReference = "ARPP/IR&D/CU/2026-27/14",
            Fund = "IR&D",
            DfpdsSchedule = "9.3",
            Cfa = "Comdt SDD",
            AonDate = new DateOnly(2024, 9, 20),
            SupplyOrderDate = new DateOnly(2024, 12, 14),
            DevelopmentPdcDate = new DateOnly(2025, 12, 4),
            JdpNames = new[] { "Example Defence Technologies Pvt Ltd" },
            ExternalStatus = "Project development is progressing as per the approved plan.",
            ProjectOfficer = "Lt Col Udit Agarwal",
            LineDirectorate = "DG AAD",
            ProjectBrief = "The simulator provides realistic weapon-system training without expenditure of operational ammunition.",
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 701,
            DeckName = "Formal Project Update",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            PresentationMode = ProjectBriefingPresentationMode.Combined,
            CostMode = ProjectBriefingCostMode.Both,
            NarrativeMode = ProjectBriefingNarrativeMode.CapabilityOverview,
            PresentationTheme = ProjectBriefingPresentationTheme.GraphiteDark,
            BrandingScope = ProjectBriefingBrandingScope.AllSlides,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            UpdateSheetOptions = new ProjectBriefingUpdateSheetOptions(
                ProjectBriefingUpdateSheetOptions.AllRows,
                HideEmptyValues: false),
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 29, 2, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 1,
                OngoingCount = 1,
                TotalCostRdInRupees = 7_400_000m,
                CostRdRecordedCount = 1,
                TotalIpaCostInRupees = 8_000_000m,
                IpaCostRecordedCount = 1
            }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(2, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var presentationPart = Assert.IsType<PresentationPart>(document.PresentationPart);
        Assert.Equal(12192000, presentationPart.Presentation.SlideSize?.Cx?.Value);
        Assert.Equal(6858000, presentationPart.Presentation.SlideSize?.Cy?.Value);
        var slide = SingleContentSlide(presentationPart);
        var text = SlideText(slide);

        Assert.DoesNotContain("PROJECT UPDATE SHEET", text, StringComparison.Ordinal);
        Assert.Contains("Touch Screen Based Simulator", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Name of Project", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PROJECT COST", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("₹0.74 Cr", text, StringComparison.Ordinal);
        Assert.Contains("ARPP/IR&D/CU/2026-27/14", text, StringComparison.Ordinal);
        Assert.Contains("20 Sep 24", text, StringComparison.Ordinal);
        Assert.Contains("SO Date: 14 Dec 24", text, StringComparison.Ordinal);
        Assert.Contains("Firm: Example Defence Technologies", text, StringComparison.Ordinal);
        Assert.Contains("04 Dec 25", text, StringComparison.Ordinal);
        Assert.Contains("Lt Col Udit Agarwal", text, StringComparison.Ordinal);
        Assert.Contains("DG AAD", text, StringComparison.Ordinal);
        Assert.Contains("BRIEF OF THE PROJECT", text, StringComparison.Ordinal);
        Assert.Contains("without expenditure of operational ammunition", text, StringComparison.Ordinal);
        Assert.DoesNotContain("₹0.62 Cr", text, StringComparison.Ordinal);
        Assert.Contains("15181E", slide.Slide.OuterXml, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("F3F4F6", ShapeTextColor(ShapeByName(slide, "Project sheet title")));
        Assert.Equal("5B7CFA", ShapeFillColor(ShapeByName(slide, "Project sheet top accent")));
        Assert.DoesNotContain("8F0D21", slide.Slide.OuterXml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Left formation insignia", slide.Slide.OuterXml, StringComparison.Ordinal);
        Assert.Contains("Right division insignia", slide.Slide.OuterXml, StringComparison.Ordinal);

        var leftInsignia = slide.Slide.Descendants<P.Picture>()
            .Single(picture => string.Equals(
                picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value,
                "Left formation insignia",
                StringComparison.Ordinal));
        var rightInsignia = slide.Slide.Descendants<P.Picture>()
            .Single(picture => string.Equals(
                picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value,
                "Right division insignia",
                StringComparison.Ordinal));
        var leftExtent = Assert.IsType<A.Extents>(leftInsignia.ShapeProperties?.Transform2D?.Extents);
        var rightExtent = Assert.IsType<A.Extents>(rightInsignia.ShapeProperties?.Transform2D?.Extents);
        Assert.True(rightExtent.Cy?.Value > leftExtent.Cy?.Value);
        Assert.True((rightExtent.Cx?.Value ?? 0L) * (rightExtent.Cy?.Value ?? 0L)
            > (leftExtent.Cx?.Value ?? 0L) * (leftExtent.Cy?.Value ?? 0L));

        Assert.DoesNotContain("Compact footer insignia", slide.Slide.OuterXml, StringComparison.Ordinal);
        Assert.Equal(2, slide.ImageParts.Count());
        Assert.Single(slide.Slide.Descendants<A.Table>());
    }

    [Fact]
    public void Compose_ProjectUpdateSheet_EditorialLightUsesFormalRuleAndPrimaryTitleText()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = BriefingProject(
            711,
            "Editorial update sheet",
            StageCodes.AON,
            ProjectBriefingStageOrder.AcceptanceOfNecessity,
            1,
            projectBrief: "A concise formal project update for circulation and print.");
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 711,
            DeckName = "Editorial Update",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            PresentationTheme = ProjectBriefingPresentationTheme.EditorialLight,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        Assert.Equal("191B20", ShapeTextColor(ShapeByName(slide, "Project sheet title")));
        Assert.Equal("8F0D21", ShapeFillColor(ShapeByName(slide, "Project sheet top accent")));
        Assert.Equal("EDF1F6", ShapeFillColor(ShapeByName(slide, "Project brief heading")));
    }

    [Fact]
    public void Compose_GraphiteProjectSlideUsesSemanticHeaderNarrativeAndOperationalColours()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var (content, _) = composer.Compose(BuildData(ProjectBriefingPresentationTheme.GraphiteDark));

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts
            .Single(part => SlideText(part).Contains("AURA", StringComparison.Ordinal)
                && SlideText(part).Contains("CAPABILITY OVERVIEW", StringComparison.Ordinal));

        Assert.Equal("F3F4F6", ShapeTextColor(ShapeByName(slide, "Slide title")));
        Assert.Equal("8A3042", ShapeFillColor(ShapeByName(slide, "Slide top accent")));
        Assert.Equal("4FA6A8", ShapeFillColor(ShapeByName(slide, "Capability accent")));
        Assert.Equal("5B7CFA", ShapeTextColor(ShapeByName(slide, "Present status labels")));
    }

    [Fact]
    public void Compose_ShortStatusWithoutPhotographKeepsLeftColumnVisuallyBalanced()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var (content, _) = composer.Compose(BuildData(ProjectBriefingPresentationTheme.GraphiteDark));

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts
            .Single(part => SlideText(part).Contains("AURA", StringComparison.Ordinal)
                && SlideText(part).Contains("CAPABILITY OVERVIEW", StringComparison.Ordinal));
        var placeholderHeight = ShapeHeight(ShapeByName(slide, "Project photograph placeholder"));
        var statusHeight = ShapeHeight(ShapeByName(slide, "Present status panel"));

        Assert.True(placeholderHeight >= 1.20 * 914400, $"Expected a substantive placeholder zone; found {placeholderHeight} EMU.");
        Assert.True(statusHeight <= 2.70 * 914400, $"Expected a bounded status card; found {statusHeight} EMU.");
    }

    [Fact]
    public void Compose_ProjectUpdateSheet_LeavesPppCellBlankForDelistedPosition()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 702,
            ProjectName = "Delisted Project",
            LifecycleStatus = ProjectLifecycleStatus.Cancelled,
            LifecycleDisplay = "Cancelled",
            PresentStageCode = "CANCELLED",
            PresentStage = "Cancelled",
            PresentStageOrder = 1,
            CostRd = new ProjectBriefingCostValue(5_000_000m, ProjectBriefingCostBasis.IPA, "₹50 Lakh", "IPA"),
            ArppPppNumberApplicable = false,
            Fund = "IR&D",
            DfpdsSchedule = "9.3",
            Cfa = "Comdt SDD",
            ProjectBrief = "Delisted position retained for historical and financial reference.",
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 702,
            DeckName = "Delisted Position",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            UpdateSheetOptions = new ProjectBriefingUpdateSheetOptions(
                new[] { ProjectBriefingUpdateSheetRow.ArppPppNumber },
                HideEmptyValues: false),
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 29, 2, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1 }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(2, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var table = Assert.Single(slide.Slide.Descendants<A.Table>());
        var arppRow = table.Elements<A.TableRow>()
            .Single(row => row.Descendants<A.Text>().Any(text => text.Text == "ARPP/PPP Number"));
        var cells = arppRow.Elements<A.TableCell>().ToArray();

        Assert.Equal(3, cells.Length);
        Assert.Equal(string.Empty, string.Concat(cells[2].Descendants<A.Text>().Select(text => text.Text)));
    }

    [Fact]
    public void Compose_ProjectUpdateSheet_LeavesPdcBlankOutsideDevelopmentStage()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 703,
            ProjectName = "AoN Stage Project",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = StageCodes.AON,
            PresentStage = "Acceptance of Necessity",
            PresentStageOrder = ProjectBriefingStageOrder.AcceptanceOfNecessity,
            CostRd = new ProjectBriefingCostValue(10_000_000m, ProjectBriefingCostBasis.AoN, "₹1.00 Cr", "AoN"),
            DevelopmentPdcDate = new DateOnly(2030, 1, 15),
            ProjectBrief = "Concise project brief.",
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 703,
            DeckName = "PDC Rule",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 29, 2, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(2, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var text = SlideText(slide);
        Assert.Contains("PDC Date", text, StringComparison.Ordinal);
        Assert.DoesNotContain("15 Jan 30", text, StringComparison.Ordinal);
        var table = Assert.Single(slide.Slide.Descendants<A.Table>());
        var pdcRow = table.Elements<A.TableRow>()
            .Single(row => row.Descendants<A.Text>().Any(value => value.Text == "PDC Date"));
        var pdcCells = pdcRow.Elements<A.TableCell>().ToArray();
        Assert.Equal(string.Empty, string.Concat(pdcCells[2].Descendants<A.Text>().Select(value => value.Text)));
    }

    [Fact]
    public void Compose_ProjectUpdateSheet_CompletedProjectUsesCompletionStatus()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 704,
            ProjectName = "Completed Simulator",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            LifecycleDisplay = "Completed",
            PresentStageCode = ProjectBriefingStageOrder.CompletedCode,
            PresentStage = "Completed",
            PresentStageOrder = ProjectBriefingStageOrder.Completed,
            CompletionStatusDisplay = "Project completed on 18 Jun 2026",
            ProjectBrief = "Completed project brief.",
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 704,
            DeckName = "Completion Rule",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            UpdateSheetOptions = new ProjectBriefingUpdateSheetOptions(
                new[] { ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus },
                HideEmptyValues: false),
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, CompletedCount = 1 }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(2, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var text = SlideText(slide);
        Assert.Contains("Completion Status", text, StringComparison.Ordinal);
        Assert.Contains("Project completed on 18 Jun 2026", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PDC Date", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_ProjectUpdateSheet_UsesSelectedRowOrderAndHidesUnselectedRows()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 705,
            ProjectName = "Selectable Rows Project",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = StageCodes.AON,
            PresentStage = "Acceptance of Necessity",
            PresentStageOrder = ProjectBriefingStageOrder.AcceptanceOfNecessity,
            CostRd = new ProjectBriefingCostValue(20_000_000m, ProjectBriefingCostBasis.AoN, "₹2 Cr", "AoN"),
            ExternalStatus = "AoN accorded.",
            LineDirectorate = "Inf",
            ProjectBrief = "Selectable rows project brief.",
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 705,
            DeckName = "Selectable Rows",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            UpdateSheetOptions = new ProjectBriefingUpdateSheetOptions(
                new[]
                {
                    ProjectBriefingUpdateSheetRow.LineDirectorate,
                    ProjectBriefingUpdateSheetRow.PresentStatus,
                    ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus
                },
                HideEmptyValues: false),
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, _) = composer.Compose(data);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var table = Assert.Single(slide.Slide.Descendants<A.Table>());
        var labels = table.Elements<A.TableRow>()
            .Select(row => string.Concat(row.Elements<A.TableCell>().ElementAt(1).Descendants<A.Text>().Select(text => text.Text)))
            .ToArray();

        Assert.Equal(new[] { "Line Directorate", "Present Status", "PDC Date" }, labels);
        Assert.DoesNotContain("Project Cost", labels);
        Assert.DoesNotContain("Name of Project", labels);
    }

    [Fact]
    public void Compose_ProjectUpdateSheet_HideEmptyStillKeepsEditablePdcRow()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 706,
            ProjectName = "Editable Blank PDC",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = StageCodes.AON,
            PresentStage = "Acceptance of Necessity",
            PresentStageOrder = ProjectBriefingStageOrder.AcceptanceOfNecessity,
            ProjectBrief = "Brief.",
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 706,
            DeckName = "Hide Empty",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            UpdateSheetOptions = new ProjectBriefingUpdateSheetOptions(
                new[]
                {
                    ProjectBriefingUpdateSheetRow.ProjectCost,
                    ProjectBriefingUpdateSheetRow.PdcOrCompletionStatus
                },
                HideEmptyValues: true),
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, _) = composer.Compose(data);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var text = SlideText(slide);
        Assert.Contains("PDC Date", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Project Cost", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_ProjectUpdateSheet_OneOrTwoRowsUsesCompactBalancedLayout()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = BriefingProject(
            707,
            "Compact update sheet",
            StageCodes.AON,
            ProjectBriefingStageOrder.AcceptanceOfNecessity,
            1,
            projectBrief: "A concise project brief that should remain beside the photograph when only one information field is selected.");
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 707,
            DeckName = "Compact Update",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            UpdateSheetOptions = new ProjectBriefingUpdateSheetOptions(
                new[] { ProjectBriefingUpdateSheetRow.ProjectCost },
                HideEmptyValues: false),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, _) = composer.Compose(data);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var facts = ShapeByName(slide, "Project facts panel - Compact");
        var photo = ShapeByName(slide, "Project photograph frame - Compact");
        var brief = ShapeByName(slide, "Project brief panel - Compact");

        Assert.True(ShapeWidth(facts) > 12.0 * 914400, "Compact facts should form a full-width information band.");
        Assert.Equal(ShapeY(photo), ShapeY(brief));
        Assert.Equal(ShapeHeight(photo), ShapeHeight(brief));
        Assert.True(ShapeX(photo) < ShapeX(brief));
        Assert.True(ShapeY(photo) > ShapeY(facts));
    }

    [Fact]
    public void Compose_ProjectUpdateSheet_ThreeToFiveRowsUsesStandardLayout()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = BriefingProject(
            708,
            "Standard update sheet",
            StageCodes.DEVP,
            ProjectBriefingStageOrder.Development,
            1,
            projectBrief: "A standard project brief for the recommended five-row command-update design.");
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 708,
            DeckName = "Standard Update",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            UpdateSheetOptions = new ProjectBriefingUpdateSheetOptions(
                ProjectBriefingUpdateSheetOptions.RecommendedRows,
                HideEmptyValues: false),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, _) = composer.Compose(data);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var facts = ShapeByName(slide, "Project facts panel - Standard");
        var photo = ShapeByName(slide, "Project photograph frame - Standard");
        var brief = ShapeByName(slide, "Project brief panel - Standard");

        Assert.Equal(ShapeY(facts), ShapeY(photo));
        Assert.Equal(ShapeHeight(facts), ShapeHeight(photo));
        Assert.True(ShapeX(photo) > ShapeX(facts));
        Assert.True(ShapeY(brief) > ShapeY(facts));
        Assert.True(ShapeWidth(facts) < 7.0 * 914400);
    }

    [Fact]
    public void Compose_ProjectUpdateSheet_SixOrMoreRowsUsesDetailedLayout()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = BriefingProject(
            709,
            "Detailed update sheet",
            StageCodes.DEVP,
            ProjectBriefingStageOrder.Development,
            1,
            projectBrief: "A detailed project brief for the complete nine-row project update sheet.");
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 709,
            DeckName = "Detailed Update",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            UpdateSheetOptions = new ProjectBriefingUpdateSheetOptions(
                ProjectBriefingUpdateSheetOptions.AllRows,
                HideEmptyValues: false),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, _) = composer.Compose(data);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var facts = ShapeByName(slide, "Project facts panel - Detailed");
        var photo = ShapeByName(slide, "Project photograph frame - Detailed");
        var brief = ShapeByName(slide, "Project brief panel - Detailed");

        Assert.Equal(ShapeY(facts), ShapeY(photo));
        Assert.Equal(ShapeHeight(facts), ShapeHeight(photo));
        Assert.True(ShapeWidth(facts) > 7.0 * 914400, "Detailed layout should allocate additional width to the facts table.");
        Assert.True(ShapeWidth(photo) < 5.0 * 914400, "Detailed layout should use a narrower photograph column.");
        Assert.True(ShapeY(brief) > ShapeY(facts));
    }

    [Fact]
    public void Compose_ProjectUpdateSheet_RespectsOptionalIntroductorySlides()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = BriefingProject(
            702,
            "Update sheet project",
            StageCodes.AON,
            ProjectBriefingStageOrder.AcceptanceOfNecessity,
            1,
            projectBrief: "Concise project brief.");
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 702,
            DeckName = "Project Update Review",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = true,
            IncludePortfolioSummarySlide = true,
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 29, 2, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 1,
                OngoingCount = 1,
                TotalCostRdInRupees = 7_400_000m,
                CostRdRecordedCount = 1,
                TotalIpaCostInRupees = 8_000_000m,
                IpaCostRecordedCount = 1
            }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(4, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slideTexts = Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts
            .Select(SlideText)
            .ToArray();
        Assert.Contains("PROJECT UPDATE REVIEW", slideTexts[0], StringComparison.Ordinal);
        Assert.Contains("Portfolio at a glance", slideTexts[1], StringComparison.Ordinal);
        Assert.Contains("TOTAL R&D COST", slideTexts[1], StringComparison.Ordinal);
        Assert.Contains("TOTAL IPA COST", slideTexts[1], StringComparison.Ordinal);
        Assert.Contains("₹0.74 Cr", slideTexts[1], StringComparison.Ordinal);
        Assert.Contains("₹0.80 Cr", slideTexts[1], StringComparison.Ordinal);
        Assert.Contains("Update sheet project", slideTexts[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_ProjectBriefPhotoEmphasis_UsesLargePhotographAndIndependentContextChoices()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 801,
            ProjectName = "PHOTO EMPHASIS PROJECT",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = StageCodes.DEVP,
            PresentStage = "Development",
            PresentStageOrder = ProjectBriefingStageOrder.Development,
            ExternalStatus = "Status recorded.",
            ProjectBrief = "A concise project brief that allows the photograph to remain the primary visual element on the slide.",
            CoverPhoto = TinyPng(),
            CoverPhotoContentType = "image/png",
            CostRd = new ProjectBriefingCostValue(5_000_000m, ProjectBriefingCostBasis.L1, "₹50 Lakh", "L1"),
            ProliferationCost = new ProjectBriefingCostValue(400_000m, ProjectBriefingCostBasis.Proliferation, "₹4 Lakh", "Proliferation"),
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 801,
            DeckName = "Photo Emphasis",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.Both,
            StandardSlideOptions = new ProjectBriefingStandardSlideOptions(
                ProjectBriefingProjectBriefLayout.PhotoEmphasis,
                ShowPresentStage: false,
                ShowPresentStatus: false,
                IncludeStageDistributionTable: false),
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(2, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var photographFrame = ShapeByName(slide, "Photo-emphasis project photograph");
        var text = SlideText(slide);

        Assert.True(ShapeWidth(photographFrame) >= 5.4 * 914400, "Photo-emphasis layout should allocate approximately half the slide to the photograph.");
        Assert.DoesNotContain("PRESENT STAGE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PRESENT STATUS", text, StringComparison.Ordinal);
        Assert.Contains("COST (R&D)", text, StringComparison.Ordinal);
        Assert.Contains("PROLIFERATION COST", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_ProjectBriefStandard_CanShowStatusWithoutPresentStage()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 802,
            ProjectName = "STATUS ONLY PROJECT",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = StageCodes.AON,
            PresentStage = "Acceptance of Necessity",
            PresentStageOrder = ProjectBriefingStageOrder.AcceptanceOfNecessity,
            ExternalStatus = "Current external status retained without displaying the present-stage field.",
            ProjectBrief = "The standard project-brief layout retains its wide narrative panel while project context remains configurable.",
            CoverPhoto = TinyPng(),
            CoverPhotoContentType = "image/png",
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 802,
            DeckName = "Status Only",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.None,
            StandardSlideOptions = new ProjectBriefingStandardSlideOptions(
                ProjectBriefingProjectBriefLayout.Standard,
                ShowPresentStage: false,
                ShowPresentStatus: true,
                IncludeStageDistributionTable: false),
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(2, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var text = SlideText(slide);

        Assert.Contains("PRESENT STATUS", text, StringComparison.Ordinal);
        Assert.Contains("Current external status retained", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PRESENT STAGE", text, StringComparison.Ordinal);
        Assert.NotNull(ShapeByName(slide, "Project brief photograph frame"));
    }

    [Fact]
    public void Compose_ProjectBrief_UsesOnePresentStatusHeadingAndOnlyAuthoritativeValues()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 804,
            ProjectName = "CONSOLIDATED STATUS PROJECT",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = StageCodes.DEVP,
            PresentStage = "Development",
            PresentStageOrder = ProjectBriefingStageOrder.Development,
            ExternalStatus = "Integration trials are underway.",
            ProjectBrief = "A project brief used to verify the consolidated status treatment.",
            CoverPhoto = TinyPng(),
            CoverPhotoContentType = "image/png",
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 804,
            DeckName = "Consolidated Status",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.None,
            StandardSlideOptions = new ProjectBriefingStandardSlideOptions(
                ProjectBriefingProjectBriefLayout.Standard,
                ShowPresentStage: true,
                ShowPresentStatus: true,
                IncludeStageDistributionTable: false),
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 4, 14, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var text = SlideText(slide);

        Assert.Equal(1, text.Split("PRESENT STATUS", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("PRESENT STAGE", text, StringComparison.Ordinal);
        Assert.Contains("Development · Integration trials are underway.", text, StringComparison.Ordinal);
        Assert.NotNull(ShapeByName(slide, "Project brief information strip"));
    }

    [Fact]
    public void Compose_ProjectBrief_OmitsMissingExternalStatusWithoutInventingRemarks()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 805,
            ProjectName = "STAGE ONLY PROJECT",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            LifecycleDisplay = "Completed",
            PresentStageCode = ProjectBriefingStageOrder.CompletedCode,
            PresentStage = "Completed",
            PresentStageOrder = ProjectBriefingStageOrder.Completed,
            ExternalStatus = "No external status recorded",
            ProjectBrief = "A project brief used to verify that missing status text is silently omitted.",
            CoverPhoto = TinyPng(),
            CoverPhotoContentType = "image/png",
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 805,
            DeckName = "Missing Status",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.None,
            StandardSlideOptions = new ProjectBriefingStandardSlideOptions(
                ProjectBriefingProjectBriefLayout.PhotoEmphasis,
                ShowPresentStage: true,
                ShowPresentStatus: true,
                IncludeStageDistributionTable: false),
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 4, 14, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, CompletedCount = 1 }
        };

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var text = SlideText(slide);

        Assert.Contains("PRESENT STATUS", text, StringComparison.Ordinal);
        Assert.Contains("Completed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("No external status recorded", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Available for proliferation", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_ProjectBrief_PlacesStatusAndSelectedCostsInOneSleekBottomStrip()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 806,
            ProjectName = "BOTTOM STRIP PROJECT",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = StageCodes.DEVP,
            PresentStage = "Development",
            PresentStageOrder = ProjectBriefingStageOrder.Development,
            ExternalStatus = "Prototype validation is in progress.",
            ProjectBrief = "A concise brief used to verify the unified bottom information strip.",
            CoverPhoto = TinyPng(),
            CoverPhotoContentType = "image/png",
            CostRd = new ProjectBriefingCostValue(5_000_000m, ProjectBriefingCostBasis.L1, "₹50 Lakh", "L1"),
            ProliferationCost = new ProjectBriefingCostValue(400_000m, ProjectBriefingCostBasis.Proliferation, "₹4 Lakh", "Proliferation"),
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 806,
            DeckName = "Bottom Strip",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.Both,
            StandardSlideOptions = new ProjectBriefingStandardSlideOptions(
                ProjectBriefingProjectBriefLayout.PhotoEmphasis,
                ShowPresentStage: true,
                ShowPresentStatus: true,
                IncludeStageDistributionTable: false),
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 4, 14, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var strip = ShapeByName(slide, "Project brief information strip");
        var photograph = ShapeByName(slide, "Photo-emphasis project photograph");
        var brief = ShapeByName(slide, "Project brief panel");
        var text = SlideText(slide);

        Assert.True(ShapeWidth(strip) >= 12.0 * 914400, "The combined status-and-cost strip should span the usable slide width.");
        Assert.True(ShapeHeight(strip) <= 1.05 * 914400, "The bottom information strip should remain sleek.");
        Assert.True(ShapeY(strip) >= ShapeY(photograph) + ShapeHeight(photograph));
        Assert.True(ShapeY(strip) >= ShapeY(brief) + ShapeHeight(brief));
        Assert.Contains("PRESENT STATUS", text, StringComparison.Ordinal);
        Assert.Contains("COST (R&D)", text, StringComparison.Ordinal);
        Assert.Contains("PROLIFERATION COST", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_ProjectBrief_LongCombinedStatusUsesTwoReadableLinesWithoutChangingAuthoritativeText()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 807,
            ProjectName = "LONG STATUS PROJECT",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = StageCodes.DEVP,
            PresentStage = "Development",
            PresentStageOrder = ProjectBriefingStageOrder.Development,
            ExternalStatus = "SO placed on 03 Feb 2026. Development is in progress. PDC is 03 Oct 2026.",
            ProjectBrief = "A concise project brief used to verify readable status-strip typography.",
            CoverPhoto = TinyPng(),
            CoverPhotoContentType = "image/png",
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 807,
            DeckName = "Long Status",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.Both,
            StandardSlideOptions = new ProjectBriefingStandardSlideOptions(
                ProjectBriefingProjectBriefLayout.PhotoEmphasis,
                ShowPresentStage: true,
                ShowPresentStatus: true,
                IncludeStageDistributionTable: false),
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 4, 14, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        var statusValue = ShapeByName(slide, "Project brief present status value");
        var paragraphs = statusValue.Descendants<A.Paragraph>().ToArray();
        var text = SlideText(slide);

        Assert.Equal(2, paragraphs.Length);
        Assert.Equal("Development", string.Concat(paragraphs[0].Descendants<A.Text>().Select(node => node.Text)));
        Assert.Equal(project.ExternalStatus, string.Concat(paragraphs[1].Descendants<A.Text>().Select(node => node.Text)));
        Assert.DoesNotContain("Available for proliferation", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No external status recorded", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_GraphiteProjectSlideUsesMutedMaroonHeaderAndCompactDarkBrandingPlates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pbd-dark-branding-{Guid.NewGuid():N}");
        var logoDirectory = Path.Combine(root, "wwwroot", "img", "logos");
        Directory.CreateDirectory(logoDirectory);
        File.WriteAllBytes(Path.Combine(logoDirectory, "artrac.png"), TinyPng());
        File.WriteAllBytes(Path.Combine(logoDirectory, "sdd.png"), TinyPng());

        try
        {
            var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
            var (content, _) = composer.Compose(BuildData(ProjectBriefingPresentationTheme.GraphiteDark));

            using var stream = new MemoryStream(content, writable: false);
            using var document = PresentationDocument.Open(stream, false);
            var slide = Assert.IsType<PresentationPart>(document.PresentationPart)
                .SlideParts
                .Single(part => SlideText(part).Contains("AURA", StringComparison.Ordinal)
                    && SlideText(part).Contains("CAPABILITY OVERVIEW", StringComparison.Ordinal));
            var leftPlate = ShapeByName(slide, "Left branding plate");
            var rightPlate = ShapeByName(slide, "Right branding plate");

            Assert.Equal("8A3042", ShapeFillColor(ShapeByName(slide, "Slide top accent")));
            Assert.Equal("242A34", ShapeFillColor(leftPlate));
            Assert.Equal("242A34", ShapeFillColor(rightPlate));
            Assert.True(ShapeWidth(leftPlate) <= .60 * 914400, "The dark left logo backing plate should remain optically compact.");
            Assert.True(ShapeWidth(rightPlate) <= .50 * 914400, "The dark right logo backing plate should remain optically compact.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Compose_AutomaticProjectBriefLayout_UsesPhotoEmphasisForConciseVisualProject()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 803,
            ProjectName = "AUTOMATIC VISUAL PROJECT",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = StageCodes.DEVP,
            PresentStage = "Development",
            PresentStageOrder = ProjectBriefingStageOrder.Development,
            ExternalStatus = "Status recorded.",
            ProjectBrief = "A concise visual project brief suitable for the automatic photo-emphasis composition.",
            CoverPhoto = TinyPng(),
            CoverPhotoContentType = "image/png",
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 803,
            DeckName = "Automatic Layout",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.None,
            StandardSlideOptions = ProjectBriefingStandardSlideOptions.Default,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        Assert.NotNull(ShapeByName(slide, "Photo-emphasis project photograph"));
    }

    [Fact]
    public void Compose_AutomaticProjectBriefLayout_UsesStandardDesignForLongNarrative()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var project = new ProjectBriefingPresentationProject
        {
            ProjectId = 804,
            ProjectName = "AUTOMATIC NARRATIVE PROJECT",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = StageCodes.DEVP,
            PresentStage = "Development",
            PresentStageOrder = ProjectBriefingStageOrder.Development,
            ExternalStatus = "A detailed current status is recorded for the project.",
            ProjectBrief = string.Join(" ", Enumerable.Repeat(
                "This detailed project narrative records the operational requirement, development approach, integration activities, validation methodology and intended employment.",
                14)),
            CoverPhoto = TinyPng(),
            CoverPhotoContentType = "image/png",
            SortOrder = 1
        };
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 804,
            DeckName = "Automatic Standard Layout",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.None,
            StandardSlideOptions = ProjectBriefingStandardSlideOptions.Default,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, _) = composer.Compose(data);

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = SingleContentSlide(Assert.IsType<PresentationPart>(document.PresentationPart));
        Assert.NotNull(ShapeByName(slide, "Project brief photograph frame"));
        Assert.DoesNotContain(
            slide.Slide.Descendants<P.NonVisualDrawingProperties>(),
            properties => string.Equals(properties.Name?.Value, "Photo-emphasis project photograph", StringComparison.Ordinal));
    }


    [Fact]
    public void Compose_PlacesModularInstitutionalProfileAfterCoverWithConfiguredAuthoritativeBreakdowns()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 880,
            DeckName = "SDD Institutional Profile",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            IncludeCoverSlide = true,
            IncludePortfolioSummarySlide = true,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            Projects = new[]
            {
                BriefingProject(
                    880,
                    "PROFILE SLIDE PROJECT",
                    StageCodes.DEVP,
                    ProjectBriefingStageOrder.Development,
                    1,
                    projectBrief: "Profile slide ordering regression coverage.")
            },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 },
            InstitutionalProfile = new ProjectBriefingInstitutionalProfileData
            {
                Title = "SDD – Growth over the years",
                DataAsOnUtc = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
                HistoryMilestones = new[]
                {
                    new ProjectBriefingInstitutionalHistoryMilestone(1986, "Conceptualised at MCEME"),
                    new ProjectBriefingInstitutionalHistoryMilestone(2024, "CoE (AR/VR)")
                },
                Modules = new[]
                {
                    new ProjectBriefingInstitutionalModuleData(
                        ProjectBriefingInstitutionalProfileModule.ProjectsDeveloped,
                        "Simulators/Projects Developed",
                        "160",
                        new[] { new ProjectBriefingInstitutionalMetricRow("AR/VR", "19") }),
                    new ProjectBriefingInstitutionalModuleData(
                        ProjectBriefingInstitutionalProfileModule.Proliferation,
                        "Proliferated",
                        "15,429",
                        new[] { new ProjectBriefingInstitutionalMetricRow("Firing Simulators", "10,081") }),
                    new ProjectBriefingInstitutionalModuleData(
                        ProjectBriefingInstitutionalProfileModule.TrainingSupport,
                        "Assistance to Field Formations",
                        "605",
                        new[] { new ProjectBriefingInstitutionalMetricRow("FY 2025-26", "150") },
                        "191 Units / 347 Individuals trained in AR/VR"),
                    new ProjectBriefingInstitutionalModuleData(
                        ProjectBriefingInstitutionalProfileModule.IntellectualProperty,
                        "Intellectual Property",
                        "21",
                        new[] { new ProjectBriefingInstitutionalMetricRow("Patents granted", "15") }),
                    new ProjectBriefingInstitutionalModuleData(
                        ProjectBriefingInstitutionalProfileModule.Partnerships,
                        "Military–Academia–Industry Synergy",
                        null,
                        new[] { new ProjectBriefingInstitutionalMetricRow("IIT Hyderabad", string.Empty) })
                },
                IncludeFooterStrip = true,
                FooterStripText = "GOC-in-C Unit Citations",
                FooterStripEmphasisValue = "03",
                FooterStripStyle = ProjectBriefingInstitutionalFooterStyle.Outline,
                FooterStripAlignment = ProjectBriefingInstitutionalFooterAlignment.Center
            }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(5, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        Assert.Equal(5, slides.Length);
        Assert.Contains("SDD Institutional Profile", SlideText(slides[0]), StringComparison.Ordinal);
        Assert.Contains("SDD – Growth over the years", SlideText(slides[1]), StringComparison.Ordinal);
        Assert.Contains("Portfolio at a glance", SlideText(slides[2]), StringComparison.Ordinal);
        Assert.Contains("PROFILE SLIDE PROJECT", SlideText(slides[3]), StringComparison.Ordinal);
        Assert.True(IsClosingSlide(slides[4]));

        var profileSlide = slides[1];
        var profileText = SlideText(profileSlide);

        Assert.Contains("SDD – Growth over the years", profileText, StringComparison.Ordinal);
        Assert.Contains("Conceptualised at MCEME", profileText, StringComparison.Ordinal);
        Assert.Contains("SIMULATORS / PROJECTS", profileText, StringComparison.Ordinal);
        Assert.Contains("DEVELOPED", profileText, StringComparison.Ordinal);
        Assert.Contains("15,429", profileText, StringComparison.Ordinal);
        Assert.Contains("FY 2025-26", profileText, StringComparison.Ordinal);
        Assert.Contains("191 Units / 347 Individuals", profileText, StringComparison.Ordinal);
        Assert.Contains("trained in AR/VR", profileText, StringComparison.Ordinal);
        Assert.Contains("IIT Hyderabad", profileText, StringComparison.Ordinal);
        Assert.Contains("GOC-in-C Unit Citations — 03", profileText, StringComparison.Ordinal);
        Assert.Contains("10,081", profileText, StringComparison.Ordinal);
        Assert.Contains("Data as on 04 Aug 2026 · Source: PRISM ERP", profileText, StringComparison.Ordinal);
        Assert.DoesNotContain("515 ABW", profileText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            profileSlide.Slide.Descendants<P.NonVisualDrawingProperties>(),
            properties => string.Equals(properties.Name?.Value, "SDD institutional history timeline", StringComparison.Ordinal));
        Assert.Contains(
            profileSlide.Slide.Descendants<P.NonVisualDrawingProperties>(),
            properties => string.Equals(properties.Name?.Value, "Proliferated institutional module", StringComparison.Ordinal));
        Assert.Contains(
            profileSlide.Slide.Descendants<P.NonVisualDrawingProperties>(),
            properties => string.Equals(properties.Name?.Value, "Military–Academia–Industry Synergy institutional module", StringComparison.Ordinal));
        Assert.Contains(
            profileSlide.Slide.Descendants<P.NonVisualDrawingProperties>(),
            properties => string.Equals(properties.Name?.Value, "SDD profile footer strip", StringComparison.Ordinal));
        Assert.Equal(7, profileSlide.Slide.Descendants<P.GroupShape>().Count());
        Assert.Empty(profileSlide.Slide.Descendants<P.GraphicFrame>());
        Assert.Empty(profileSlide.Slide.Descendants<A.Table>());
        Assert.Contains(
            profileSlide.Slide.Descendants<P.NonVisualDrawingProperties>(),
            properties => string.Equals(properties.Name?.Value, "Slide title", StringComparison.Ordinal));
        Assert.Contains(
            profileSlide.Slide.Descendants<P.NonVisualDrawingProperties>(),
            properties => string.Equals(properties.Name?.Value, "Slide top accent", StringComparison.Ordinal));
        Assert.DoesNotContain(
            profileSlide.Slide.Descendants<P.NonVisualDrawingProperties>(),
            properties => string.Equals(properties.Name?.Value, "SDD profile title band", StringComparison.Ordinal));
        Assert.True(
            ShapeWidth(ShapeByName(profileSlide, "SDD profile footer-strip background")) < 10.0 * 914400,
            "Short profile-footer content should render as a compact centred strip rather than a full-width bar.");
        Assert.True(
            ShapeHeight(ShapeByName(profileSlide, "SDD profile footer-strip background")) < .37 * 914400,
            "The supporting profile footer strip should remain visually subordinate to the five institutional modules.");
        Assert.NotEqual(
            "7A263A",
            ShapeLineColor(ShapeByName(profileSlide, "SDD profile footer-strip background")));
        Assert.Equal(
            "D6E9E9",
            ShapeFillColor(ShapeByName(profileSlide, "Simulators/Projects Developed module header fill")));
        Assert.True(
            MaxShapeFontSize(ShapeByName(profileSlide, "Proliferated module labels")) >= 960,
            "Institutional detail labels must remain readable when projected.");
        Assert.True(
            MaxShapeFontSize(ShapeByName(profileSlide, "Proliferated module values")) >= 990,
            "Institutional detail values must remain readable when projected.");
        Assert.True(
            MaxShapeFontSize(ShapeByName(profileSlide, "Military–Academia–Industry Synergy module content")) >= 1000,
            "Institutional-partnership entries should use the larger sparse-module typography.");
        Assert.True(
            ShapeHeight(ShapeByName(profileSlide, "Assistance to Field Formations module highlight fill")) >= .49 * 914400,
            "The training highlight must provide sufficient height for a controlled two-line message.");
    }

    [Fact]
    public void Compose_ProjectUpdateSheet_OmitsInstitutionalProfileOnlyWhenNoProfileDataIsProvided()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 881,
            DeckName = "Update Sheet Without Profile",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            IncludeCoverSlide = true,
            IncludePortfolioSummarySlide = true,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 5, 4, 0, 0, TimeSpan.Zero),
            Projects = new[]
            {
                BriefingProject(
                    881,
                    "NO PROFILE PROJECT",
                    StageCodes.DEVP,
                    ProjectBriefingStageOrder.Development,
                    1,
                    projectBrief: "Institutional profile opt-out regression coverage.")
            },
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 1,
                OngoingCount = 1
            },
            InstitutionalProfile = null
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(4, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        Assert.Equal(4, slides.Length);
        Assert.DoesNotContain(slides, slide =>
            SlideText(slide).Contains("SDD – Growth over the years", StringComparison.Ordinal));
        Assert.Contains("Portfolio at a glance", SlideText(slides[1]), StringComparison.Ordinal);
        Assert.Contains("NO PROFILE PROJECT", SlideText(slides[2]), StringComparison.Ordinal);
        Assert.True(IsClosingSlide(slides[3]));
    }

    [Fact]
    public void Compose_AppendsProfessionalJaiHindClosingSlideByDefault()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 901,
            DeckName = "Closing Slide Review",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            Projects = new[]
            {
                BriefingProject(
                    901,
                    "CLOSING SLIDE PROJECT",
                    StageCodes.DEVP,
                    ProjectBriefingStageOrder.Development,
                    1,
                    projectBrief: "Closing slide regression coverage.")
            },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(2, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        var closing = slides[^1];

        Assert.Equal("JAI HIND", ShapeByName(closing, "Closing message")
            .Descendants<A.Text>().Single().Text);

        var panel = ShapeByName(closing, "Closing ceremonial panel");
        var organisation = ShapeByName(closing, "Closing organisation");
        var saffron = ShapeByName(closing, "Closing saffron accent");
        var white = ShapeByName(closing, "Closing white accent");
        var green = ShapeByName(closing, "Closing green accent");
        var panelAdjustment = Assert.Single(panel.ShapeProperties!
            .PresetGeometry!
            .AdjustValueList!
            .Elements<A.ShapeGuide>());

        Assert.Equal("7A263A", ShapeFillColor(panel));
        Assert.True(ShapeWidth(panel) >= 11.70 * 914400, "The ceremonial field should use the slide width confidently.");
        Assert.Equal("val 6000", panelAdjustment.Formula?.Value);
        Assert.Equal(ShapeWidth(saffron), ShapeWidth(white));
        Assert.Equal(ShapeWidth(white), ShapeWidth(green));
        Assert.True(ShapeWidth(saffron) <= 1.12 * 914400, "The tricolour accent should remain short and ceremonial.");
        Assert.True(ShapeHeight(saffron) <= .045 * 914400, "The tricolour accent should remain fine rather than bar-like.");
        Assert.True(ShapeY(organisation) > ShapeY(green), "The organisation name should follow the tricolour accent.");
        Assert.DoesNotContain("PROJECT BRIEFING DECK", SlideText(closing), StringComparison.Ordinal);
        Assert.DoesNotContain(closing.Slide.Descendants<P.NonVisualDrawingProperties>(), properties =>
            string.Equals(properties.Name?.Value, "Left branding plate", StringComparison.Ordinal)
            || string.Equals(properties.Name?.Value, "Right branding plate", StringComparison.Ordinal));
        Assert.DoesNotContain("2/2", SlideText(closing), StringComparison.Ordinal);
        Assert.DoesNotContain("SIMULATOR DEVELOPMENT DIVISION\n2/2", SlideText(closing), StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_GraphiteClosingSlideUsesCompactBorderlessLogoPlates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pbd-closing-branding-{Guid.NewGuid():N}");
        var logoDirectory = Path.Combine(root, "wwwroot", "img", "logos");
        Directory.CreateDirectory(logoDirectory);
        File.WriteAllBytes(Path.Combine(logoDirectory, "artrac.png"), TinyPng());
        File.WriteAllBytes(Path.Combine(logoDirectory, "sdd.png"), TinyPng());

        try
        {
            var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
            var (content, _) = composer.Compose(BuildData(ProjectBriefingPresentationTheme.GraphiteDark));

            using var stream = new MemoryStream(content, writable: false);
            using var document = PresentationDocument.Open(stream, false);
            var closing = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.Last();
            var leftPlate = ShapeByName(closing, "Left branding plate");
            var rightPlate = ShapeByName(closing, "Right branding plate");

            Assert.Equal("242A34", ShapeFillColor(leftPlate));
            Assert.Equal("242A34", ShapeFillColor(rightPlate));
            Assert.True(ShapeWidth(leftPlate) <= .65 * 914400, "The closing-slide left logo plate should remain compact.");
            Assert.True(ShapeWidth(rightPlate) <= .60 * 914400, "The closing-slide right logo plate should remain compact.");
            Assert.NotEmpty(leftPlate.Descendants<A.NoFill>());
            Assert.NotEmpty(rightPlate.Descendants<A.NoFill>());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Compose_UsesSelectedThankYouClosingMessage()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var data = new ProjectBriefingPresentationData
        {
            DeckId = 902,
            DeckName = "External Audience Review",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.None,
            ClosingSlideType = ProjectBriefingClosingSlideType.ThankYou,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            Projects = new[]
            {
                BriefingProject(
                    902,
                    "THANK YOU PROJECT",
                    StageCodes.DEVP,
                    ProjectBriefingStageOrder.Development,
                    1,
                    projectBrief: "External audience closing slide coverage.")
            },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(2, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var closing = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.Last();

        Assert.Contains("THANK YOU", SlideText(closing), StringComparison.Ordinal);
        Assert.DoesNotContain("JAI HIND", SlideText(closing), StringComparison.Ordinal);
    }


    [Fact]
    public void Compose_ProjectUpdateSheet_LongBriefUsesReadableContinuationAndCropFilledPhoto()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var longBrief = string.Join(
            " ",
            Enumerable.Repeat(
                "The simulator provides realistic mission-oriented training, supports repeatable instructional practice and records performance for structured after-action review.",
                18));
        var project = BriefingProject(
            995,
            "Readable Update Sheet",
            StageCodes.DEVP,
            ProjectBriefingStageOrder.Development,
            1,
            projectBrief: longBrief);
        project.CoverPhoto = TinyPng();
        project.CoverPhotoContentType = "image/png";

        var data = new ProjectBriefingPresentationData
        {
            DeckId = 995,
            DeckName = "Readable Update Sheet",
            Layout = ProjectBriefingLayout.ProjectUpdateSheet,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            UpdateSheetOptions = new ProjectBriefingUpdateSheetOptions(
                new[]
                {
                    ProjectBriefingUpdateSheetRow.ProjectCost,
                    ProjectBriefingUpdateSheetRow.PresentStatus
                },
                HideEmptyValues: false),
            Projects = new[] { project },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

        var (content, slideCount) = composer.Compose(data);

        Assert.Equal(3, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var presentationPart = Assert.IsType<PresentationPart>(document.PresentationPart);
        var contentSlides = presentationPart.SlideParts.Where(slide => !IsClosingSlide(slide)).ToArray();
        Assert.Equal(2, contentSlides.Length);

        var firstSlide = contentSlides.Single(slide =>
            slide.Slide.Descendants<P.NonVisualDrawingProperties>()
                .Any(properties => string.Equals(properties.Name?.Value, "Project brief", StringComparison.Ordinal)));
        var continuationSlide = contentSlides.Single(slide =>
            SlideText(slide).Contains("BRIEF OF THE PROJECT — CONTINUED", StringComparison.Ordinal));

        var briefShape = ShapeByName(firstSlide, "Project brief");
        var briefFontSizes = briefShape.Descendants<A.RunProperties>()
            .Select(properties => properties.FontSize?.Value)
            .Where(size => size.HasValue)
            .Select(size => size!.Value)
            .ToArray();
        Assert.NotEmpty(briefFontSizes);
        Assert.All(briefFontSizes, size => Assert.True(size >= 1200, "Update-sheet narrative text must not shrink below 12 pt."));
        Assert.Null(briefShape.TextBody?.BodyProperties?.GetFirstChild<A.NormalAutoFit>());
        Assert.Contains("CONTINUED", SlideText(continuationSlide), StringComparison.Ordinal);

        var photoFrame = ShapeByName(firstSlide, "Project photograph frame - Compact");
        var photo = firstSlide.Slide.Descendants<P.Picture>()
            .Single(picture => string.Equals(
                picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value,
                "Readable Update Sheet photograph",
                StringComparison.Ordinal));
        var photoExtent = Assert.IsType<A.Extents>(photo.ShapeProperties?.Transform2D?.Extents);
        Assert.True(
            (photoExtent.Cy?.Value ?? 0L) >= ShapeHeight(photoFrame) - (.20 * 914400),
            "The project photograph should crop to fill the available panel instead of leaving large unused bands.");
    }

    private static byte[] TinyPng()
        => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z1ZsAAAAASUVORK5CYII=");

    private static ProjectBriefingPresentationProject BriefingProject(
        int id,
        string name,
        string stageCode,
        int stageOrder,
        int sortOrder,
        ProjectLifecycleStatus lifecycleStatus = ProjectLifecycleStatus.Active,
        string? projectBrief = null)
        => new()
        {
            ProjectId = id,
            ProjectName = name,
            LifecycleStatus = lifecycleStatus,
            LifecycleDisplay = lifecycleStatus == ProjectLifecycleStatus.Completed ? "Completed" : "Ongoing",
            PresentStageCode = stageCode,
            PresentStage = lifecycleStatus == ProjectLifecycleStatus.Completed ? "Completed" : stageCode,
            PresentStageOrder = stageOrder,
            CostRd = ProjectBriefingCostValue.Missing(),
            IpaCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.IPA),
            ProliferationCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
            ExternalStatus = "Status recorded.",
            BriefDescription = "Capability description for ordering regression coverage.",
            ProjectBrief = projectBrief ?? string.Empty,
            SortOrder = sortOrder
        };

    private static SlidePart SingleContentSlide(PresentationPart presentationPart)
        => Assert.Single(presentationPart.SlideParts.Where(slide => !IsClosingSlide(slide)));

    private static bool IsClosingSlide(SlidePart slide)
        => slide.Slide.Descendants<P.NonVisualDrawingProperties>()
            .Any(properties => string.Equals(
                properties.Name?.Value,
                "Closing message",
                StringComparison.Ordinal));

    private static P.Shape ShapeByName(SlidePart slide, string name)
        => Assert.Single(slide.Slide.Descendants<P.Shape>().Where(shape => string.Equals(
            shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value,
            name,
            StringComparison.Ordinal)));

    private static string ShapeTextColor(P.Shape shape)
        => Assert.IsType<A.RgbColorModelHex>(shape
                .Descendants<A.RunProperties>()
                .SelectMany(properties => properties.Descendants<A.RgbColorModelHex>())
                .FirstOrDefault())
            .Val?.Value
            ?? string.Empty;

    private static string ShapeFillColor(P.Shape shape)
        => shape.ShapeProperties?
               .Descendants<A.RgbColorModelHex>()
               .FirstOrDefault()?
               .Val?.Value
           ?? string.Empty;

    private static string ShapeLineColor(P.Shape shape)
        => shape.ShapeProperties?
               .Descendants<A.Outline>()
               .SelectMany(outline => outline.Descendants<A.RgbColorModelHex>())
               .FirstOrDefault()?
               .Val?.Value
           ?? string.Empty;

    [Fact]
    public void Compose_RoleAndCharter_UsesConfiguredOrderGroupedShapesAndContinuationSlides()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var charterItems = Enumerable.Range(1, 13)
            .Select(index => new ProjectBriefingRoleCharterEntry(
                $"Charter {index}",
                $"Authorised charter responsibility number {index}"))
            .ToArray();
        var roleCharter = new ProjectBriefingRoleCharterData
        {
            Title = "Role & Charter",
            Layout = ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter,
            RoleStatements = new[]
            {
                new ProjectBriefingRoleCharterEntry(
                    "Nodal Centre",
                    "Development of specified simulators, robotics and AI products for the Indian Army")
            },
            CharterItems = charterItems
        };

        var (content, slideCount) = composer.Compose(BuildData(
            roleCharter: roleCharter,
            additionalSlideOrder: new[] { ProjectBriefingAdditionalSlideType.RoleAndCharter }));

        Assert.Equal(10, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        var roleSlides = slides
            .Where(slide => SlideText(slide).Contains("ROLE & CHARTER", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(2, roleSlides.Length);
        Assert.Contains("Nodal Centre", SlideText(roleSlides[0]), StringComparison.Ordinal);
        Assert.Contains("CHARTER — CONTINUED", SlideText(roleSlides[1]), StringComparison.Ordinal);
        Assert.All(roleSlides, slide => Assert.Empty(slide.Slide.Descendants<A.Table>()));
        Assert.All(roleSlides, slide => Assert.NotEmpty(slide.Slide.Descendants<P.GroupShape>()));
        var rolePanel = ShapeByName(roleSlides[0], "Role panel background");
        var charterHeading = ShapeByName(roleSlides[0], "Charter heading");
        var charterPanel = ShapeByName(roleSlides[0], "Charter items left column background");
        Assert.Equal("F8E8E8", ShapeFillColor(rolePanel));
        Assert.True(ShapeHeight(rolePanel) <= 1.15 * 914400, "The Role panel should remain compact.");
        Assert.True(
            ShapeY(charterHeading) >= ShapeY(rolePanel) + ShapeHeight(rolePanel) + (.10 * 914400),
            "The Charter heading must have a clear gap below the Role panel.");
        Assert.True(ShapeHeight(charterPanel) <= 3.80 * 914400, "Charter panels must remain within the approved content region.");
        var roleCharterText = string.Join("\n", roleSlides.Select(SlideText));
        foreach (var item in charterItems)
        {
            Assert.Contains(item.LeadPhrase, roleCharterText, StringComparison.Ordinal);
        }

        var allText = slides.Select(SlideText).ToArray();
        var coverIndex = Array.FindIndex(allText, text => text.Contains("QUARTERLY COMMAND REVIEW", StringComparison.Ordinal));
        var roleIndex = Array.FindIndex(allText, text => text.Contains("ROLE & CHARTER", StringComparison.OrdinalIgnoreCase));
        var summaryIndex = Array.FindIndex(allText, text => text.Contains("PORTFOLIO AT A GLANCE", StringComparison.OrdinalIgnoreCase));
        Assert.True(coverIndex >= 0 && roleIndex > coverIndex && summaryIndex > roleIndex);
    }


    [Fact]
    public void Compose_FfcGlobalFootprint_IsFinalSubstantiveSlideBeforeClosing()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var footprint = new ProjectBriefingFfcGlobalFootprintData
        {
            Title = "FFC Global Footprint",
            Layout = ProjectBriefingFfcGlobalFootprintLayout.MapWithCountryBreakdown,
            IncludeCountryWiseBreakdown = false,
            Summary = new FfcFootprintSummary(
                CountryCount: 2,
                RecordCount: 2,
                ProjectCount: 5,
                InstalledUnits: 3,
                DeliveredNotInstalledUnits: 2,
                PlannedUnits: 7),
            Countries = new[]
            {
                new ProjectBriefingFfcCountryData("Ethiopia", "ETH", 4, 0, 0, 7),
                new ProjectBriefingFfcCountryData("Sri Lanka", "LKA", 1, 3, 2, 0)
            },
            MaximumCountryRows = 8,
            MapImage = TinyPng(),
            DataAsOnUtc = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero)
        };

        var (content, _) = composer.Compose(BuildData(
            additionalSlideOrder: new[] { ProjectBriefingAdditionalSlideType.FfcGlobalFootprint },
            ffcGlobalFootprint: footprint));

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        var footprintIndex = Array.FindIndex(slides, slide =>
            SlideText(slide).Contains("FFC GLOBAL FOOTPRINT", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(slides.Length - 2, footprintIndex);
        Assert.True(IsClosingSlide(slides[^1]));
        var footprintText = SlideText(slides[footprintIndex]);
        Assert.Contains("DELIVERED, AWAITING INSTALLATION", footprintText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNTRY-WISE BREAKDOWN", footprintText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QTY", footprintText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Data as on 21 Jul 2026 · Source: PRISM ERP", footprintText, StringComparison.Ordinal);
        Assert.DoesNotContain("Data as on 19 Jul 2026", footprintText, StringComparison.Ordinal);
        Assert.Contains("ETH", footprintText, StringComparison.Ordinal);
        Assert.NotEmpty(slides[footprintIndex].Slide.Descendants<P.Picture>());
        Assert.Equal("7A263A", ShapeFillColor(ShapeByName(slides[footprintIndex], "Slide top accent")));

        var mapFrame = ShapeByName(slides[footprintIndex], "FFC footprint map frame");
        var countryPanel = ShapeByName(slides[footprintIndex], "FFC country-wise breakdown panel");
        var combinedWidth = ShapeWidth(mapFrame) + ShapeWidth(countryPanel);
        var mapShare = ShapeWidth(mapFrame) / (double)combinedWidth;
        Assert.InRange(mapShare, .71, .73);
        Assert.InRange(ShapeWidth(countryPanel), 3.20 * 914400, 3.40 * 914400);
        Assert.True(ShapeWidth(mapFrame) >= 8.30 * 914400, "The footprint map must remain the dominant visual.");
        Assert.True(ShapeHeight(mapFrame) >= 4.25 * 914400, "Footprint body should use the available vertical space.");
        Assert.Equal("AEB6C2", ShapeFillColor(ShapeByName(slides[footprintIndex], "Ethiopia planned quantity")));
        Assert.Equal("E9ECEF", ShapeFillColor(ShapeByName(slides[footprintIndex], "Sri Lanka quantity bar background")));
    }


    [Fact]
    public void Compose_StageDistributionTable_IsOptionalAndChartRemains()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));

        var (content, slideCount) = composer.Compose(BuildData(includeStageDistributionTable: false));

        Assert.Equal(7, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var text = string.Join("\n", Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts.Select(SlideText));
        Assert.Contains("Stage-wise summary", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Stage-wise project distribution", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_TechnicalCategorySummary_IsSuppressedForSingleCategory()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var singleCategory = new[] { new ProjectBriefingSummaryPoint("AR/VR", 2) };

        var (content, slideCount) = composer.Compose(BuildData(
            includeTechnicalCategorySummary: true,
            technicalCategorySummary: singleCategory));

        Assert.Equal(8, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var text = string.Join("\n", Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts.Select(SlideText));
        Assert.DoesNotContain("Technical-category summary", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_FfcMapDominantWithBreakdown_GeneratesContiguousPaginatedBlock()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot");
        var composer = new ProjectBriefingSlideComposer(new TestEnvironment(root));
        var countries = Enumerable.Range(1, 13)
            .Select(index => new ProjectBriefingFfcCountryData(
                $"Country {index}",
                $"C{index:00}",
                ProjectCount: index,
                InstalledUnits: index,
                DeliveredNotInstalledUnits: index % 3,
                PlannedUnits: index + 1))
            .ToArray();
        var footprint = new ProjectBriefingFfcGlobalFootprintData
        {
            Title = "FFC Global Footprint",
            Layout = ProjectBriefingFfcGlobalFootprintLayout.MapDominant,
            IncludeCountryWiseBreakdown = true,
            Summary = new FfcFootprintSummary(13, 13, 91, 91, 13, 104),
            Countries = countries,
            MaximumCountryRows = 8,
            MapImage = TinyPng(),
            DataAsOnUtc = new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero)
        };

        var (content, slideCount) = composer.Compose(BuildData(
            additionalSlideOrder: new[] { ProjectBriefingAdditionalSlideType.FfcGlobalFootprint },
            ffcGlobalFootprint: footprint));

        Assert.Equal(11, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slides = Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts.ToArray();
        Assert.True(IsClosingSlide(slides[^1]));
        Assert.Contains("FFC GLOBAL FOOTPRINT", SlideText(slides[^4]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FFC – COUNTRY-WISE BREAKDOWN", SlideText(slides[^3]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CONTINUED", SlideText(slides[^2]), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(ShapeByName(slides[^4], "FFC map-dominant frame"));
        Assert.Single(slides[^3].Slide.Descendants<A.Table>());
        Assert.Single(slides[^2].Slide.Descendants<A.Table>());
        Assert.Contains("Country 13", SlideText(slides[^2]), StringComparison.Ordinal);
    }

    private static int MaxShapeFontSize(P.Shape shape)
        => shape
            .Descendants<A.RunProperties>()
            .Select(properties => properties.FontSize?.Value ?? 0)
            .DefaultIfEmpty(0)
            .Max();

    private static long ShapeX(P.Shape shape)
        => shape.ShapeProperties?.Transform2D?.Offset?.X?.Value ?? 0L;

    private static long ShapeY(P.Shape shape)
        => shape.ShapeProperties?.Transform2D?.Offset?.Y?.Value ?? 0L;

    private static long ShapeWidth(P.Shape shape)
        => shape.ShapeProperties?.Transform2D?.Extents?.Cx?.Value ?? 0L;

    private static long ShapeHeight(P.Shape shape)
        => shape.ShapeProperties?.Transform2D?.Extents?.Cy?.Value ?? 0L;

    private static string SlideText(SlidePart slide)
        => string.Join("\n", slide.Slide.Descendants<A.Text>().Select(node => node.Text));

    private static void AssertProjectSequence(string text, params string[] projectNames)
    {
        var previous = -1;
        foreach (var projectName in projectNames)
        {
            var index = text.IndexOf(projectName, StringComparison.Ordinal);
            Assert.True(index > previous, $"Expected {projectName} after the preceding project in the generated sequence.");
            previous = index;
        }
    }

    private static ProjectBriefingPresentationData BuildData(
        ProjectBriefingPresentationTheme presentationTheme = ProjectBriefingPresentationTheme.EditorialLight,
        ProjectBriefingBrandingScope brandingScope = ProjectBriefingBrandingScope.AllSlides,
        ProjectBriefingRoleCharterData? roleCharter = null,
        IReadOnlyList<ProjectBriefingAdditionalSlideType>? additionalSlideOrder = null,
        ProjectBriefingFfcGlobalFootprintData? ffcGlobalFootprint = null,
        bool includeStageDistributionTable = true,
        bool includeTechnicalCategorySummary = false,
        IReadOnlyList<ProjectBriefingSummaryPoint>? technicalCategorySummary = null)
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
            StandardSlideOptions = new ProjectBriefingStandardSlideOptions(
                ProjectBriefingProjectBriefLayout.Automatic,
                ShowPresentStage: true,
                ShowPresentStatus: true,
                IncludeStageDistributionTable: includeStageDistributionTable),
            PresentationTheme = presentationTheme,
            BrandingScope = brandingScope,
            AdditionalSlideOrder = additionalSlideOrder
                ?? new[] { ProjectBriefingAdditionalSlideType.InstitutionalProfile },
            RoleCharter = roleCharter,
            FfcGlobalFootprint = ffcGlobalFootprint,
            IncludeStageSummary = true,
            IncludeTechnicalCategorySummary = includeTechnicalCategorySummary,
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
                StageSummary = ProjectBriefingStageOrder.BuildSummary(
                    projects.Select(project => project.PresentStageOrder)),
                OngoingStageSummary = ProjectBriefingStageOrder.BuildSummary(
                    projects
                        .Where(project => project.LifecycleStatus == ProjectLifecycleStatus.Active)
                        .Select(project => project.PresentStageOrder)),
                TechnicalCategorySummary = technicalCategorySummary ?? Array.Empty<ProjectBriefingSummaryPoint>()
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
