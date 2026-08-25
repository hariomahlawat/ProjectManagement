namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Single production identity for the Compendium authoring, planning and PDF pipeline.
/// Keep this value stable inside a deployed build so response headers, PDF metadata,
/// self-test output and diagnostic records can be correlated without source access.
/// </summary>
public static class CompendiumBuildIdentity
{
    public const string Phase = "46";
    public const string BuildStamp = "CompendiumPdf_2026-08-25_phase46-dossier-defaults";
    // Left-aligned reviews retain the v19 fingerprint byte-for-byte. Existing Justified reviews
    // moved to v20 in Phase 44 because their physical side-column treatment changed. Phase 45
    // corrects flow/proof geometry without a blanket review-fingerprint reset.
    public const string ReviewContract = "compendium-review-v19-left-v20-semantic-justification";
    public const string PdfContract = "physical-a4-v46";
    public const string HeaderName = "X-PRISM-Compendium-Build";
    public const string PdfProducer = "PRISM ERP / QuestPDF / Phase 46";

    // Compendium generation is deliberately serialized per worker process. A production
    // publication can contain 78+ image-rich dossiers and must not compete with a second
    // full renderer for the same native Skia/QuestPDF memory budget.
    public const int MaximumConcurrentGenerations = 1;
}
