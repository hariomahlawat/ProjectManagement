namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public interface IProjectBriefingPowerPointExportService
{
    Task<ProjectBriefingExportResult> GenerateAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default);

    // Builds the same PowerPoint as GenerateAsync without recording a generation. The slide
    // preview uses it so the preview and the download always come from one composition.
    Task<ProjectBriefingComposedDeck> ComposeAsync(
        long deckId,
        string requestingUserId,
        CancellationToken cancellationToken = default);
}
