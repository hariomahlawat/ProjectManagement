using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.ConferenceRemarks;

public interface IConferenceRemarkCommandService
{
    Task<AddConferenceRemarkResult> AddAsync(
        string requestingUserId,
        AddConferenceRemarkRequest request,
        CancellationToken cancellationToken = default);
}
