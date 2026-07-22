namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public interface IProjectBriefingPowerPointExportService
{
    Task<ProjectBriefingExportResult> GenerateAsync(
        long deckId,
        string ownerUserId,
        CancellationToken cancellationToken = default);
}
