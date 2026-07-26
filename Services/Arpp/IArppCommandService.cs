namespace ProjectManagement.Services.Arpp;

public interface IArppCommandService
{
    Task<ArppCommandResult> CreateIssueAsync(
        ArppIssueCreateCommand command,
        CancellationToken cancellationToken = default);

    Task<ArppCommandResult> SaveWorkspaceAsync(
        ArppWorkspaceSaveCommand command,
        CancellationToken cancellationToken = default);
}
