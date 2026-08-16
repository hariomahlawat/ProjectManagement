namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Editorial validity rules applied before residual-space optimisation. Phase 37.2 deliberately
/// separates "physically fits" from "looks publishable": a one-page candidate may fit the A4
/// envelope and still be rejected if the photograph has been reduced to a token strip or a
/// Balanced side column produces a large unmatched text tower/void beside the image.
/// </summary>
public static class CompendiumDossierEditorialPolicy
{
    public const float PreferredSideBalanceTolerancePoints = 28f;
    public const float MaximumSideOverflowAbsolutePoints = 56f;
    public const float MaximumSideUnderfillAbsolutePoints = 72f;
    public const float MaximumSideOverflowFraction = .18f;
    public const float MaximumSideUnderfillFraction = .26f;

    public sealed record SideColumnAssessment(
        float ImageHeightPoints,
        float NarrativeHeightPoints,
        float OverflowHeightPoints,
        float UnderfillHeightPoints,
        float BalanceRatio,
        bool IsEditoriallyBalanced,
        string? Warning);

    /// <summary>
    /// Minimum normal Fill height for a primary dossier image. These are editorial floors, not
    /// pagination rescue values. Fit is exempt because the full source is preserved and its actual
    /// rendered aspect-ratio height is authoritative.
    /// </summary>
    public static float MinimumEditorialFillHeightPoints(CompendiumDossierLayout layout)
        => layout switch
        {
            CompendiumDossierLayout.VisualHero => 185f,
            CompendiumDossierLayout.MultiImageEditorial => 185f,
            CompendiumDossierLayout.Technical => 82f,
            _ => 165f
        };

    public static bool IsImageGeometryEditoriallyValid(
        CompendiumDossierLayout layout,
        CompendiumImageFitMode fitMode,
        bool hasPhoto,
        float renderedImageHeightPoints)
    {
        if (!hasPhoto || fitMode == CompendiumImageFitMode.Fit)
            return true;

        return renderedImageHeightPoints + .1f >= MinimumEditorialFillHeightPoints(layout);
    }

    public static SideColumnAssessment AssessSideColumn(float imageHeightPoints, float narrativeHeightPoints)
    {
        var image = Math.Max(1f, imageHeightPoints);
        var narrative = Math.Max(0f, narrativeHeightPoints);
        var overflow = Math.Max(0f, narrative - image);
        var underfill = Math.Max(0f, image - narrative);
        var maximumOverflow = Math.Max(MaximumSideOverflowAbsolutePoints, image * MaximumSideOverflowFraction);
        var maximumUnderfill = Math.Max(MaximumSideUnderfillAbsolutePoints, image * MaximumSideUnderfillFraction);
        var smaller = Math.Max(1f, Math.Min(image, Math.Max(1f, narrative)));
        var larger = Math.Max(image, narrative);
        var ratio = larger / smaller;
        var balanced = overflow <= maximumOverflow + .1f && underfill <= maximumUnderfill + .1f;

        string? warning = null;
        if (overflow > maximumOverflow + .1f)
        {
            warning = "Side column text extends substantially below the publication image. Use Flow below image or another dossier layout for a more balanced page.";
        }
        else if (underfill > maximumUnderfill + .1f)
        {
            warning = "Side column text ends substantially above the publication image. Flow below image or a more image-led layout will provide a better editorial balance.";
        }

        return new SideColumnAssessment(
            image,
            narrative,
            overflow,
            underfill,
            ratio,
            balanced,
            warning);
    }
}
