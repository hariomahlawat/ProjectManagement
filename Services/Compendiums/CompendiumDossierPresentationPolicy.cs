namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Single authoritative resolver for Compendium dossier presentation inheritance.
/// Publication defaults establish the normal editorial language; nullable project overrides
/// create deliberate exceptions without materialising inherited values into project state.
/// </summary>
public static class CompendiumDossierPresentationPolicy
{
    public static CompendiumResolvedDossierPresentation Resolve(
        CompendiumDossierPresentationDefaults? defaults,
        CompendiumProjectSelection selection)
    {
        defaults ??= new CompendiumDossierPresentationDefaults();

        var defaultLayout = Normalize(defaults.DossierLayout, CompendiumDossierLayout.Automatic);
        var defaultFlow = Normalize(defaults.BalancedTextFlowMode, CompendiumBalancedTextFlowMode.FlowBelowImage);
        var defaultAlignment = CompendiumNarrativeTypographyPolicy.Normalize(defaults.NarrativeAlignment);
        var defaultImageFit = Normalize(defaults.ImageFitMode, CompendiumImageFitMode.Fill);

        var layoutOverride = NormalizeNullable(selection.DossierLayoutOverride);
        var flowOverride = NormalizeNullable(selection.BalancedTextFlowModeOverride);
        CompendiumNarrativeAlignment? alignmentOverride = selection.NarrativeAlignmentOverride.HasValue
            && Enum.IsDefined(selection.NarrativeAlignmentOverride.Value)
                ? CompendiumNarrativeTypographyPolicy.Normalize(selection.NarrativeAlignmentOverride.Value)
                : null;
        var imageFitOverride = NormalizeNullable(selection.ImageFitModeOverride);

        // Inheritance contract: DossierLayoutOverride ?? default; BalancedTextFlowModeOverride ?? default;
        // ImageFitModeOverride ?? default; NarrativeAlignmentOverride ?? default.

        return new CompendiumResolvedDossierPresentation(
            layoutOverride ?? defaultLayout,
            flowOverride ?? defaultFlow,
            alignmentOverride ?? defaultAlignment,
            imageFitOverride ?? defaultImageFit,
            layoutOverride.HasValue,
            flowOverride.HasValue,
            alignmentOverride.HasValue,
            imageFitOverride.HasValue);
    }

    public static CompendiumDossierPresentationDefaults Normalize(CompendiumDossierPresentationDefaults? defaults)
    {
        defaults ??= new CompendiumDossierPresentationDefaults();
        return new CompendiumDossierPresentationDefaults
        {
            DossierLayout = Normalize(defaults.DossierLayout, CompendiumDossierLayout.Automatic),
            BalancedTextFlowMode = Normalize(defaults.BalancedTextFlowMode, CompendiumBalancedTextFlowMode.FlowBelowImage),
            NarrativeAlignment = CompendiumNarrativeTypographyPolicy.Normalize(defaults.NarrativeAlignment),
            ImageFitMode = Normalize(defaults.ImageFitMode, CompendiumImageFitMode.Fill)
        };
    }

    private static T Normalize<T>(T value, T fallback) where T : struct, Enum
        => Enum.IsDefined(value) ? value : fallback;

    private static T? NormalizeNullable<T>(T? value) where T : struct, Enum
        => value.HasValue && Enum.IsDefined(value.Value) ? value : null;
}

public sealed record CompendiumResolvedDossierPresentation(
    CompendiumDossierLayout DossierLayout,
    CompendiumBalancedTextFlowMode BalancedTextFlowMode,
    CompendiumNarrativeAlignment NarrativeAlignment,
    CompendiumImageFitMode ImageFitMode,
    bool UsesDossierLayoutOverride,
    bool UsesBalancedTextFlowOverride,
    bool UsesNarrativeAlignmentOverride,
    bool UsesImageFitOverride);
