using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase38CoverReliabilityTests
{
    [Fact]
    public void Automatic_cover_policy_is_deterministic_and_curated_preferences_win()
    {
        var projects = new[]
        {
            new CompendiumCoverAutomaticImagePolicy.ProjectSource(10, 100, .35d, .65d, 0),
            new CompendiumCoverAutomaticImagePolicy.ProjectSource(20, 200, .5d, .5d, 1)
        };
        var preferences = new[]
        {
            new CompendiumPhotoPreference(20, 201, PreferredForPublication: true, SuitableForCoverHero: false),
            new CompendiumPhotoPreference(10, 101, PreferredForPublication: false, SuitableForCoverHero: true)
        };

        var candidates = CompendiumCoverAutomaticImagePolicy.BuildCandidates(projects, preferences);

        Assert.Equal((10, 101), (candidates[0].ProjectId, candidates[0].PhotoId));
        Assert.Equal((20, 201), (candidates[1].ProjectId, candidates[1].PhotoId));
        Assert.Contains(candidates, candidate => candidate.ProjectId == 10 && candidate.PhotoId == 100);
    }

    [Fact]
    public void Automatic_cover_policy_preserves_project_focal_point_for_its_resolved_cover_photo()
    {
        var candidates = CompendiumCoverAutomaticImagePolicy.BuildCandidates(
            new[] { new CompendiumCoverAutomaticImagePolicy.ProjectSource(10, 100, .23d, .71d, 0) },
            Array.Empty<CompendiumPhotoPreference>());

        var candidate = Assert.Single(candidates);
        Assert.Equal(.23d, candidate.FocalX, 5);
        Assert.Equal(.71d, candidate.FocalY, 5);
    }

    [Fact]
    public void Cover_typography_policy_reduces_dense_titles_but_respects_minimum()
    {
        var normal = CompendiumCoverTypographyPolicy.ResolveTitleSize("SDD Simulators Compendium", 34f);
        var dense = CompendiumCoverTypographyPolicy.ResolveTitleSize(new string('A', 120), 28f);

        Assert.Equal(34f, normal);
        Assert.True(dense < 28f);
        Assert.True(dense >= CompendiumCoverTypographyPolicy.MinimumTitleSize);
        Assert.True(CompendiumCoverTypographyPolicy.NeedsAdvisory(new string('A', 120), null));
    }

    [Fact]
    public void Cover_template_policy_identifies_required_non_quartet_slots()
    {
        var fullBleed = CompendiumCoverTemplatePolicy.RequiredSlotKeys(
            CompendiumCoverSurface.Front,
            CompendiumFrontCoverTemplate.FullBleedHero,
            CompendiumBackCoverTemplate.MinimalInstitutional);
        var imageEcho = CompendiumCoverTemplatePolicy.RequiredSlotKeys(
            CompendiumCoverSurface.Back,
            CompendiumFrontCoverTemplate.Minimal,
            CompendiumBackCoverTemplate.ImageEcho);

        Assert.Contains("Hero", fullBleed);
        Assert.Contains("Hero", imageEcho);
    }
}
