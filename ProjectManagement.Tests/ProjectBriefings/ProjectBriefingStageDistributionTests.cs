using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;

namespace ProjectManagement.Tests.ProjectBriefings;

// The stage-wise distribution block (PR 7 of the blocks plan): the bands from the shared ARPP rule, and the
// chart and project list slides drawn from them.
public sealed class ProjectBriefingStageDistributionTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 16);
    private static readonly ProjectStandingFactsBuilder NoFacts = new();

    // SECTION: Bands
    [Fact]
    public void Build_PlacesEachProjectInTheArppBand()
    {
        var projects = new[]
        {
            Project(1, "At feasibility", StageCodes.FS),
            Project(2, "In the AoN stage", StageCodes.AON),
            Project(3, "AoN dated, at TEC", StageCodes.TEC),
            Project(4, "AoN skipped, at bidding", StageCodes.BID),
            Project(5, "No AoN stage in its workflow, at BM", StageCodes.BM),
            Project(6, "In the supply order stage", StageCodes.SO),
            Project(7, "Supply order dated, in the supply order stage", StageCodes.SO),
            Project(8, "At development", StageCodes.DEVP),
            Project(9, "At acceptance testing", StageCodes.ATP),
            Project(10, "Done", ProjectBriefingStageOrder.CompletedCode, ProjectLifecycleStatus.Completed),
            Project(11, "Stopped", StageCodes.TEC, ProjectLifecycleStatus.Cancelled)
        };
        var facts = new Dictionary<int, ProjectBriefingStageDistributionResolver.ProjectStandingFacts>
        {
            [1] = NoFacts.WithAon(StageStatus.NotStarted),
            [2] = NoFacts.WithAon(StageStatus.InProgress),
            [3] = NoFacts.WithAon(StageStatus.Completed) with { AonDate = new DateOnly(2025, 4, 1) },
            [4] = NoFacts.WithAon(StageStatus.Skipped),
            [5] = NoFacts.Build(),
            [6] = NoFacts.WithAon(StageStatus.Completed),
            [7] = NoFacts.WithAon(StageStatus.Completed) with { SupplyOrderDate = new DateOnly(2026, 1, 10) },
            [8] = NoFacts.WithAon(StageStatus.Completed),
            [9] = NoFacts.WithAon(StageStatus.Completed),
            [10] = NoFacts.Build() with { CompletedYear = 2026, CompletedMonth = 3 },
            [11] = NoFacts.WithAon(StageStatus.Completed)
        };

        var data = ProjectBriefingStageDistributionResolver.Build(projects, facts, _ => 0, AsOf);

        ArppProgressBucket BandOf(int id) => data.Lines.Single(line => line.ProjectId == id).Bucket;
        Assert.Equal(ArppProgressBucket.PreAon, BandOf(1));
        Assert.Equal(ArppProgressBucket.PreAon, BandOf(2));
        Assert.Equal(ArppProgressBucket.AonToSupplyOrder, BandOf(3));
        Assert.Equal(ArppProgressBucket.AonToSupplyOrder, BandOf(4));
        Assert.Equal(ArppProgressBucket.AonToSupplyOrder, BandOf(5));
        // Being in the Supply Order stage is not yet an order; a supply-order date is.
        Assert.Equal(ArppProgressBucket.AonToSupplyOrder, BandOf(6));
        Assert.Equal(ArppProgressBucket.UnderDevelopment, BandOf(7));
        Assert.Equal(ArppProgressBucket.UnderDevelopment, BandOf(8));
        Assert.Equal(ArppProgressBucket.UnderDevelopment, BandOf(9));
        Assert.Equal(ArppProgressBucket.Completed, BandOf(10));
        Assert.Equal(ArppProgressBucket.Cancelled, BandOf(11));

        var completed = data.Lines.Single(line => line.ProjectId == 10);
        Assert.Equal("Mar 2026", completed.CompletedLabel);
        Assert.Null(completed.StageCode);
        Assert.Equal(Array.IndexOf(StageCodes.All, StageCodes.TEC), data.Lines.Single(line => line.ProjectId == 3).StageRank);
    }

    [Fact]
    public void Build_ShowsThePdcOnlyAtDevelopmentAndMarksItOverdueWhenPast()
    {
        var projects = new[]
        {
            Project(1, "Late", StageCodes.DEVP),
            Project(2, "On time", StageCodes.DEVP),
            Project(3, "Past development", StageCodes.ATP)
        };
        var facts = new Dictionary<int, ProjectBriefingStageDistributionResolver.ProjectStandingFacts>
        {
            [1] = NoFacts.WithAon(StageStatus.Completed) with { DevelopmentPdcDate = AsOf.AddDays(-1) },
            [2] = NoFacts.WithAon(StageStatus.Completed) with { DevelopmentPdcDate = AsOf },
            [3] = NoFacts.WithAon(StageStatus.Completed) with { DevelopmentPdcDate = AsOf.AddDays(-30) }
        };

        var lines = ProjectBriefingStageDistributionResolver.Build(projects, facts, _ => 0, AsOf).Lines;

        Assert.True(lines.Single(line => line.ProjectId == 1).PdcOverdue);
        Assert.False(lines.Single(line => line.ProjectId == 2).PdcOverdue);
        Assert.Equal(AsOf, lines.Single(line => line.ProjectId == 2).DevelopmentPdc);
        Assert.Null(lines.Single(line => line.ProjectId == 3).DevelopmentPdc);
        Assert.False(lines.Single(line => line.ProjectId == 3).PdcOverdue);
    }

    [Fact]
    public void Build_SplitsByParentCategoryInTheCategoriesOwnOrder()
    {
        var projects = new[]
        {
            Project(1, "One", StageCodes.FS),
            Project(2, "Two", StageCodes.FS),
            Project(3, "Three", StageCodes.FS),
            Project(4, "Four", StageCodes.FS)
        };
        var facts = new Dictionary<int, ProjectBriefingStageDistributionResolver.ProjectStandingFacts>
        {
            [1] = NoFacts.Build() with { ParentCategory = "Other R&D" },
            [2] = NoFacts.Build() with { ParentCategory = "DCD" },
            [3] = NoFacts.Build() with { ParentCategory = "  " },
            [4] = NoFacts.Build() with { ParentCategory = "CoE" }
        };
        var rank = new Dictionary<string, int> { ["DCD"] = 1, ["CoE"] = 2, ["Other R&D"] = 3 };

        var data = ProjectBriefingStageDistributionResolver.Build(projects, facts, name => rank.GetValueOrDefault(name, int.MaxValue), AsOf);

        Assert.Equal(new[] { "DCD", "CoE", "Other R&D", ProjectBriefingStageDistributionResolver.NotCategorised }, data.CategoryOrder);
        Assert.Equal(ProjectBriefingStageDistributionResolver.NotCategorised, data.Lines.Single(line => line.ProjectId == 3).Category);
    }
    [Fact]
    public async Task ResolveAsync_ReadsTheParentCategoryAoNPdcAndCompletionFromTheRecords()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"briefing-stage-distribution-{Guid.NewGuid()}")
            .Options);
        db.ProjectCategories.AddRange(
            new ProjectCategory { Id = 1, Name = "DCD Projects", SortOrder = 1 },
            new ProjectCategory { Id = 2, Name = "CoE", SortOrder = 2 },
            new ProjectCategory { Id = 3, Name = "AR/VR", ParentId = 2, SortOrder = 1 });
        db.Projects.AddRange(
            new Project { Id = 1, Name = "Goggles", CategoryId = 3 },
            new Project { Id = 2, Name = "Drive", CategoryId = 1 },
            new Project { Id = 3, Name = "Range", CategoryId = 1, LifecycleStatus = ProjectLifecycleStatus.Completed, CompletedYear = 2025, CompletedMonth = 8 },
            new Project { Id = 4, Name = "Loose" });
        db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.AON, SortOrder = 4, Status = StageStatus.Skipped },
            new ProjectStage { ProjectId = 2, StageCode = StageCodes.AON, SortOrder = 4, Status = StageStatus.Completed, CompletedOn = new DateOnly(2025, 1, 5) },
            new ProjectStage { ProjectId = 2, StageCode = StageCodes.DEVP, SortOrder = 11, Status = StageStatus.InProgress, PlannedDue = new DateOnly(2026, 6, 30) },
            new ProjectStage { ProjectId = 4, StageCode = StageCodes.AON, SortOrder = 4, Status = StageStatus.NotStarted });
        await db.SaveChangesAsync();
        var projects = new[]
        {
            Project(1, "Goggles", StageCodes.TEC),
            Project(2, "Drive", StageCodes.DEVP),
            Project(3, "Range", ProjectBriefingStageOrder.CompletedCode, ProjectLifecycleStatus.Completed),
            Project(4, "Loose", StageCodes.SOW)
        };

        var data = await new ProjectBriefingStageDistributionResolver(db).ResolveAsync(projects, AsOf);

        var goggles = data.Lines.Single(line => line.ProjectId == 1);
        Assert.Equal("CoE", goggles.Category);
        Assert.Equal(ArppProgressBucket.AonToSupplyOrder, goggles.Bucket);
        var drive = data.Lines.Single(line => line.ProjectId == 2);
        Assert.Equal(ArppProgressBucket.UnderDevelopment, drive.Bucket);
        Assert.True(drive.DevelopmentPdc.HasValue);
        Assert.True(drive.PdcOverdue);
        var range = data.Lines.Single(line => line.ProjectId == 3);
        Assert.Equal(ArppProgressBucket.Completed, range.Bucket);
        Assert.Equal("Aug 2025", range.CompletedLabel);
        var loose = data.Lines.Single(line => line.ProjectId == 4);
        Assert.Equal(ArppProgressBucket.PreAon, loose.Bucket);
        Assert.Equal(ProjectBriefingStageDistributionResolver.NotCategorised, loose.Category);
        Assert.Equal(new[] { "DCD Projects", "CoE", ProjectBriefingStageDistributionResolver.NotCategorised }, data.CategoryOrder);
    }
    // END SECTION

    // SECTION: Slides
    [Theory]
    [InlineData(ProjectBriefingPresentationTheme.EditorialLight)]
    [InlineData(ProjectBriefingPresentationTheme.GraphiteDark)]
    public void Compose_DrawsTheChartThenTheListAndPassesTheIntegrityCheck(ProjectBriefingPresentationTheme theme)
    {
        var data = SampleData(theme);

        var composition = Composer().ComposeStageDistributionSlidesForTesting(data);

        ProjectBriefingPresentationIntegrityValidator.Validate(composition.Content, composition.SlideCount);
        Assert.Equal(ProjectBriefingSlideRole.StageDistribution, composition.Outline[0].Role);
        Assert.All(composition.Outline.Skip(1), item => Assert.Equal(ProjectBriefingSlideRole.StageDistributionList, item.Role));
        var slides = SlideTexts(composition.Content);
        Assert.Contains("Where the projects stand", slides[0], StringComparison.Ordinal);
        foreach (var band in new[] { "Pre-AoN", "AoN to SO", "Under development", "Completed" })
        {
            Assert.Contains(band, slides[0], StringComparison.Ordinal);
            Assert.Contains(band, slides[1], StringComparison.Ordinal);
        }
        // Cancelled projects are counted nowhere; the subtitle says so.
        Assert.Contains("1 cancelled, not shown", slides[0], StringComparison.Ordinal);
        Assert.DoesNotContain(string.Concat(slides.Skip(1)), "Withdrawn trainer", StringComparison.Ordinal);
        Assert.Contains("The projects by where they stand", slides[1], StringComparison.Ordinal);

        // Every shown project is listed exactly once across the list slides, and the outline names it.
        var listed = composition.Outline.Skip(1).SelectMany(item => item.ListedProjectIds).ToList();
        var shown = data.StageDistribution.Lines.Where(line => line.Bucket != ArppProgressBucket.Cancelled).Select(line => line.ProjectId).ToList();
        Assert.Equal(shown.OrderBy(id => id), listed.OrderBy(id => id));

        var xml = SlideXml(composition.Content);
        var canvas = ProjectBriefingThemeCatalog.Resolve(theme).Canvas;
        Assert.All(xml, slide => Assert.Contains($"val=\"{canvas}\"", slide, StringComparison.Ordinal));
        var dcd = theme == ProjectBriefingPresentationTheme.GraphiteDark ? "6EA8FE" : "2563EB";
        Assert.Contains($"val=\"{dcd}\"", xml[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_MarksAnOverduePdcInTheCriticalColour()
    {
        var data = SampleData(ProjectBriefingPresentationTheme.EditorialLight);
        var composition = Composer().ComposeStageDistributionSlidesForTesting(data);

        var critical = ProjectBriefingThemeCatalog.Resolve(ProjectBriefingPresentationTheme.EditorialLight).Critical;
        using var stream = new MemoryStream(composition.Content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var runs = document.PresentationPart!.SlideParts
            .SelectMany(slide => slide.Slide.Descendants<A.Run>())
            .Where(run => run.Text?.Text.StartsWith("  PDC", StringComparison.Ordinal) == true)
            .ToList();
        Assert.NotEmpty(runs);
        Assert.All(runs, run =>
        {
            var colour = run.RunProperties!.GetFirstChild<A.SolidFill>()!.RgbColorModelHex!.Val!.Value;
            Assert.Equal(run.Text!.Text.EndsWith("overdue", StringComparison.Ordinal), colour == critical);
        });
        Assert.Contains(runs, run => run.Text!.Text.EndsWith("overdue", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_ContinuesALongColumnOnFurtherSlides()
    {
        var lines = Enumerable.Range(1, 60)
            .Select(id => Line(id, $"Pre-AoN project {id} with a long descriptive name that wraps over two lines", ArppProgressBucket.PreAon, StageCodes.FS))
            .Append(Line(100, "One completed", ArppProgressBucket.Completed, null, completed: "Jan 2026"))
            .ToList();
        var data = Data(ProjectBriefingPresentationTheme.EditorialLight, lines);

        var composition = Composer().ComposeStageDistributionSlidesForTesting(data);

        Assert.True(composition.SlideCount >= 3, "A sixty-project column should need at least two list slides.");
        var slides = SlideTexts(composition.Content);
        Assert.Contains("(continued)", slides[2], StringComparison.Ordinal);
        Assert.Contains("Pre-AoN (cont.)", slides[2], StringComparison.Ordinal);
        Assert.Contains("page 1 of", slides[1], StringComparison.Ordinal);
        Assert.Equal(
            lines.Select(line => line.ProjectId).OrderBy(id => id),
            composition.Outline.Skip(1).SelectMany(item => item.ListedProjectIds).OrderBy(id => id));
        // Totals stay the band's whole count on every page.
        Assert.All(slides.Skip(1), slide => Assert.Contains("60", slide, StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_KeepsAFortyProjectListOnOneSlideInSideBySideParts()
    {
        // docs/ux/briefing-slides-review.md, PR 3: a long band takes two or more parts side by side
        // rather than a second slide.
        var lines = Enumerable.Range(1, 40)
            .Select(id => Line(id, $"Pre-AoN project {id}", ArppProgressBucket.PreAon, StageCodes.FS))
            .Concat(Enumerable.Range(41, 6).Select(id => Line(id, $"Completed project {id}", ArppProgressBucket.Completed, null, completed: "Jan 2026")))
            .ToList();

        var composition = Composer().ComposeStageDistributionSlidesForTesting(Data(ProjectBriefingPresentationTheme.EditorialLight, lines));

        ProjectBriefingPresentationIntegrityValidator.Validate(composition.Content, composition.SlideCount);
        Assert.Equal(2, composition.SlideCount); // the chart and one list slide
        var slides = SlideTexts(composition.Content);
        Assert.DoesNotContain("(cont.)", slides[1], StringComparison.Ordinal);
        Assert.Equal(
            lines.Select(line => line.ProjectId).OrderBy(id => id),
            composition.Outline.Skip(1).SelectMany(item => item.ListedProjectIds).OrderBy(id => id));
    }

    [Fact]
    public void Compose_LeavesEmptyBandsOutOfTheChartAndSaysNoneInTheList()
    {
        var lines = new[]
        {
            Line(1, "Finished one", ArppProgressBucket.Completed, null, completed: "Mar 2026"),
            Line(2, "Finished two", ArppProgressBucket.Completed, null, completed: "2025")
        };
        var composition = Composer().ComposeStageDistributionSlidesForTesting(Data(ProjectBriefingPresentationTheme.EditorialLight, lines));

        Assert.Equal(2, composition.SlideCount);
        var slides = SlideTexts(composition.Content);
        Assert.DoesNotContain("Pre-AoN", slides[0], StringComparison.Ordinal);
        Assert.Contains("Completed", slides[0], StringComparison.Ordinal);
        Assert.Equal(3, CountOf(slides[1], "None"));
    }

    [Fact]
    public void Compose_WithNoOngoingOrCompletedProjectSaysSo()
    {
        var lines = new[] { Line(1, "Withdrawn", ArppProgressBucket.Cancelled, StageCodes.TEC) };
        var composition = Composer().ComposeStageDistributionSlidesForTesting(Data(ProjectBriefingPresentationTheme.GraphiteDark, lines));

        ProjectBriefingPresentationIntegrityValidator.Validate(composition.Content, composition.SlideCount);
        var slides = SlideTexts(composition.Content);
        Assert.Contains("No project in this deck is ongoing or completed.", slides[0], StringComparison.Ordinal);
        Assert.Equal(4, CountOf(slides[1], "None"));
    }

    [Fact]
    public void RenderSample_WritesThePreviewWhenAskedFor()
    {
        // Set PRISM_BRIEFING_SAMPLE_DIR to look at the slides: the preview HTML of both themes is written there.
        var directory = Environment.GetEnvironmentVariable("PRISM_BRIEFING_SAMPLE_DIR");
        foreach (var theme in new[] { ProjectBriefingPresentationTheme.EditorialLight, ProjectBriefingPresentationTheme.GraphiteDark })
        {
            var data = SampleData(theme);
            var composition = Composer().ComposeStageDistributionSlidesForTesting(data);
            var preview = ProjectBriefingSlidePreviewRenderer.Render(composition.Content, data.GeneratedAtUtc);
            Assert.Equal(composition.SlideCount, preview.SlideCount);
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            Directory.CreateDirectory(directory);
            var html = string.Concat(preview.Slides.Select(slide => $"<section style=\"margin:16px\">{slide.Html}</section>"));
            foreach (var (key, uri) in preview.Images)
            {
                html = html.Replace($"data-pbd-preview-image=\"{key}\"", $"src=\"{uri}\"", StringComparison.Ordinal);
            }
            File.WriteAllText(Path.Combine(directory, $"stage-distribution-{theme}.html"), $"<!doctype html><meta charset=utf-8><body style=\"background:#888\">{html}</body>");
            File.WriteAllBytes(Path.Combine(directory, $"stage-distribution-{theme}.pptx"), composition.Content);
        }
    }
    // END SECTION

    // SECTION: Helpers
    private static ProjectBriefingPresentationData SampleData(ProjectBriefingPresentationTheme theme)
    {
        // Around 49 projects, as a command deck has, spread like the mockup.
        var names = new[]
        {
            "Gunnery Trainer Mk III", "Bridge Laying Simulator", "Tank Crew Procedural Trainer", "Mortar Fire Controller Simulator",
            "EW Operator Trainer", "Field Medic VR", "Signals Net Trainer", "Convoy Driving Simulator",
            "UAV Crew Trainer", "Air Defence Tactical Trainer", "Boat Handling Simulator", "Radar Maintenance Trainer",
            "Parachute Descent Simulator", "Combat Engineer VR", "Counter-IED Procedural Trainer", "Infantry Section Battle Drill Simulator",
            "Armoured Recovery Vehicle Trainer", "Night Vision Driving Trainer", "Avalanche Rescue VR", "Loitering Munition Operator Trainer",
            "Driving Simulator for ICV", "ATGM Trainer", "Shoot House VR", "Artillery Observer Simulator", "Helicopter Door Gunner Trainer",
            "Integrated Air Defence Fire Control Radar Maintenance Simulator", "Bridge Crossing Procedural Trainer", "Sniper Spotter Team Trainer",
            "Engine Fault Diagnosis Trainer", "Tactical Floor Games Suite", "Swarm Drone Mission Planner", "Map Reading VR",
            "Small Arms Trainer", "Tank Gunnery Simulator", "Ship Bridge Simulator", "Sniper Trainer", "Mine Detection VR",
            "Field Artillery Crew Drill Trainer", "Communication Equipment Maintenance Trainer", "Pontoon Bridge Simulator",
            "Vehicle Recovery Procedural Trainer", "High Altitude Acclimatisation VR", "Anti-Tank Mine Laying Trainer",
            "Battle Damage Repair Trainer", "Electronic Warfare Analyst Trainer", "Snow Mobile Driving Simulator",
            "Military Police Convoy Escort Trainer", "Rifle Grenade Trainer", "Withdrawn trainer"
        };
        var categories = new[] { "DCD", "DCD", "CoE", "DCD", "Other R&D" };
        var lines = new List<ArppProjectLineVm>();
        for (var index = 0; index < names.Length; index++)
        {
            var id = index + 1;
            var category = categories[index % categories.Length];
            ArppProjectLineVm line = index switch
            {
                < 8 => Line(id, names[index], ArppProgressBucket.PreAon, new[] { StageCodes.FS, StageCodes.IPA, StageCodes.SOW, StageCodes.AON }[index % 4], category),
                < 20 => Line(id, names[index], ArppProgressBucket.AonToSupplyOrder, new[] { StageCodes.BID, StageCodes.TEC, StageCodes.BM, StageCodes.COB, StageCodes.PNC, StageCodes.SO }[index % 6], category),
                < 32 => index % 4 == 3
                    ? Line(id, names[index], ArppProgressBucket.UnderDevelopment, StageCodes.ATP, category)
                    : Line(id, names[index], ArppProgressBucket.UnderDevelopment, StageCodes.DEVP, category,
                        pdc: AsOf.AddDays((index - 25) * 37)),
                < 48 => Line(id, names[index], ArppProgressBucket.Completed, null, category,
                    completed: new DateOnly(2026, 1 + index % 10, 1).ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
                    completedSort: new DateOnly(2026, 1 + index % 10, 1)),
                _ => Line(id, names[index], ArppProgressBucket.Cancelled, StageCodes.TEC, category)
            };
            lines.Add(line);
        }

        return Data(theme, lines);
    }

    private static ProjectBriefingPresentationData Data(ProjectBriefingPresentationTheme theme, IReadOnlyList<ArppProjectLineVm> lines)
        => new()
        {
            DeckId = 900,
            DeckName = "Stage-wise distribution",
            PresentationTheme = theme,
            HandlingMarking = "RESTRICTED",
            GeneratedAtUtc = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero),
            StageDistribution = new ProjectBriefingStageDistributionData
            {
                AsOf = AsOf,
                CategoryOrder = new[] { "DCD", "CoE", "Other R&D" },
                Lines = lines
            }
        };

    private static ArppProjectLineVm Line(
        int id,
        string name,
        ArppProgressBucket band,
        string? stage,
        string category = "DCD",
        DateOnly? pdc = null,
        string? completed = null,
        DateOnly? completedSort = null)
        => new(
            id,
            name,
            category,
            band,
            stage,
            stage,
            stage is null ? -1 : Array.IndexOf(StageCodes.All, stage),
            pdc,
            pdc is { } due && due < AsOf,
            completed,
            completedSort);

    private static ProjectBriefingPresentationProject Project(
        int id,
        string name,
        string stageCode,
        ProjectLifecycleStatus lifecycle = ProjectLifecycleStatus.Active)
        => new()
        {
            ProjectId = id,
            ProjectName = name,
            LifecycleStatus = lifecycle,
            PresentStageCode = stageCode,
            PresentStage = stageCode
        };

    private sealed class ProjectStandingFactsBuilder
    {
        public ProjectBriefingStageDistributionResolver.ProjectStandingFacts Build() =>
            new(null, null, Array.Empty<StageStatus>(), null, null, null, null, null);

        public ProjectBriefingStageDistributionResolver.ProjectStandingFacts WithAon(StageStatus status) =>
            Build() with { AonStageStatuses = new[] { status } };
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

    private static List<string> SlideTexts(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var part = document.PresentationPart!;
        return part.Presentation.SlideIdList!.Elements<DocumentFormat.OpenXml.Presentation.SlideId>()
            .Select(slideId => (SlidePart)part.GetPartById(slideId.RelationshipId!.Value!))
            .Select(slide => string.Join("\n", slide.Slide.Descendants<A.Text>().Select(node => node.Text)))
            .ToList();
    }

    private static List<string> SlideXml(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var part = document.PresentationPart!;
        return part.Presentation.SlideIdList!.Elements<DocumentFormat.OpenXml.Presentation.SlideId>()
            .Select(slideId => (SlidePart)part.GetPartById(slideId.RelationshipId!.Value!))
            // The slide with its layout, which carries the background and the shared furniture.
            .Select(slide => slide.Slide.OuterXml + (slide.SlideLayoutPart?.SlideLayout.OuterXml ?? string.Empty))
            .ToList();
    }

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
    // END SECTION
}
