using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// Key points only follow-through (docs/ux/briefing-deck-review-fixes.md, PR 4): the slide estimate
// and the Studio's capability offer follow the deck's project slide design.
public sealed class ProjectBriefingSlideEstimateTests
{
    // Long enough to run the capability overview onto continuation slides.
    private static readonly string LongCapability = string.Join(' ', Enumerable.Repeat(
        "The system gives the user a clear picture of the field and trains crews to act on it quickly.", 60));

    [Fact]
    public void KeyPointsOnly_WithLongCapabilityText_EstimatesOneSlidePerProject()
    {
        var estimate = Estimate(KeyPointsOptions(keyPointsOnly: true), Projects(3));

        Assert.Equal(3, estimate.DetailedProjectSlides);
        Assert.Equal(0, estimate.CapabilityContinuationSlides);
        Assert.Equal(0, estimate.ProjectBriefSlides);
        // Three project slides and the closing slide.
        Assert.Equal(4, estimate.TotalSlides);
    }

    [Fact]
    public void KeyPoints_WithTheSameText_StillCountsItsContinuationSlides()
    {
        var estimate = Estimate(KeyPointsOptions(keyPointsOnly: false), Projects(3));

        Assert.Equal(3, estimate.DetailedProjectSlides);
        Assert.True(estimate.CapabilityContinuationSlides > 0, "the long text continues on the Key points design");
        Assert.Equal(4 + estimate.CapabilityContinuationSlides, estimate.TotalSlides);
    }

    [Theory]
    [InlineData(true, "KeyPointsOnly")]
    [InlineData(false, "KeyPoints")]
    public void TheDeckPayload_CarriesTheDesign_TheStudioReads(bool keyPointsOnly, string expected)
    {
        var deck = new ProjectBriefingDeckVm
        {
            Layout = ProjectBriefingLayout.StandardBriefing,
            NarrativeMode = ProjectBriefingNarrativeMode.CapabilityOverview,
            StandardSlideOptions = KeyPointsOptions(keyPointsOnly)
        };

        // The same options as the page's in-page JSON and the save replies: enums as names.
        var json = JsonSerializer.Serialize(deck, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        });

        Assert.Equal(expected, JsonDocument.Parse(json).RootElement.GetProperty("projectDesign").GetString());
    }

    [Fact]
    public void TheStudio_ReadsTheDesignFromTheDeck_NotAFlagFixedAtPageLoad()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", "Index.cshtml");
        Assert.True(File.Exists(path), $"The page was not copied to test output: {path}");
        var page = File.ReadAllText(path);

        Assert.Contains("projectDesign = deck.ProjectDesign", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-uses-capability", page, StringComparison.Ordinal);
    }

    private static ProjectBriefingStandardSlideOptions KeyPointsOptions(bool keyPointsOnly)
        => ProjectBriefingStandardSlideOptions.Default with
        {
            KeyPoints = ProjectBriefingKeyPointPlacement.CapabilitySlide,
            KeyPointsOnly = keyPointsOnly
        };

    private static ProjectBriefingProjectVm[] Projects(int count)
        => Enumerable.Range(1, count)
            .Select(id => new ProjectBriefingProjectVm
            {
                ProjectId = id,
                ProjectName = $"Project {id}",
                BriefDescription = LongCapability
            })
            .ToArray();

    private static ProjectBriefingSlideEstimateVm Estimate(
        ProjectBriefingStandardSlideOptions options,
        IReadOnlyList<ProjectBriefingProjectVm> projects)
        => ProjectBriefingDataService.BuildSlideEstimate(
            ProjectBriefingLayout.StandardBriefing,
            includeCoverSlide: false,
            includePortfolioSummarySlide: false,
            ProjectBriefingPresentationMode.DetailedProjects,
            includeStageSummary: false,
            includeProjectCategorySummary: false,
            includeTechnicalCategorySummary: false,
            options,
            ProjectBriefingUpdateSheetOptions.Default,
            ProjectBriefingInstitutionalProfileOptions.Default,
            ProjectBriefingRoleCharterOptions.Default,
            ProjectBriefingFfcGlobalFootprintOptions.Default,
            ffcCountryCount: 0,
            Array.Empty<ProjectBriefingAdditionalSlideType>(),
            ProjectBriefingCostMode.Both,
            ProjectBriefingNarrativeMode.CapabilityOverview,
            projects);
}
