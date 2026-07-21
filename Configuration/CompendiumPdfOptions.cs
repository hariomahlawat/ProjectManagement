using System.Collections.Generic;

namespace ProjectManagement.Configuration;

// SECTION: Configuration options for the Simulators Compendium PDF.
public sealed class CompendiumPdfOptions
{
    public const string SectionName = "CompendiumPdf";

    // SECTION: Publication identity.
    public string Title { get; set; } = "Simulators Compendium";
    public string Subtitle { get; set; } = "Available for Proliferation";
    public string UnitDisplayName { get; set; } = "Simulator Development Division";
    public string IssuerDisplayName { get; set; } = "Simulator Development Division";
    public string FileNamePrefix { get; set; } = "SDD_Simulators_Compendium";

    // SECTION: Category names that should be forced to the end of the index ordering.
    public List<string> MiscCategoryNames { get; set; } = new() { "Misc", "Miscellaneous" };

    // SECTION: Project-photo rendering.
    // Must match a key configured under ProjectPhotos:Derivatives.
    public string CoverPhotoDerivativeKey { get; set; } = "md";

    // Prefer a PDF-friendly raster derivative first. The export still falls back to
    // every supported derivative format when the preferred format is unavailable.
    public string PreferredPhotoFormat { get; set; } = "jpg";

    // Retained for backward compatibility with existing configuration. When true,
    // WebP is attempted after the explicitly preferred format.
    public bool PreferWebp { get; set; }

    public bool ShowMissingPhotoPlaceholder { get; set; } = true;
}
