using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// PR 3 of the blocks plan (docs/ux/briefing-decks.md): the composer builds a deck from a list of
// blocks instead of a fixed sequence, and an existing deck must come out exactly as before. The
// fixture was recorded from the composer before the change (set PRISM_UPDATE_BLOCK_FIXTURE=1 to
// record it again): for each of a wide sample of setting combinations, every slide's role, project,
// listed projects and a digest of its XML. The test composes the same combinations now and compares.
public sealed class ProjectBriefingBlockEquivalenceTests
{
    private const string FixtureName = "block-equivalence.json";

    [Fact]
    public void EveryRecordedSettingCombination_ComposesTheSameSlides()
    {
        var cases = Cases().ToList();
        var actual = cases.ToDictionary(item => item.Key, item => Describe(item.Data));
        var path = FixturePath();

        if (Environment.GetEnvironmentVariable("PRISM_UPDATE_BLOCK_FIXTURE") == "1")
        {
            File.WriteAllText(path, JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        var expected = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path))!;
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (key, slides) in expected)
        {
            Assert.True(actual.ContainsKey(key), $"Missing case {key}");
            Assert.True(slides.SequenceEqual(actual[key]), $"Case {key} changed.\nExpected:\n{string.Join("\n", slides)}\nActual:\n{string.Join("\n", actual[key])}");
        }
    }

    private static List<string> Describe(ProjectBriefingPresentationData data)
    {
        var composition = Composer().ComposeWithOutline(data);
        using var stream = new MemoryStream(composition.Content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var presentation = document.PresentationPart!;
        var slideIds = presentation.Presentation.SlideIdList!.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().ToArray();
        var lines = new List<string>();
        for (var index = 0; index < slideIds.Length; index++)
        {
            var part = (SlidePart)presentation.GetPartById(slideIds[index].RelationshipId!);
            // Picture relationship ids are allotted per run; they name parts, not what the slide shows.
            var xml = System.Text.RegularExpressions.Regex.Replace(part.Slide.OuterXml, "r:(embed|id|link)=\"[^\"]*\"", "r:$1=\"\"");
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)))[..16];
            var outline = composition.Outline[index];
            lines.Add($"{outline.Role}|{outline.ProjectId}|{string.Join(",", outline.ListedProjectIds)}|{digest}");
        }

        return lines;
    }

    // A deterministic sample of the settings that decide which slides a deck has and in what order.
    private static IEnumerable<(string Key, ProjectBriefingPresentationData Data)> Cases()
    {
        var additionalOrders = new[]
        {
            Array.Empty<ProjectBriefingAdditionalSlideType>(),
            new[] { ProjectBriefingAdditionalSlideType.InstitutionalProfile, ProjectBriefingAdditionalSlideType.RoleAndCharter, ProjectBriefingAdditionalSlideType.FfcGlobalFootprint },
            new[] { ProjectBriefingAdditionalSlideType.FfcGlobalFootprint, ProjectBriefingAdditionalSlideType.RoleAndCharter, ProjectBriefingAdditionalSlideType.InstitutionalProfile }
        };
        var keyPoints = new[] { ProjectBriefingKeyPointPlacement.None, ProjectBriefingKeyPointPlacement.ProjectBriefSlide, ProjectBriefingKeyPointPlacement.CapabilitySlide };

        // Every layout, mode, narrative and additional-slide order, with the summary switches
        // and the rest varied by a fixed pseudo-random walk so each switch is seen on and off.
        var random = new Random(3359);
        foreach (var layout in new[] { ProjectBriefingLayout.StandardBriefing, ProjectBriefingLayout.ProjectUpdateSheet })
        foreach (var mode in Enum.GetValues<ProjectBriefingPresentationMode>())
        foreach (var narrative in Enum.GetValues<ProjectBriefingNarrativeMode>())
        foreach (var orderIndex in Enumerable.Range(0, additionalOrders.Length))
        for (var variant = 0; variant < 3; variant++)
        {
            if (layout == ProjectBriefingLayout.ProjectUpdateSheet && (mode != ProjectBriefingPresentationMode.Combined || narrative != ProjectBriefingNarrativeMode.ProjectBrief))
            {
                // The update-sheet layout ignores mode and narrative; one combination of them is enough.
                continue;
            }

            var cover = random.Next(2) == 0;
            var portfolio = random.Next(2) == 0;
            var stage = random.Next(2) == 0;
            var stageTable = random.Next(2) == 0;
            var projectCategory = random.Next(2) == 0;
            var technicalCategory = random.Next(2) == 0;
            var tableRowOnly = random.Next(2) == 0;
            var keyPoint = keyPoints[random.Next(keyPoints.Length)];
            var key = $"{layout}/{mode}/{narrative}/order{orderIndex}/v{variant}"
                + $"/c{B(cover)}p{B(portfolio)}s{B(stage)}t{B(stageTable)}pc{B(projectCategory)}tc{B(technicalCategory)}r{B(tableRowOnly)}/{keyPoint}";
            yield return (key, Data(layout, mode, narrative, additionalOrders[orderIndex], cover, portfolio, stage, stageTable, projectCategory, technicalCategory, tableRowOnly, keyPoint));
        }
    }

    private static string B(bool value) => value ? "1" : "0";

    private static ProjectBriefingPresentationData Data(
        ProjectBriefingLayout layout,
        ProjectBriefingPresentationMode mode,
        ProjectBriefingNarrativeMode narrative,
        IReadOnlyList<ProjectBriefingAdditionalSlideType> additionalOrder,
        bool cover,
        bool portfolio,
        bool stage,
        bool stageTable,
        bool projectCategory,
        bool technicalCategory,
        bool tableRowOnly,
        ProjectBriefingKeyPointPlacement keyPoints)
    {
        var projects = new[]
        {
            Project(1, "Driving simulator", "DEVP", "Development", ProjectBriefingStageOrder.Development, 10, "Training", "VR", tableRowOnly: false, longText: true),
            Project(2, "Gunnery trainer", "TEC", "TEC", ProjectBriefingStageOrder.Development - 3, 20, "Training", "Full motion", tableRowOnly: false, longText: false),
            Project(3, "Bridge laying simulator", "SOC", "SoC", ProjectBriefingStageOrder.Development - 6, 30, "Engineering", "Desktop", tableRowOnly, longText: false),
            Project(4, "Radar maintenance trainer", "DEVP", "Development", ProjectBriefingStageOrder.Development, 40, "Maintenance", "VR", tableRowOnly: false, longText: false)
        };

        return new ProjectBriefingPresentationData
        {
            DeckId = 3359,
            DeckName = "Equivalence deck",
            DeckDescription = "Every combination",
            Layout = layout,
            PresentationMode = mode,
            CostMode = ProjectBriefingCostMode.Both,
            NarrativeMode = narrative,
            StandardSlideOptions = ProjectBriefingStandardSlideOptions.Normalize(
                ProjectBriefingProjectBriefLayout.Automatic,
                showPresentStage: true,
                showPresentStatus: true,
                includeStageDistributionTable: stageTable,
                keyPoints: keyPoints),
            PresentationTheme = ProjectBriefingPresentationTheme.EditorialLight,
            ClosingSlideType = ProjectBriefingClosingSlideType.JaiHind,
            AdditionalSlideOrder = additionalOrder,
            BrandingScope = ProjectBriefingBrandingScope.None,
            IncludeCoverSlide = cover,
            IncludePortfolioSummarySlide = portfolio,
            IncludeStageSummary = layout == ProjectBriefingLayout.StandardBriefing && stage,
            IncludeProjectCategorySummary = layout == ProjectBriefingLayout.StandardBriefing && projectCategory,
            IncludeTechnicalCategorySummary = layout == ProjectBriefingLayout.StandardBriefing && technicalCategory,
            UpdateSheetOptions = ProjectBriefingUpdateSheetOptions.Default,
            HandlingMarking = "RESTRICTED",
            GeneratedAtUtc = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero),
            Projects = projects,
            Summary = new ProjectBriefingPresentationSummary
            {
                ProjectCount = 4,
                OngoingCount = 4,
                StageSummary = new[] { new ProjectBriefingSummaryPoint("Development", 2, 1), new ProjectBriefingSummaryPoint("TEC", 1, 2), new ProjectBriefingSummaryPoint("SoC", 1, 3) },
                OngoingStageSummary = new[] { new ProjectBriefingSummaryPoint("Development", 2, 1), new ProjectBriefingSummaryPoint("TEC", 1, 2), new ProjectBriefingSummaryPoint("SoC", 1, 3) },
                ProjectCategorySummary = new[] { new ProjectBriefingSummaryPoint("Training", 2), new ProjectBriefingSummaryPoint("Engineering", 1), new ProjectBriefingSummaryPoint("Maintenance", 1) },
                TechnicalCategorySummary = new[] { new ProjectBriefingSummaryPoint("VR", 2), new ProjectBriefingSummaryPoint("Full motion", 1), new ProjectBriefingSummaryPoint("Desktop", 1) }
            },
            InstitutionalProfile = additionalOrder.Contains(ProjectBriefingAdditionalSlideType.InstitutionalProfile)
                ? new ProjectBriefingInstitutionalProfileData
                {
                    Title = "SDD – Growth over the years",
                    DataAsOnUtc = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero),
                    Modules = new[]
                    {
                        new ProjectBriefingInstitutionalModuleData(
                            ProjectBriefingInstitutionalProfileModule.ProjectsDeveloped,
                            "Simulators/Projects Developed",
                            "160",
                            new[] { new ProjectBriefingInstitutionalMetricRow("AR/VR", "19") })
                    }
                }
                : null,
            RoleCharter = additionalOrder.Contains(ProjectBriefingAdditionalSlideType.RoleAndCharter)
                ? new ProjectBriefingRoleCharterData
                {
                    Layout = ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter,
                    RoleStatements = ProjectBriefingRoleCharterOptions.SharedRoleStatements,
                    CharterItems = ProjectBriefingRoleCharterOptions.SharedCharterItems
                }
                : null,
            FfcGlobalFootprint = additionalOrder.Contains(ProjectBriefingAdditionalSlideType.FfcGlobalFootprint)
                ? new ProjectBriefingFfcGlobalFootprintData
                {
                    Layout = ProjectBriefingFfcGlobalFootprintLayout.MapWithCountryBreakdown,
                    IncludeCountryWiseBreakdown = true,
                    Summary = new FfcFootprintSummary(2, 2, 5, 3, 2, 7),
                    Countries = new[]
                    {
                        new ProjectBriefingFfcCountryData("Ethiopia", "ETH", 4, 0, 0, 7),
                        new ProjectBriefingFfcCountryData("Sri Lanka", "LKA", 1, 3, 2, 0)
                    },
                    MaximumCountryRows = 8,
                    MapImage = TestImageFactory.TinyPng(),
                    DataAsOnUtc = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero)
                }
                : null
        };
    }

    private static ProjectBriefingPresentationProject Project(
        int id,
        string name,
        string stageCode,
        string stage,
        int stageOrder,
        int sortOrder,
        string category,
        string technicalCategory,
        bool tableRowOnly,
        bool longText)
    {
        var capability = longText
            ? string.Join("\n", Enumerable.Range(1, 14).Select(index => $"Capability statement {index} describing what crews can practise safely on the simulator before live training."))
            : "Trains crews safely.\nCuts live-training cost.";
        var brief = longText
            ? string.Join("\n\n", Enumerable.Range(1, 6).Select(index => $"Paragraph {index}. The project gives units a realistic, repeatable way to practise drills that are costly or unsafe with live equipment, and records each crew's performance for review by instructors."))
            : "A short brief for the project.";
        return new ProjectBriefingPresentationProject
        {
            ProjectId = id,
            ProjectName = name,
            LifecycleStatus = ProjectLifecycleStatus.Active,
            LifecycleDisplay = "Ongoing",
            PresentStageCode = stageCode,
            PresentStage = stage,
            PresentStageOrder = stageOrder,
            ProjectCategory = category,
            TechnicalCategory = technicalCategory,
            CostRd = ProjectBriefingCostValue.Missing(),
            ExternalStatus = "Trials at the user site complete.",
            BriefDescription = capability,
            ProjectBrief = brief,
            KeyPoints = new[]
            {
                new ProjectBriefingKeyPoint("Training gap", "Live firing costs limit practice hours."),
                new ProjectBriefingKeyPoint("Op necessity", "Crews must drill night movement before induction.")
            },
            SortOrder = sortOrder,
            TableRowOnly = tableRowOnly,
            SpeakerNotes = id == 1 ? "Open with the training gap." : null
        };
    }

    private static string FixturePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.Tests.csproj")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "ProjectBriefings", "Fixtures", FixtureName);
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
}
