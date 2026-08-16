using ProjectManagement.Models.Publications;
using ProjectManagement.Services.Compendiums;
using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase37_7CoverIdentityTests
{
    [Fact]
    public void CoverIdentityPolicy_ExposesExactlySixCuratedThemes()
    {
        var themes = Enum.GetValues<CompendiumPublicationTheme>();

        Assert.Equal(6, themes.Length);
        Assert.Contains(CompendiumPublicationTheme.InstitutionalGreen, themes);
        Assert.Contains(CompendiumPublicationTheme.DeepNavy, themes);
        Assert.Contains(CompendiumPublicationTheme.Burgundy, themes);
        Assert.Contains(CompendiumPublicationTheme.Graphite, themes);
        Assert.Contains(CompendiumPublicationTheme.DeepTeal, themes);
        Assert.Contains(CompendiumPublicationTheme.Slate, themes);
    }

    [Fact]
    public void CoverIdentityPolicy_ExposesExactlySixControlledBackgroundTreatments()
    {
        var treatments = Enum.GetValues<CompendiumCoverBackgroundTreatment>();

        Assert.Equal(6, treatments.Length);
        Assert.Contains(CompendiumCoverBackgroundTreatment.Solid, treatments);
        Assert.Contains(CompendiumCoverBackgroundTreatment.SubtleGradient, treatments);
        Assert.Contains(CompendiumCoverBackgroundTreatment.TopographicContours, treatments);
        Assert.Contains(CompendiumCoverBackgroundTreatment.TechnicalGrid, treatments);
        Assert.Contains(CompendiumCoverBackgroundTreatment.GeometricMesh, treatments);
        Assert.Contains(CompendiumCoverBackgroundTreatment.Camouflage, treatments);
    }

    [Theory]
    [InlineData(CompendiumPublicationTheme.InstitutionalGreen, true)]
    [InlineData(CompendiumPublicationTheme.DeepNavy, true)]
    [InlineData(CompendiumPublicationTheme.Graphite, true)]
    [InlineData(CompendiumPublicationTheme.Slate, true)]
    [InlineData(CompendiumPublicationTheme.Burgundy, false)]
    [InlineData(CompendiumPublicationTheme.DeepTeal, false)]
    public void CamouflageCompatibility_IsCuratedPerTheme(CompendiumPublicationTheme theme, bool expected)
    {
        Assert.Equal(expected, CompendiumCoverIdentityPolicy.IsCompatible(
            theme,
            CompendiumCoverBackgroundTreatment.Camouflage));
    }

    [Fact]
    public void InvalidThemeTreatmentCombination_NormalizesToSolid()
    {
        var result = CompendiumCoverIdentityPolicy.NormalizeTreatmentForTheme(
            CompendiumPublicationTheme.Burgundy,
            CompendiumCoverBackgroundTreatment.Camouflage);

        Assert.Equal(CompendiumCoverBackgroundTreatment.Solid, result);
    }

    [Fact]
    public void CleanBack_AlwaysUsesSolidPublicationColour()
    {
        var effective = CompendiumCoverIdentityPolicy.ResolveEffectiveTreatment(
            CompendiumCoverSurface.Back,
            CompendiumBackCoverTemplate.Clean,
            CompendiumPublicationTheme.DeepNavy,
            CompendiumCoverBackgroundTreatment.TopographicContours);

        Assert.Equal(CompendiumCoverBackgroundTreatment.Solid, effective);
    }

    [Fact]
    public void PatternSvg_IsDeterministicAndThemeAware()
    {
        var first = CompendiumCoverIdentityPolicy.BuildSurfaceSvg(
            CompendiumPublicationTheme.Graphite,
            CompendiumCoverBackgroundTreatment.Camouflage);
        var second = CompendiumCoverIdentityPolicy.BuildSurfaceSvg(
            CompendiumPublicationTheme.Graphite,
            CompendiumCoverBackgroundTreatment.Camouflage);
        var navy = CompendiumCoverIdentityPolicy.BuildSurfaceSvg(
            CompendiumPublicationTheme.DeepNavy,
            CompendiumCoverBackgroundTreatment.Camouflage);

        Assert.Equal(first, second);
        Assert.NotEqual(first, navy);
        Assert.Contains("viewBox=\"0 0 1000 1000\"", first, StringComparison.Ordinal);
        Assert.Contains("#22272B", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("random", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackPatternSurface_IsSubtlerThanFrontPatternSurface()
    {
        var front = CompendiumCoverIdentityPolicy.BuildSurfaceSvg(
            CompendiumPublicationTheme.InstitutionalGreen,
            CompendiumCoverBackgroundTreatment.TopographicContours,
            isBackSurface: false);
        var back = CompendiumCoverIdentityPolicy.BuildSurfaceSvg(
            CompendiumPublicationTheme.InstitutionalGreen,
            CompendiumCoverBackgroundTreatment.TopographicContours,
            isBackSurface: true);

        Assert.NotEqual(front, back);
        Assert.Contains("opacity=\"0.105\"", front, StringComparison.Ordinal);
        Assert.Contains("opacity=\"0.059\"", back, StringComparison.Ordinal);
    }

    [Fact]
    public void PresetAndCoverDesign_DefaultToLegacyGreenSolidIdentity()
    {
        var preset = new CompendiumPreset();
        var design = new CompendiumCoverDesignConfiguration();

        Assert.Equal(12, preset.SettingsSchemaVersion);
        Assert.Equal("InstitutionalGreen", preset.PublicationTheme);
        Assert.Equal("Solid", preset.CoverBackgroundTreatment);
        Assert.Equal(CompendiumPublicationTheme.InstitutionalGreen, design.PublicationTheme);
        Assert.Equal(CompendiumCoverBackgroundTreatment.Solid, design.BackgroundTreatment);
    }
}
