namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Single production identity for the Compendium authoring, planning and PDF pipeline.
/// Keep this value stable inside a deployed build so response headers, PDF metadata,
/// self-test output and diagnostic records can be correlated without source access.
/// </summary>
public static class CompendiumBuildIdentity
{
    public const string Phase = "43";
    public const string BuildStamp = "CompendiumPdf_2026-08-23_phase43-cover-proof-parity";
    // Phase 43 corrects browser/PDF cover geometry parity only; it does not change the
    // per-project review fingerprint contract.
    public const string ReviewContract = "compendium-review-v19-cover-identity";
    public const string PdfContract = "physical-a4-v43";
    public const string HeaderName = "X-PRISM-Compendium-Build";
    public const string PdfProducer = "PRISM ERP / QuestPDF / Phase 43";

    // Compendium generation is deliberately serialized per worker process. A production
    // publication can contain 78+ image-rich dossiers and must not compete with a second
    // full renderer for the same native Skia/QuestPDF memory budget.
    public const int MaximumConcurrentGenerations = 1;
}
