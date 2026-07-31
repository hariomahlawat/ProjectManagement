namespace ProjectManagement.Services.Workspace;

/// <summary>
/// Defines the project scope used by command conference review. This scope intentionally
/// differs from active workload: active projects remain in scope, and recently completed
/// projects remain available for a bounded carryover period.
/// </summary>
public interface IConferenceProjectScopeService
{
    int CompletedProjectRetentionDays { get; }

    Task<IReadOnlyList<ConferenceProjectCarryover>> GetRecentlyCompletedProjectsAsync(
        CancellationToken cancellationToken = default);

    Task<bool> IsProjectInScopeAsync(
        string officerUserId,
        int projectId,
        CancellationToken cancellationToken = default);
}

public sealed record ConferenceProjectCarryover(
    int ProjectId,
    string ProjectName,
    string OfficerUserId,
    string OfficerName,
    string OfficerRank,
    DateOnly? CompletedOn,
    int? CompletedYear,
    short? CompletedMonth,
    DateTime? CompletionRecordedAtUtc,
    DateOnly CompletionSortDate,
    string CompletionContext);
