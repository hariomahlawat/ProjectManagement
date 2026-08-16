namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Print-protective image quality policy for Compendium automatic composition. Manual publisher
/// layout choices remain available with warnings; Automatic is deliberately conservative.
/// </summary>
public static class CompendiumImageQualityPolicy
{
    public const int PreferredPrintDpi = 200;
    public const int AcceptablePrintDpi = 150;
    public const int MinimumLargeImageDpi = 120;

    public static bool IsAutomaticLayoutAllowed(CompendiumDossierLayout layout, int? effectiveDpi)
    {
        if (effectiveDpi is not > 0) return true;
        if (effectiveDpi.Value < MinimumLargeImageDpi)
            return layout is CompendiumDossierLayout.Balanced or CompendiumDossierLayout.Technical;
        if (effectiveDpi.Value < AcceptablePrintDpi)
            return layout is CompendiumDossierLayout.Balanced or CompendiumDossierLayout.Technical;
        return true;
    }

    public static float MaximumAutomaticImageHeight(
        CompendiumDossierLayout layout,
        float preferredHeightPoints,
        int? effectiveDpi)
    {
        if (effectiveDpi is not > 0) return float.MaxValue;
        if (effectiveDpi.Value >= AcceptablePrintDpi) return float.MaxValue;

        var scaled = preferredHeightPoints * Math.Max(.35f, effectiveDpi.Value / (float)AcceptablePrintDpi);
        var floor = layout switch
        {
            CompendiumDossierLayout.Technical => 82f,
            CompendiumDossierLayout.Balanced => 96f,
            _ => 105f
        };
        return Math.Max(floor, scaled);
    }

    public static int AutomaticLayoutPenalty(CompendiumDossierLayout layout, int? effectiveDpi)
    {
        if (effectiveDpi is not > 0 || effectiveDpi.Value >= PreferredPrintDpi) return 0;
        if (layout is not (CompendiumDossierLayout.VisualHero or CompendiumDossierLayout.MultiImageEditorial)) return 0;
        if (effectiveDpi.Value < AcceptablePrintDpi) return 500;
        return (PreferredPrintDpi - effectiveDpi.Value) * 2;
    }
}
