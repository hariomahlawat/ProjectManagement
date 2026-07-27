using System.Globalization;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Arpp;

/// <summary>
/// Registers post-commit audit records for ARPP-driven IPA lifecycle changes.
/// The stage update remains part of the primary transaction; audit persistence is
/// deliberately post-commit so an audit-store problem cannot reverse published data.
/// </summary>
internal static class ArppIpaStageSynchronizationAudit
{
    public static void Register(
        RelationalTransactionScope transaction,
        IAuditService audit,
        ArppIpaStageSynchronizationResult synchronization,
        string userId,
        string? userName,
        string sourceAction,
        IReadOnlyCollection<long> sourceIssueIds)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(synchronization);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAction);
        ArgumentNullException.ThrowIfNull(sourceIssueIds);

        if (synchronization.Changes.Count == 0)
        {
            return;
        }

        var issueIds = string.Join(",", sourceIssueIds.Distinct().OrderBy(issueId => issueId));

        transaction.RegisterAfterCommit(async _ =>
        {
            foreach (var change in synchronization.Changes)
            {
                await audit.LogAsync(
                    action: "Arpp.IpaStageSynchronized",
                    message: $"Synchronized the IPA stage from the earliest published ARPP position for project {change.ProjectId}.",
                    userId: userId,
                    userName: userName,
                    data: new Dictionary<string, string?>
                    {
                        ["ProjectId"] = change.ProjectId.ToString(CultureInfo.InvariantCulture),
                        ["CompletionDate"] = change.CompletionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["PreviousStatus"] = change.PreviousStatus.ToString(),
                        ["PreviousCompletionDate"] = change.PreviousCompletedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["PreviousActualStart"] = change.PreviousActualStart?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["StageCreated"] = change.StageCreated.ToString(CultureInfo.InvariantCulture),
                        ["SourceAction"] = sourceAction,
                        ["SourceIssueIds"] = issueIds
                    });
            }
        });
    }
}
