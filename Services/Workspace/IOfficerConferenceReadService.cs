using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public interface IOfficerConferenceReadService
{
    Task<IReadOnlyList<OfficerConferenceOfficerOptionVm>> GetOfficerOptionsAsync(
        string requestingUserId,
        string? selectedOfficerUserId = null,
        CancellationToken cancellationToken = default);

    Task<OfficerConferenceVm?> GetAsync(
        string requestingUserId,
        string officerUserId,
        CancellationToken cancellationToken = default);

    Task<ConferenceDirectionHistoryVm?> GetDirectionHistoryAsync(
        string requestingUserId,
        string officerUserId,
        ConferenceItemKind kind,
        int itemId,
        CancellationToken cancellationToken = default);
}
