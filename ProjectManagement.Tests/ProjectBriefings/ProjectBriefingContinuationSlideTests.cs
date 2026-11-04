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

// Long Role & Charter and project briefs continue onto further slides instead of shrinking
// below a readable size: charter text stays at 10 pt or more, brief text at 12 pt or more.
public sealed class ProjectBriefingContinuationSlideTests
{
    private const string LongBriefSentence =
        "This project narrative records the operational requirement, the development approach, integration activities, validation methodology and intended employment of the system in service.";

    [Fact]
    public void ShortCharter_StaysOnOneSlide()
    {
        var pages = ProjectBriefingRoleCharterPaginator.Paginate(Charter(itemCount: 13, textLength: 45));

        var page = Assert.Single(pages);
        Assert.False(page.IsContinuation);
        Assert.Equal(13, page.CharterItems.Count);
        Assert.Equal(1, ProjectBriefingRoleCharterPaginator.EstimateSlideCount(ProjectBriefingRoleCharterOptions.Default with { IncludeSlide = true }));
    }

    [Theory]
    [InlineData(ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter)]
    [InlineData(ProjectBriefingRoleCharterLayout.RoleAndSingleColumnCharter)]
    [InlineData(ProjectBriefingRoleCharterLayout.CharterOnly)]
    public void LongCharter_ContinuesInOrder_AndEveryPageStaysAtTenPointOrMore(ProjectBriefingRoleCharterLayout layout)
    {
        var data = Charter(itemCount: 18, textLength: 230, layout);

        var pages = ProjectBriefingRoleCharterPaginator.Paginate(data);

        Assert.True(pages.Count > 1);
        Assert.Equal(data.CharterItems, pages.SelectMany(page => page.CharterItems));
        Assert.All(pages, page => Assert.NotEmpty(page.CharterItems));
        Assert.False(pages[0].IsContinuation);
        Assert.All(pages.Skip(1), page =>
        {
            Assert.True(page.IsContinuation);
            Assert.Empty(page.RoleStatements);
        });
        Assert.Equal(layout == ProjectBriefingRoleCharterLayout.CharterOnly ? 0 : 2, pages[0].RoleStatements.Count);
        for (var index = 0; index < pages.Count; index++)
        {
            var height = index == 0 && pages[0].RoleStatements.Count > 0
                ? ProjectBriefingRoleCharterPaginator.CharterHeightBelowRole
                : ProjectBriefingRoleCharterPaginator.CharterHeightFullSlide;
            var typography = ProjectBriefingRoleCharterPaginator.ResolveTypography(pages[index].CharterItems, layout, height);
            Assert.True(typography.FontSize >= 9.95, $"Page {index + 1} charter type is {typography.FontSize:0.0} pt.");
        }
    }

    [Fact]
    public void CharterSlideEstimate_MatchesTheSlidesComposed()
    {
        var data = Charter(itemCount: 18, textLength: 230);
        var options = ProjectBriefingRoleCharterOptions.Normalize(
            includeSlide: true,
            data.Title,
            data.Layout,
            useSharedContent: false,
            data.RoleStatements,
            data.CharterItems);

        var composition = Composer().ComposeWithOutline(Deck(roleCharter: data));

        Assert.Equal(
            ProjectBriefingRoleCharterPaginator.EstimateSlideCount(options),
            composition.Outline.Count(item => item.Role == ProjectBriefingSlideRole.RoleCharter));
        Assert.True(composition.Outline.Count(item => item.Role == ProjectBriefingSlideRole.RoleCharter) > 1);
    }

    [Fact]
    public void ShortBrief_KeepsItsSingleSlideAndTypography()
    {
        const string brief = "A concise brief that sits comfortably beside the project photograph.";

        var page = Assert.Single(ProjectBriefingSlideComposer.PaginateProjectBrief(brief, 8.25, 4.36));

        Assert.False(page.IsContinuation);
        Assert.Equal(ProjectBriefingNarrativeTypography.ResolveProjectBrief(brief).BodyFontSize, page.Typography.BodyFontSize);
        Assert.Equal(1, ProjectBriefingSlideComposer.EstimateProjectBriefSlideCount(brief));
    }

    [Fact]
    public void LongBrief_ContinuesAtTwelvePointOrMore_WithoutLosingText()
    {
        var brief = LongBrief(paragraphs: 5, sentencesPerParagraph: 3);
        Assert.InRange(brief.Length, 2_500, 3_000);

        var pages = ProjectBriefingSlideComposer.PaginateProjectBrief(brief, 8.25, 4.36);

        Assert.True(pages.Count > 1);
        Assert.False(pages[0].IsContinuation);
        Assert.All(pages.Skip(1), page => Assert.True(page.IsContinuation));
        Assert.All(pages, page => Assert.True(page.Typography.BodyFontSize >= 12.0));
        Assert.Equal(Words(brief), pages.SelectMany(page => Words(page.Text)));
        Assert.Equal(pages.Count, ProjectBriefingSlideComposer.EstimateProjectBriefSlideCount(brief));
    }

