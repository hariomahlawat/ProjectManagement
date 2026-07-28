using System.Globalization;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services.Arpp;

/// <summary>
/// Registers post-commit audit records for ARPP-driven IPA lifecycle changes.
/// Stage updates remain part of the primary transaction; audit persistence is
/// post-commit so an audit-store problem cannot reverse published data.
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

        if (synchronization.Changes.Count == 0 &&
            synchronization.DataQualityIssues.Count == 0 &&
            synchronization.SupersededRequestCount == 0)
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
                    message: $"Synchronized the IPA stage from the first published ARPP position for project {change.ProjectId}.",
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
                        ["AuthorityIssueId"] = change.SourceIssueId.ToString(CultureInfo.InvariantCulture),
                        ["AuthorityDocument"] = change.SourceDocumentLabel,
                        ["SourceAction"] = sourceAction,
                        ["SourceIssueIds"] = issueIds
                    });
            }

            if (synchronization.SupersededRequestCount > 0)
            {
                await audit.LogAsync(
                    action: "Arpp.IpaStagePendingRequestsSuperseded",
                    message: $"Superseded {synchronization.SupersededRequestCount} pending IPA stage request(s) because published ARPP records became authoritative.",
                    userId: userId,
                    userName: userName,
                    data: new Dictionary<string, string?>
                    {
                        ["SupersededRequestCount"] = synchronization.SupersededRequestCount.ToString(CultureInfo.InvariantCulture),
                        ["SourceAction"] = sourceAction,
                        ["SourceIssueIds"] = issueIds
                    });
            }

            foreach (var issue in synchronization.DataQualityIssues)
            {
                await audit.LogAsync(
                    action: "Arpp.IpaStageDataQualityIssue",
                    message: $"IPA actual start is later than the ARPP-derived completion date for project {issue.ProjectId}.",
                    level: "Warning",
                    userId: userId,
                    userName: userName,
                    data: new Dictionary<string, string?>
                    {
                        ["ProjectId"] = issue.ProjectId.ToString(CultureInfo.InvariantCulture),
                        ["ActualStart"] = issue.ActualStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["CompletionDate"] = issue.CompletionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["AuthorityIssueId"] = issue.SourceIssueId.ToString(CultureInfo.InvariantCulture),
                        ["AuthorityDocument"] = issue.SourceDocumentLabel,
                        ["AuthorityIssueDate"] = issue.SourceIssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["SourceAction"] = sourceAction,
                        ["SourceIssueIds"] = issueIds
                    });
            }
        });
    }
}
