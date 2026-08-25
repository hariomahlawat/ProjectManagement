using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase46DossierPresentationDefaultsTests
{
    [Fact]
    public void Resolve_UsesPublicationDefaultsWhenProjectHasNoOverrides()
    {
        var defaults = new CompendiumDossierPresentationDefaults
        {
            DossierLayout = CompendiumDossierLayout.Balanced,
            BalancedTextFlowMode = CompendiumBalancedTextFlowMode.SideColumn,
            NarrativeAlignment = CompendiumNarrativeAlignment.Justified,
            ImageFitMode = CompendiumImageFitMode.Fit
        };
        var selection = new CompendiumProjectSelection(42);

        var resolved = CompendiumDossierPresentationPolicy.Resolve(defaults, selection);

        Assert.Equal(CompendiumDossierLayout.Balanced, resolved.DossierLayout);
        Assert.Equal(CompendiumBalancedTextFlowMode.SideColumn, resolved.BalancedTextFlowMode);
        Assert.Equal(CompendiumNarrativeAlignment.Justified, resolved.NarrativeAlignment);
        Assert.Equal(CompendiumImageFitMode.Fit, resolved.ImageFitMode);
        Assert.False(resolved.UsesDossierLayoutOverride);
        Assert.False(resolved.UsesBalancedTextFlowOverride);
        Assert.False(resolved.UsesNarrativeAlignmentOverride);
        Assert.False(resolved.UsesImageFitOverride);
    }

    [Fact]
    public void Resolve_ProjectOverridesWinEvenWhenTheyMatchOrDifferFromDefaults()
    {
        var defaults = new CompendiumDossierPresentationDefaults
        {
            DossierLayout = CompendiumDossierLayout.VisualHero,
            BalancedTextFlowMode = CompendiumBalancedTextFlowMode.FlowBelowImage,
            NarrativeAlignment = CompendiumNarrativeAlignment.Justified,
            ImageFitMode = CompendiumImageFitMode.Fill
        };
        var selection = new CompendiumProjectSelection(42)
        {
            DossierLayoutOverride = CompendiumDossierLayout.Automatic,
            BalancedTextFlowModeOverride = CompendiumBalancedTextFlowMode.SideColumn,
            NarrativeAlignmentOverride = CompendiumNarrativeAlignment.Left,
            ImageFitModeOverride = CompendiumImageFitMode.Fit
        };

        var resolved = CompendiumDossierPresentationPolicy.Resolve(defaults, selection);

        Assert.Equal(CompendiumDossierLayout.Automatic, resolved.DossierLayout);
        Assert.Equal(CompendiumBalancedTextFlowMode.SideColumn, resolved.BalancedTextFlowMode);
        Assert.Equal(CompendiumNarrativeAlignment.Left, resolved.NarrativeAlignment);
        Assert.Equal(CompendiumImageFitMode.Fit, resolved.ImageFitMode);
        Assert.True(resolved.UsesDossierLayoutOverride);
        Assert.True(resolved.UsesBalancedTextFlowOverride);
        Assert.True(resolved.UsesNarrativeAlignmentOverride);
        Assert.True(resolved.UsesImageFitOverride);
    }
}
