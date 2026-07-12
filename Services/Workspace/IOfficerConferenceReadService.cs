using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public interface IOfficerConferenceReadService
{
    Task<OfficerConferenceVm?> GetAsync(
        string requestingUserId,
        string officerUserId,
        CancellationToken cancellationToken = default);
}
