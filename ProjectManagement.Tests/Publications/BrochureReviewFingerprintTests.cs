using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class BrochureReviewFingerprintTests
{
    private const string Narrative = "Authoritative publication copy for the selected project.";

    [Fact]
    public void CreateProject_IsDeterministicAndFixedLength()
    {
        var first = ProjectFingerprint();
        var second = ProjectFingerprint();

        Assert.Equal(first, second);
        Assert.Equal(BrochureReviewFingerprint.HexLength, first.Length);
        Assert.All(first, character => Assert.True(Uri.IsHexDigit(character)));
        Assert.True(BrochureReviewFingerprint.Matches(first, second));
    }

    [Theory]
    [InlineData("narrative")]
    [InlineData("primary-version")]
    [InlineData("secondary-version")]
    [InlineData("primary-crop")]
    [InlineData("secondary-crop")]
    [InlineData("image-mode")]
    [InlineData("project-name")]
    public void CreateProject_ChangesWhenReviewedPublicationInputChanges(string change)
    {
        var baseline = ProjectFingerprint();
        var changed = change switch
        {
            "narrative" => ProjectFingerprint(narrative: Narrative + " Updated."),
            "primary-version" => ProjectFingerprint(primaryVersion: 8),
            "secondary-version" => ProjectFingerprint(secondaryVersion: 12),
            "primary-crop" => ProjectFingerprint(primaryFocalX: .625d),
            "secondary-crop" => ProjectFingerprint(secondaryFocalY: .375d),
            "image-mode" => ProjectFingerprint(imageMode: BrochureImageMode.Single),
            "project-name" => ProjectFingerprint(projectName: "Renamed publication project"),
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        Assert.NotEqual(baseline, changed);
        Assert.False(BrochureReviewFingerprint.Matches(baseline, changed));
    }

    [Fact]
    public void CreateProject_UsesSameFourDecimalFocalPrecisionAsPostedForm()
    {
        var posted = ProjectFingerprint(primaryFocalX: .54321d);
        var rounded = ProjectFingerprint(primaryFocalX: .5432d);

        Assert.Equal(posted, rounded);
    }

    [Fact]
    public void CreateCover_BindsVisibleCoverCopyHeroVersionAndCrop()
    {
        var context = new BrochureCoverReviewContext(
            "SDD Capability Brochure",
            "Simulator Development Division",
            "Capability Edition · 2026",
            "Simulators of the Army, by the Army, for the Army",
            "RESTRICTED");
        var baseline = BrochureReviewFingerprint.CreateCover(
            context,
            BrochurePublicationProfile.PrintCompact,
            heroProjectId: 17,
            heroPhotoId: 42,
            heroPhotoVersion: 6,
            focalX: .5d,
            focalY: .5d);
        var newPhotoVersion = BrochureReviewFingerprint.CreateCover(
            context,
            BrochurePublicationProfile.PrintCompact,
            heroProjectId: 17,
            heroPhotoId: 42,
            heroPhotoVersion: 7,
            focalX: .5d,
            focalY: .5d);
        var changedTitle = BrochureReviewFingerprint.CreateCover(
            context with { Title = "Updated capability brochure" },
            BrochurePublicationProfile.PrintCompact,
            heroProjectId: 17,
            heroPhotoId: 42,
            heroPhotoVersion: 6,
            focalX: .5d,
            focalY: .5d);

        Assert.NotEqual(baseline, newPhotoVersion);
        Assert.NotEqual(baseline, changedTitle);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-fingerprint")]
    [InlineData("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    public void Matches_RejectsMissingMalformedOrDifferentValue(string? submitted)
    {
        Assert.False(BrochureReviewFingerprint.Matches(submitted, ProjectFingerprint()));
    }

    private static string ProjectFingerprint(
        string projectName = "Reviewed publication project",
        string narrative = Narrative,
        int primaryVersion = 7,
        int secondaryVersion = 11,
        double primaryFocalX = .5d,
        double secondaryFocalY = .5d,
        BrochureImageMode imageMode = BrochureImageMode.GalleryTwo)
        => BrochureReviewFingerprint.CreateProject(
            projectId: 17,
            projectName: projectName,
            projectCategory: "AR / VR",
            technicalCategory: "Simulator",
            narrativeSource: BrochureNarrativeSource.ProjectBrief,
            narrative: narrative,
            primaryPhotoId: 42,
            primaryPhotoVersion: primaryVersion,
            secondaryPhotoId: 43,
            secondaryPhotoVersion: secondaryVersion,
            primaryFocalX: primaryFocalX,
            primaryFocalY: .5d,
            secondaryFocalX: .5d,
            secondaryFocalY: secondaryFocalY,
            imageMode: imageMode);
}
