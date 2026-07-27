namespace ProjectManagement.Services.Arpp;

public interface IArppLibraryService
{
    Task<ArppLibraryNavigation> GetNavigationAsync(
        string? query,
        CancellationToken cancellationToken = default);

    Task<ArppLibraryDocument?> GetDocumentAsync(
        long issueId,
        CancellationToken cancellationToken = default);

    Task<ArppLibraryCurrentPosition?> GetCurrentPositionAsync(
        int financialYearStart,
        string? query,
        CancellationToken cancellationToken = default);

    Task<ArppLibraryProjectHistory?> GetProjectHistoryAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<ArppLibraryAttachmentDownload?> OpenAttachmentAsync(
        long issueId,
        CancellationToken cancellationToken = default);
}
