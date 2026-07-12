using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public interface IConferenceTaskCommandService
{
    Task<CreateConferenceTaskResult> CreateAsync(
        string actorUserId,
        CreateConferenceTaskRequest request,
        CancellationToken cancellationToken = default);
}
