using ProjectManagement.Models.Arpp;

namespace ProjectManagement.Services.Arpp;

/// <summary>
/// Resolves the first published HQ ARPP position that established a project's
/// In-Principle Approval milestone and enforces the resulting lifecycle lock.
/// </summary>
public interface IArppIpaStageAuthorityService
{
    Task<ArppIpaStageAuthority?> ResolveAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, ArppIpaStageAuthority>> ResolveManyAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default);

    Task<bool> IsManagedAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task EnsureManualLifecycleMutationAllowedAsync(
        int projectId,
        string stageCode,
        CancellationToken cancellationToken = default);
}

public sealed record ArppIpaStageAuthority(
    int ProjectId,
    int FinancialYearStart,
    long IssueId,
    ArppIssueKind IssueKind,
    int IssueSequence,
    string IssueName,
    DateOnly IssueDate,
    long EntryId,
    string? SerialNumber)
{
    public string DocumentLabel => IssueKind == ArppIssueKind.Original
        ? "Original ARPP"
        : $"Addendum No. {IssueSequence}";
}

public sealed class ArppManagedIpaStageException : InvalidOperationException
{
    public const string UserMessage =
        "In-Principle Approval status and completion date are controlled by published ARPP records. Update the authoritative ARPP record or project linkage instead.";

    public ArppManagedIpaStageException(int projectId)
        : base(UserMessage)
    {
        ProjectId = projectId;
    }

    public int ProjectId { get; }
}
