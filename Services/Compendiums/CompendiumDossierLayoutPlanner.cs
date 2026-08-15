namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Deterministic layout resolver shared by browser review and PDF planning. The resolver treats
/// text readability as authoritative: as content pressure rises, photography yields space before
/// publication typography is reduced.
/// </summary>
public static class CompendiumDossierLayoutPlanner
{
    public sealed record Decision(
        CompendiumDossierLayout Layout,
        string Reason,
        int PressureScore,
        bool RequiresTechnicalEmphasis);

    public static Decision Resolve(
        CompendiumDossierLayout requested,
        int availablePhotoCount,
        string? narrative,
        IReadOnlyList<string>? technicalSpecifications,
        int programmeModuleCount,
        string? projectName)
    {
        availablePhotoCount = Math.Clamp(availablePhotoCount, 0, 3);
        var narrativeLength = EstimateTextPressure(narrative);
        var specificationPressure = (technicalSpecifications ?? Array.Empty<string>())
            .Sum(EstimateTextPressure);
        var titlePressure = Math.Max(0, ((projectName?.Trim().Length ?? 0) - 52) / 18);
        var programmePressure = Math.Max(0, programmeModuleCount - 2) * 2;
        var pressure = narrativeLength + specificationPressure + titlePressure + programmePressure;
        var technicalHeavy = specificationPressure >= 15 || (technicalSpecifications?.Count ?? 0) >= 5;

        if (requested != CompendiumDossierLayout.Automatic)
        {
            var compatible = requested != CompendiumDossierLayout.MultiImageEditorial || availablePhotoCount >= 2;
            if (compatible)
            {
                return new Decision(requested, "Publisher-selected layout", pressure, technicalHeavy);
            }
        }

        if (technicalHeavy || pressure >= 42)
        {
            return new Decision(
                CompendiumDossierLayout.Technical,
                "Technical content requires additional readable space",
                pressure,
                true);
        }

        if (availablePhotoCount >= 2 && pressure <= 31)
        {
            return new Decision(
                CompendiumDossierLayout.MultiImageEditorial,
                "Multiple publication photographs are available with moderate content pressure",
                pressure,
                false);
        }

        if (availablePhotoCount >= 1 && pressure <= 18)
        {
            return new Decision(
                CompendiumDossierLayout.VisualHero,
                "Short content allows photography to lead the dossier",
                pressure,
                false);
        }

        return new Decision(
            CompendiumDossierLayout.Balanced,
            "Balanced composition provides the best fit for the current content",
            pressure,
            technicalHeavy);
    }

    private static int EstimateTextPressure(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        // Character pressure is more stable than word count for long technical bullets.
        var cleanLength = text.Trim().Length;
        return Math.Clamp((cleanLength + 109) / 110, 1, 40);
    }
}
