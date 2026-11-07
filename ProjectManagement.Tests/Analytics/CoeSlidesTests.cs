using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Analytics.Presentation;
using Xunit;

namespace ProjectManagement.Tests.Analytics;

/// <summary>The CoE (AR/VR) slides download: what each slide says, and that the deck opens.</summary>
public sealed class CoeSlidesTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 6);

    [Fact]
    public void ToSlideProject_TagsTheStage_AndAddsThePdcInDevelopment()
    {
        var completed = CoeSlidesDataBuilder.ToSlideProject(new CoeProjectSummaryVm(1, "A", "Completed", "Development", StageCodes.DEVP, new DateOnly(2027, 3, 31)), AsOf);
        Assert.Equal(("Completed", true, (string?)null), (completed.Tag, completed.IsCompleted, completed.Pdc));

        var supplyOrder = CoeSlidesDataBuilder.ToSlideProject(new CoeProjectSummaryVm(2, "B", "Ongoing", "Supply Order", StageCodes.SO, new DateOnly(2027, 1, 1)), AsOf);
        Assert.Equal(("SO", (string?)null), (supplyOrder.Tag, supplyOrder.Pdc)); // a PDC only for Development

        var development = CoeSlidesDataBuilder.ToSlideProject(new CoeProjectSummaryVm(3, "C", "Ongoing", "Development", StageCodes.DEVP, new DateOnly(2027, 3, 31)), AsOf);
        Assert.Equal(("Devp", "PDC 31 Mar 2027", false), (development.Tag, development.Pdc, development.PdcOverdue));

        var noPdc = CoeSlidesDataBuilder.ToSlideProject(new CoeProjectSummaryVm(4, "D", "Ongoing", "Development", StageCodes.DEVP), AsOf);
        Assert.Null(noPdc.Pdc);

        var noStage = CoeSlidesDataBuilder.ToSlideProject(new CoeProjectSummaryVm(5, "E", "Ongoing", "—"), AsOf);
        Assert.Equal("Ongoing", noStage.Tag);
    }

    [Fact]
    public void ToSlideProject_APassedDevelopmentPdc_IsOverdue_ButNotOneDueToday()
    {
        var late = CoeSlidesDataBuilder.ToSlideProject(new CoeProjectSummaryVm(1, "Late", "Ongoing", "Development", StageCodes.DEVP, new DateOnly(2026, 6, 22)), AsOf);
        var dueToday = CoeSlidesDataBuilder.ToSlideProject(new CoeProjectSummaryVm(2, "Today", "Ongoing", "Development", StageCodes.DEVP, AsOf), AsOf);

        Assert.Equal(("PDC 22 Jun 2026 · overdue", true), (late.Pdc, late.PdcOverdue));
        Assert.Equal(("PDC 6 Nov 2026", false), (dueToday.Pdc, dueToday.PdcOverdue));
    }

    [Fact]
    public void LineCount_WrapsAtWordBreaks()
    {
        Assert.Equal(1, CoeSlideComposer.LineCount("VR Bailey Bridge Sml", 3.0, 11));
        // About 39 characters a line at 11 pt in 3 in: this name takes two lines, and is never cut.
        Assert.Equal(2, CoeSlideComposer.LineCount("Augmented Reality-based Unified Reconnaissance Trainer", 3.0, 11));
        Assert.Equal(3, CoeSlideComposer.LineCount("VR based Physiological Response Evaluation System (Stress Detection Simulator)", 2.4, 11));
    }

    [Fact]
    public void Build_LeavesOutCancelled_OrdersOngoingByStageThenCompleted_AndPutsNoSubCategoryLast()
    {
        var data = CoeSlidesDataBuilder.Build(AsOf, SampleCoe(), impact: null);

        Assert.Equal(new[] { "CoE Mid Term", "CoE Short Term", "CoE (no sub-category)" }, data.Columns.Select(column => column.Name));
        var mid = data.Columns[0];
        // Furthest stage first (Development before Feasibility), then completed; the cancelled project is left out.
        Assert.Equal(new[] { "VR Gunnery", "AR Maint", "VR Driving" }, mid.Projects.Select(project => project.Name));
        Assert.Equal(("Devp", "PDC 31 Mar 2027"), (mid.Projects[0].Tag, mid.Projects[0].Pdc));
        Assert.True(mid.Projects[2].IsCompleted);
        Assert.Equal(new[] { "VR Dropped" }, data.CancelledLeftOut);
        Assert.Equal(5, data.CompletedTotal);

        // Slide 2 lists every completed AR/VR project, newest first, then by name; the year is the tag.
        Assert.Equal(
            new[] { ("VR Navigation", "2026"), ("VR Driving", "2025"), ("AR Medical", "2022"), ("VR Sniper", "2022"), ("VR Old", "—") },
            data.CompletedProjects.Select(project => (project.Name, project.Tag)));

        // Without the tracker policies, training and proliferation are not on the slide.
        Assert.Null(data.Training);
        Assert.Null(data.Proliferation);
    }

    [Fact]
    public void Build_TrainingAndProliferationFollowTheTrackerPolicies()
    {
        var impact = new ArVrImpactVm
        {
            CanViewTraining = true,
            CanViewProliferation = false,
            TrainingCourses = 3,
            TrainingPeople = 60,
            TrainingByYear = new[]
            {
                new ArVrTrainingYearVm(2024, "2024-25", 2, 10, 5, 25, 1),
                new ArVrTrainingYearVm(2025, "2025-26", 1, 5, 5, 10, 1)
            }
        };

        var data = CoeSlidesDataBuilder.Build(AsOf, SampleCoe(), impact);

        Assert.Equal(new CoeSlideTraining(3, 60, 15, 10, 35), data.Training);
        Assert.Null(data.Proliferation);
    }

    [Fact]
    public void Compose_TwoWidescreenSlides_WithTheCategoriesAndTheSummary()
    {
        var impact = new ArVrImpactVm
        {
            CanViewTraining = true,
            CanViewProliferation = true,
            TrainingCourses = 1,
            TrainingPeople = 20,
            TrainingByYear = new[] { new ArVrTrainingYearVm(2025, "2025-26", 1, 5, 5, 10, 1) },
            ProliferationUnitsDelivered = 7,
            ProliferationReceivingUnits = 4,
            ProliferationByYear = new[] { new ArVrProliferationYearVm(2025, 5, 2, 2, 4) }
        };
        var composer = new CoeSlideComposer(new TestEnvironment(Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", "PresentationRoot")));

        var content = composer.Compose(CoeSlidesDataBuilder.Build(AsOf, SampleCoe(), impact));

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var presentation = Assert.IsType<PresentationPart>(document.PresentationPart);
        var slideIds = presentation.Presentation.SlideIdList!.Elements<SlideId>().ToArray();
        Assert.Equal(2, slideIds.Length);
        Assert.Equal(12192000, presentation.Presentation.SlideSize?.Cx?.Value);

        string SlideText(int index) => ((SlidePart)presentation.GetPartById(slideIds[index].RelationshipId!.Value!)).Slide.InnerText;
        var first = SlideText(0);
        Assert.Contains("CoE (AR/VR) projects by category", first, StringComparison.Ordinal);
        Assert.Contains("CoE Mid Term", first, StringComparison.Ordinal);
        Assert.Contains("CoE (no sub-category)", first, StringComparison.Ordinal);
        Assert.Contains("Devp", first, StringComparison.Ordinal);
        Assert.Contains("PDC 31 Mar 2027", first, StringComparison.Ordinal);
        // The cancelled project is not in a column; the footer names it.
        Assert.Contains("Cancelled: VR Dropped", first, StringComparison.Ordinal);

        var second = SlideText(1);
        Assert.Contains("AR/VR at a glance", second, StringComparison.Ordinal);
        Assert.Contains("AR/VR projects completed", second, StringComparison.Ordinal);
        Assert.Contains("in 1 AR/VR course", second, StringComparison.Ordinal);
        Assert.Contains("to 4 receiving units", second, StringComparison.Ordinal);
        Assert.Contains("Completed AR/VR projects, with the year of completion", second, StringComparison.Ordinal);
        Assert.Contains("VR Sniper", second, StringComparison.Ordinal);
        Assert.Contains("2022", second, StringComparison.Ordinal);
        Assert.DoesNotContain("Trained, by rank", second, StringComparison.Ordinal); // the breakdowns are gone
    }

    [Fact]
    public void Compose_WithoutTrackerAccess_TheSummaryShowsOnlyCompletedProjects()
    {
        var composer = new CoeSlideComposer(new TestEnvironment(Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", "PresentationRoot")));

        var content = composer.Compose(CoeSlidesDataBuilder.Build(AsOf, SampleCoe(), impact: null));

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var text = string.Join("\n", document.PresentationPart!.SlideParts.Select(part => part.Slide.InnerText));
        Assert.Contains("AR/VR projects completed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("People trained", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Units proliferated", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_BothSlidesCarryTheTwoInsignia_AsInTheBriefingDecks()
    {
        var composer = new CoeSlideComposer(new TestEnvironment(
            Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", "PresentationRoot"),
            Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot", "wwwroot")));

        var content = composer.Compose(CoeSlidesDataBuilder.Build(AsOf, SampleCoe(), impact: null));

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        Assert.All(document.PresentationPart!.SlideParts, slide =>
        {
            var pictures = slide.Slide.Descendants<Picture>().Select(picture => picture.NonVisualPictureProperties!.NonVisualDrawingProperties!.Name!.Value).ToArray();
            Assert.Equal(new[] { "Formation insignia", "Division insignia" }, pictures);
        });
    }

    [Fact]
    public void RowHeight_CountsEveryWrappedLineOfAnOverduePdc()
    {
        var oneLine = new CoeSlideProject("VR Gunnery", "Devp", false, "PDC 22 Jun 2026");
        var overdue = oneLine with { Pdc = "PDC 22 Jun 2026 · overdue", PdcOverdue = true };

        // Wide enough for both on one line: the same height.
        Assert.Equal(CoeSlideComposer.RowHeight(oneLine, 4, 12), CoeSlideComposer.RowHeight(overdue, 4, 12), 6);
        // Narrow: the overdue PDC wraps, and the row grows by its extra line.
        Assert.True(CoeSlideComposer.RowHeight(overdue, 1.4, 12) > CoeSlideComposer.RowHeight(oneLine, 1.4, 12));
    }

    [Fact]
    public void Compose_ListsTooLongForOneSlide_CarryOnToContinuationSlides_WithEveryName()
    {
        var ongoing = Enumerable.Range(1, 60)
            .Select(index => new CoeProjectSummaryVm(index, $"Ongoing project {index:000}", "Ongoing", "Feasibility Study", StageCodes.FS))
            .ToArray();
        var completed = Enumerable.Range(1, 130)
            .Select(index => new ArVrCompletedProjectVm(1000 + index, $"Completed project {index:000}", 2020 + (index % 6)))
            .ToArray();
        var coe = new CoeAnalyticsVm
        {
            TotalCoeProjects = 60,
            OngoingCount = 60,
            SubcategoryProjects = new[] { new CoeSubcategoryProjectsVm("CoE Mid Term", ongoing, OngoingCount: 60) },
            ArVrCompleted = new[] { new ArVrCompletedGroupVm("CoE (AR/VR)", completed) }
        };
        var composer = new CoeSlideComposer(new TestEnvironment(Path.Combine(AppContext.BaseDirectory, "TestData", "Ffc", "PresentationRoot")));

        var content = composer.Compose(CoeSlidesDataBuilder.Build(AsOf, coe, impact: null));

        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var presentation = document.PresentationPart!;
        var slides = presentation.Presentation.SlideIdList!.Elements<SlideId>()
            .Select(id => ((SlidePart)presentation.GetPartById(id.RelationshipId!.Value!)).Slide)
            .ToList();
        Assert.True(slides.Count > 2);

        var categorySlides = slides.Where(slide => slide.InnerText.Contains("CoE (AR/VR) projects by category", StringComparison.Ordinal)).ToList();
        var summarySlides = slides.Where(slide => slide.InnerText.Contains("AR/VR at a glance", StringComparison.Ordinal)).ToList();
        Assert.True(categorySlides.Count > 1);
        Assert.True(summarySlides.Count > 1);
        Assert.Contains("(continued)", categorySlides[1].InnerText, StringComparison.Ordinal);
        Assert.DoesNotContain("AR/VR projects completed", summarySlides[1].InnerText, StringComparison.Ordinal); // the cards stay on the first

        var text = string.Join("\n", slides.Select(slide => slide.InnerText));
        Assert.All(ongoing, project => Assert.Contains(project.Name, text, StringComparison.Ordinal));
        Assert.All(completed, project => Assert.Contains(project.Name, text, StringComparison.Ordinal));

        // Nothing is drawn below the slide's bottom edge (7.5 in).
        const long slideHeight = 6858000;
        Assert.All(slides.SelectMany(slide => slide.Descendants<DocumentFormat.OpenXml.Drawing.Offset>()), offset =>
            Assert.True(offset.Y!.Value < slideHeight, $"An item starts at {offset.Y.Value} EMU, below the slide."));
    }

    private static CoeAnalyticsVm SampleCoe() => new()
    {
        TotalCoeProjects = 6,
        OngoingCount = 3,
        CompletedCount = 2,
        SubcategoryProjects = new[]
        {
            new CoeSubcategoryProjectsVm("CoE (no sub-category)", new[]
            {
                new CoeProjectSummaryVm(6, "AR Range", "Ongoing", "Feasibility Study", StageCodes.FS)
            }, OngoingCount: 1, WithoutSubcategory: true),
            new CoeSubcategoryProjectsVm("CoE Short Term", new[]
            {
                new CoeProjectSummaryVm(5, "VR Navigation", "Completed", "Completed")
            }, CompletedCount: 1),
            new CoeSubcategoryProjectsVm("CoE Mid Term", new[]
            {
                new CoeProjectSummaryVm(1, "VR Driving", "Completed", "Completed"),
                new CoeProjectSummaryVm(2, "AR Maint", "Ongoing", "Feasibility Study", StageCodes.FS),
                new CoeProjectSummaryVm(3, "VR Gunnery", "Ongoing", "Development", StageCodes.DEVP, new DateOnly(2027, 3, 31)),
                new CoeProjectSummaryVm(4, "VR Dropped", "Cancelled", "Cancelled")
            }, OngoingCount: 2, CompletedCount: 1, CancelledCount: 1)
        },
        ArVrCompleted = new[]
        {
            new ArVrCompletedGroupVm("CoE (AR/VR)", new[] { new ArVrCompletedProjectVm(1, "VR Driving", 2025), new ArVrCompletedProjectVm(5, "VR Navigation", 2026) }),
            new ArVrCompletedGroupVm("Before the CoE nomination", new[]
            {
                new ArVrCompletedProjectVm(10, "VR Sniper", 2022),
                new ArVrCompletedProjectVm(11, "AR Medical", 2022),
                new ArVrCompletedProjectVm(12, "VR Old", null)
            }),
            new ArVrCompletedGroupVm("DCD · Fresh projects", Array.Empty<ArVrCompletedProjectVm>()),
            new ArVrCompletedGroupVm("DCD · Repeat builds", Array.Empty<ArVrCompletedProjectVm>())
        }
    };

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string contentRootPath, string? webRootPath = null)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = webRootPath ?? Path.Combine(contentRootPath, "wwwroot");
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; } = "ProjectManagement.Tests";
        public IFileProvider ContentRootFileProvider { get; set; }
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
    }
}
