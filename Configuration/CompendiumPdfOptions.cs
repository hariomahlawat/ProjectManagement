using System.Collections.Generic;

namespace ProjectManagement.Configuration;

// SECTION: Configuration options for the Simulators Compendium PDF.
public sealed class CompendiumPdfOptions
{
    // SECTION: Cover page title text.
    public string Title { get; set; } = "Simulators Compendium";

    // SECTION: Organisation/unit text displayed on the cover page.
    public string UnitDisplayName { get; set; } = "";

    // SECTION: Category names that should be forced to the end of the index ordering.
    // Keep this list short and configurable to avoid hard-coding.
    public List<string> MiscCategoryNames { get; set; } = new() { "Misc", "Miscellaneous" };

    // SECTION: Which derivative size to fetch for cover photos.
    // Must match keys in ProjectPhotos:Derivatives (xl/md/sm/xs).
    public string CoverPhotoDerivativeKey { get; set; } = "md";

    // SECTION: If true, prefer WebP when available.
    public bool PreferWebp { get; set; }
}
