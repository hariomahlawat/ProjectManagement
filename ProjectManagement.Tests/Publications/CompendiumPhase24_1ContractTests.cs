using ProjectManagement.Models;
using ProjectManagement.Services.Compendiums;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase24_1ContractTests
{
    [Fact]
    public void Planner_UsesAdaptivePhotoLayoutsForNarrativeLength()
    {
        var shortPlan = new CompendiumPagePlanner().Plan(Context(Project(1, "Short", "Short description.", hasPhoto: true)));
        var mediumText = string.Join(" ", Enumerable.Repeat("medium narrative", 40));
        var mediumPlan = new CompendiumPagePlanner().Plan(Context(Project(2, "Medium", mediumText, hasPhoto: true)));
        var longText = string.Join(" ", Enumerable.Repeat("long narrative", 150));
        var longPlan = new CompendiumPagePlanner().Plan(Context(Project(3, "Long", longText, hasPhoto: true)));

        Assert.Equal(CompendiumProjectLayoutVariant.PhotoShort,
            Assert.Single(shortPlan.Pages.Where(page => page.Kind == CompendiumPageKind.Project)).ProjectLayout);
        Assert.Equal(CompendiumProjectLayoutVariant.PhotoMedium,
            Assert.Single(mediumPlan.Pages.Where(page => page.Kind == CompendiumPageKind.Project)).ProjectLayout);
        Assert.Equal(CompendiumProjectLayoutVariant.PhotoLong,
            Assert.Single(longPlan.Pages.Where(page => page.Kind == CompendiumPageKind.Project)).ProjectLayout);
    }

    [Fact]
    public void AdaptiveImagePolicy_GivesShortNarrativeMorePhysicalImageSpace()
    {
        var shortHeight = CompendiumPublicationImagePolicy.ResolveFrameHeightPoints("Short description.");
        var longHeight = CompendiumPublicationImagePolicy.ResolveFrameHeightPoints(
            string.Join(" ", Enumerable.Repeat("long narrative", 150)));

        Assert.True(shortHeight > longHeight);
        Assert.Equal(CompendiumPublicationImagePolicy.ShortFrameHeightPoints, shortHeight);
        Assert.Equal(CompendiumPublicationImagePolicy.LongFrameHeightPoints, longHeight);
    }

    [Fact]
    public void PublicationTextSanitizer_RemovesControlArtifactsButPreservesPublicationCharactersAndMarkdown()
    {
        var source = "**Capability**\u0001 costs ₹20 lakh\u00AD now.\r\nSecond line.";
        var clean = CompendiumPublicationTextSanitizer.Sanitize(source);

        Assert.DoesNotContain("\u0001", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("\u00AD", clean, StringComparison.Ordinal);
        Assert.Contains("**Capability**", clean, StringComparison.Ordinal);
        Assert.Contains("₹20 lakh- now.", clean, StringComparison.Ordinal);
        Assert.Contains("\nSecond line.", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void Readiness_SeparatesWorkflowReviewStateFromPublicationWarnings()
    {
        var policy = new CompendiumReadinessPolicy();
        var assessment = policy.Evaluate(new CompendiumProjectReadinessContext(
            1,
            "Ongoing project",
            ProjectLifecycleStatus.Active,
            null,
            null,
            "Description",
            null,
            null,
            10,
            true,
            CompendiumImageSelectionMode.Automatic,
            220,
            false,
            "current",
            null));

        Assert.False(assessment.IsReviewed);
        Assert.DoesNotContain(assessment.Findings, finding => finding.Code is "reviewRequired" or "projectChangedAfterReview");
        Assert.DoesNotContain(assessment.Findings, finding => finding.Code is "automaticImageSelected" or "proliferationNotAssessed");
        Assert.Contains(assessment.Findings, finding => finding.Code == "missingArmService");
    }

    [Fact]
    public void CoverPolicy_UsesOneDedicatedHeroGeometry()
    {
        Assert.Equal(491d, CompendiumCoverImagePolicy.FrameWidthPoints);
        Assert.Equal(300d, CompendiumCoverImagePolicy.FrameHeightPoints);
        Assert.Equal(1800, CompendiumCoverImagePolicy.RenderWidthPixels);
        Assert.Equal(1100, CompendiumCoverImagePolicy.RenderHeightPixels);
    }

    private static CompendiumPdfReportContext Context(CompendiumPdfProjectSection project)
        => new(
            "SDD Simulators Compendium",
            "Detailed Project Reference",
            "Simulator Development Division",
            "Simulator Development Division",
            null,
            DateTimeOffset.UtcNow,
            new[] { new CompendiumPdfCategorySection("AI", new[] { project }) },
            ShowMissingPhotoPlaceholder: false)
        {
            Edition = "Capability Edition · 2026"
        };

    private static CompendiumPdfProjectSection Project(int id, string name, string description, bool hasPhoto)
        => new(
            id,
            name,
            null,
            "AI",
            string.Empty,
            "Not recorded",
            "Not recorded",
            null,
            description,
            hasPhoto ? new byte[] { 1, 2, 3 } : null,
            PhotoWasSelected: hasPhoto)
        {
            LifecycleDisplay = "Ongoing",
            ProjectCategoryDisplay = "Other R&D Projects",
            ProliferationAvailability = null
        };
}
