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

    /// <summary>
    /// Returns the conference view for the authenticated Project Officer only. The
    /// officer identity is the same value used for both requester and subject so a
    /// Project Officer can never use this surface to inspect another officer.
    /// </summary>
    Task<OfficerConferenceVm?> GetForProjectOfficerAsync(
        string projectOfficerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the live command digest used by Notebook. Only the latest conference
    /// direction for each in-scope project, idea or task is included. Items without a
    /// conference direction, and officers with no directed items, are omitted.
    /// </summary>
    Task<ConferenceDirectionDigestVm?> GetLatestDirectionDigestAsync(
        string requestingUserId,
        CancellationToken cancellationToken = default);

    Task<ConferenceDirectionHistoryVm?> GetDirectionHistoryAsync(
        string requestingUserId,
        string officerUserId,
        ConferenceItemKind kind,
        int itemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns direction history only for work assigned to the authenticated Project
    /// Officer. No caller-supplied officer identifier is accepted.
    /// </summary>
    Task<ConferenceDirectionHistoryVm?> GetDirectionHistoryForProjectOfficerAsync(
        string projectOfficerUserId,
        ConferenceItemKind kind,
        int itemId,
        CancellationToken cancellationToken = default);
}