    [Fact]
    public void Compose_LongBrief_AddsContinuationSlidesForTheSameProject()
    {
        var brief = LongBrief(paragraphs: 5, sentencesPerParagraph: 3);

        var composition = Composer().ComposeWithOutline(Deck(projectBrief: brief));

        var briefSlides = composition.Outline
            .Where(item => item.Role is ProjectBriefingSlideRole.ProjectBrief or ProjectBriefingSlideRole.ProjectBriefContinuation)
            .ToArray();
        Assert.True(briefSlides.Length > 1);
        Assert.Equal(ProjectBriefingSlideRole.ProjectBrief, briefSlides[0].Role);
        Assert.All(briefSlides.Skip(1), item => Assert.Equal(ProjectBriefingSlideRole.ProjectBriefContinuation, item.Role));
        Assert.All(briefSlides, item => Assert.Equal(901, item.ProjectId));

        using var stream = new MemoryStream(composition.Content, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var text = string.Join("\n", document.PresentationPart!.SlideParts.Select(SlideText));
        Assert.Contains("PROJECT BRIEF — CONTINUED", text, StringComparison.Ordinal);
        Assert.Contains("employment of the system in service.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_ShortBrief_HasNoContinuationSlide()
    {
        var composition = Composer().ComposeWithOutline(Deck(projectBrief: "A short brief."));

        Assert.Single(composition.Outline, item => item.Role == ProjectBriefingSlideRole.ProjectBrief);
        Assert.DoesNotContain(composition.Outline, item => item.Role == ProjectBriefingSlideRole.ProjectBriefContinuation);
    }

    private static string LongBrief(int paragraphs, int sentencesPerParagraph)
        => string.Join(
            "\n\n",
            Enumerable.Range(1, paragraphs).Select(_ =>
                string.Join(" ", Enumerable.Repeat(LongBriefSentence, sentencesPerParagraph))));

    private static string[] Words(string value)
        => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static ProjectBriefingRoleCharterData Charter(
        int itemCount,
        int textLength,
        ProjectBriefingRoleCharterLayout layout = ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter)
        => new()
        {
            Title = "Role & Charter",
            Layout = layout,
            RoleStatements = layout == ProjectBriefingRoleCharterLayout.CharterOnly
                ? Array.Empty<ProjectBriefingRoleCharterEntry>()
                : new[]
                {
                    new ProjectBriefingRoleCharterEntry("Nodal Centre", "Development of specified simulators for the Army."),
                    new ProjectBriefingRoleCharterEntry("Centre of Excellence", "AR/VR solutions and training support.")
                },
            CharterItems = Enumerable.Range(1, itemCount)
                .Select(index => new ProjectBriefingRoleCharterEntry(
                    $"Charter {index}",
                    string.Concat(Enumerable.Repeat($"Responsibility {index} detail ", 10))[..textLength].Trim()))
                .ToArray()
        };

    private static ProjectBriefingPresentationData Deck(
        ProjectBriefingRoleCharterData? roleCharter = null,
        string projectBrief = "A short brief.")
        => new()
        {
            DeckId = 900,
            DeckName = "Continuation deck",
            PresentationMode = ProjectBriefingPresentationMode.DetailedProjects,
            NarrativeMode = ProjectBriefingNarrativeMode.ProjectBrief,
            CostMode = ProjectBriefingCostMode.Both,
            StandardSlideOptions = ProjectBriefingStandardSlideOptions.Default,
            IncludeCoverSlide = false,
            IncludePortfolioSummarySlide = false,
            GeneratedAtUtc = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero),
            RoleCharter = roleCharter,
            AdditionalSlideOrder = roleCharter is null
                ? Array.Empty<ProjectBriefingAdditionalSlideType>()
                : new[] { ProjectBriefingAdditionalSlideType.RoleAndCharter },
            Projects = new[]
            {
                new ProjectBriefingPresentationProject
                {
                    ProjectId = 901,
                    ProjectName = "LONG NARRATIVE PROJECT",
                    LifecycleStatus = ProjectLifecycleStatus.Active,
                    LifecycleDisplay = "Ongoing",
                    PresentStageCode = "DEVP",
                    PresentStage = "Development",
                    PresentStageOrder = ProjectBriefingStageOrder.Development,
                    CostRd = ProjectBriefingCostValue.Missing(),
                    ProliferationCost = ProjectBriefingCostValue.Missing(ProjectBriefingCostBasis.Proliferation),
                    ExternalStatus = "Trials in progress.",
                    BriefDescription = "Capability description.",
                    ProjectBrief = projectBrief,
                    SortOrder = 1
                }
            },
            Summary = new ProjectBriefingPresentationSummary { ProjectCount = 1, OngoingCount = 1 }
        };

    private static ProjectBriefingSlideComposer Composer()
        => new(new TestEnvironment(Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "PresentationRoot")));

    private static string SlideText(SlidePart slide)
        => string.Join("\n", slide.Slide.Descendants<A.Paragraph>()
            .Select(paragraph => string.Concat(paragraph.Descendants<A.Text>().Select(node => node.Text))));

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
