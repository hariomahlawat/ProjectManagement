using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public interface IConferenceIdeaCommandService
{
    Task<CreateConferenceIdeaResult> CreateAsync(
        string actorUserId,
        CreateConferenceIdeaRequest request,
        CancellationToken cancellationToken = default);
}
