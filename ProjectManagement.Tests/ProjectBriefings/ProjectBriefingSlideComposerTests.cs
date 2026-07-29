using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Models.Stages;
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

        Assert.Equal(3, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var text = string.Join("\n", Assert.IsType<PresentationPart>(document.PresentationPart)
            .SlideParts
            .SelectMany(slide => slide.Slide.Descendants<A.Text>())
            .Select(node => node.Text));

        Assert.Contains("PROJECT BRIEF", text, StringComparison.Ordinal);
        Assert.Contains("operational need", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CAPABILITY OVERVIEW", text, StringComparison.Ordinal);
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

        Assert.Equal(4, slideCount);
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
        Assert.All(slides.Skip(1), slide => Assert.Contains(
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

        Assert.Equal(9, slideCount); // cover + portfolio + seven project-table slides
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

        Assert.Equal(3, slideCount);
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

        Assert.Equal(1, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var presentationPart = Assert.IsType<PresentationPart>(document.PresentationPart);
        Assert.Equal(12192000, presentationPart.Presentation.SlideSize?.Cx?.Value);
        Assert.Equal(6858000, presentationPart.Presentation.SlideSize?.Cy?.Value);
        var slide = Assert.Single(presentationPart.SlideParts);
        var text = SlideText(slide);

        Assert.DoesNotContain("PROJECT UPDATE SHEET", text, StringComparison.Ordinal);
        Assert.Contains("Touch Screen Based Simulator", text, StringComparison.Ordinal);
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
        Assert.DoesNotContain("B5122B", slide.Slide.OuterXml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("8F0D21", slide.Slide.OuterXml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Left formation insignia", slide.Slide.OuterXml, StringComparison.Ordinal);
        Assert.Contains("Right division insignia", slide.Slide.OuterXml, StringComparison.Ordinal);
        Assert.DoesNotContain("Compact footer insignia", slide.Slide.OuterXml, StringComparison.Ordinal);
        Assert.Equal(2, slide.ImageParts.Count());
        Assert.Single(slide.Slide.Descendants<A.Table>());
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
            CostRd = new ProjectBriefingCostValue(10_000_000m, ProjectBriefingCostBasis.Aon, "₹1.00 Cr", "AoN"),
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

        Assert.Equal(1, slideCount);
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var slide = Assert.Single(Assert.IsType<PresentationPart>(document.PresentationPart).SlideParts);
        var text = SlideText(slide);
        Assert.Contains("PDC Date", text, StringComparison.Ordinal);
        Assert.DoesNotContain("15 Jan 30", text, StringComparison.Ordinal);
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

        Assert.Equal(3, slideCount);
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
                StageSummary = ProjectBriefingStageOrder.BuildSummary(
                    projects.Select(project => project.PresentStageOrder))
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
