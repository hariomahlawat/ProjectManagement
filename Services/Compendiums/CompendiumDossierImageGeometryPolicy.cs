namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Shared physical image geometry for dossier planning/proof/PDF. Fill owns the requested frame;
/// Fit preserves source aspect ratio and returns the actual occupied height so invisible white
/// frame capacity cannot delay narrative continuation.
/// </summary>
public static class CompendiumDossierImageGeometryPolicy
{
    public sealed record Geometry(
        float FrameWidthPoints,
        float MaximumHeightPoints,
        float RenderedWidthPoints,
        float RenderedHeightPoints,
        CompendiumImageFitMode FitMode)
    {
        public bool HasKnownSourceGeometry { get; init; }
    }

    public static Geometry Resolve(
        float frameWidthPoints,
        float maximumHeightPoints,
        int? sourceWidth,
        int? sourceHeight,
        CompendiumImageFitMode fitMode)
    {
        frameWidthPoints = Math.Max(1f, frameWidthPoints);
        maximumHeightPoints = Math.Max(1f, maximumHeightPoints);
        fitMode = Enum.IsDefined(fitMode) ? fitMode : CompendiumImageFitMode.Fill;

        if (fitMode != CompendiumImageFitMode.Fit
            || sourceWidth is not > 0
            || sourceHeight is not > 0)
        {
            return new Geometry(
                frameWidthPoints,
                maximumHeightPoints,
                frameWidthPoints,
                maximumHeightPoints,
                fitMode)
            {
                HasKnownSourceGeometry = sourceWidth is > 0 && sourceHeight is > 0
            };
        }

        var scale = Math.Min(
            frameWidthPoints / sourceWidth.Value,
            maximumHeightPoints / sourceHeight.Value);
        var renderedWidth = Math.Max(1f, sourceWidth.Value * scale);
        var renderedHeight = Math.Max(1f, sourceHeight.Value * scale);

        return new Geometry(
            frameWidthPoints,
            maximumHeightPoints,
            renderedWidth,
            renderedHeight,
            fitMode)
        {
            HasKnownSourceGeometry = true
        };
    }
}
