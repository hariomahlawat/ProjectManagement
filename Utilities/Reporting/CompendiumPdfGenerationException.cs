namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Identifies the production stage at which Compendium PDF generation failed. The exception carries
/// only publication-safe diagnostics; the original exception remains available as InnerException
/// and is written to the application log with the request TraceIdentifier.
/// </summary>
public enum CompendiumPdfGenerationStage
{
    FontInitialization = 0,
    PagePlanning = 1,
    PdfComposition = 2,
    PdfLayout = 3,
    PdfDrawing = 4,
    PdfVerification = 5,
    PublicationRead = 6,
    ImagePreparation = 7,
    CoverResolution = 8
}

public sealed class CompendiumPdfGenerationException : InvalidOperationException
{
    public CompendiumPdfGenerationException(
        CompendiumPdfGenerationStage stage,
        string message,
        Exception? innerException = null,
        int? plannedPhysicalPage = null,
        CompendiumPageKind? pageKind = null,
        int? projectId = null,
        string? projectName = null)
        : base(message, innerException)
    {
        Stage = stage;
        PlannedPhysicalPage = plannedPhysicalPage;
        PageKind = pageKind;
        ProjectId = projectId;
        ProjectName = projectName;
    }

    public CompendiumPdfGenerationStage Stage { get; }
    public int? PlannedPhysicalPage { get; }
    public CompendiumPageKind? PageKind { get; }
    public int? ProjectId { get; }
    public string? ProjectName { get; }
}
