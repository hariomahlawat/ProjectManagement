using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

internal sealed record WorkspaceActionQueueBuildResult(
    IReadOnlyList<WorkspaceActionQueueItemVm> Items,
    IReadOnlyList<WorkspaceActionQueueGroupVm> Groups,
    int TotalCount);

/// <summary>
/// Builds the Project Officer's unified operational queue. The builder deliberately
/// has no database dependency so the queue ordering and counting rules can be tested
/// independently of the workspace query.
/// </summary>
internal static class WorkspaceActionQueueBuilder
{
    private const int MaximumVisibleItems = 10;
    private const int ReturnedPriority = 0;
    private const int OverdueTaskPriority = 10;
    private const int OverdueTimelinePriority = 20;
    private const int CurrentStageTimelinePriority = 30;
    private const int ProjectUpdatePriority = 40;
    private const int HistoricalTimelinePriority = 50;
    private const int IdeaPriority = 60;
    private const int AotsPriority = 70;
    private const int DueSoonTaskPriority = 80;

    public static WorkspaceActionQueueBuildResult Build(
        IReadOnlyList<WorkspaceAttentionItemVm> returnedItems,
        IReadOnlyList<WorkspaceTaskVm> otherAssignedTasksDue,
        IReadOnlyList<WorkspaceAttentionItemVm> remarksDue,
        IReadOnlyList<WorkspaceIdeaVm> ideasNeedingUpdate,
        IReadOnlyList<WorkspaceAotsDocumentVm> aotsDocuments,
        int aotsUnreadTotalCount,
        IReadOnlyList<WorkspaceProjectMatrixRowVm> projectRows)
    {
        ArgumentNullException.ThrowIfNull(returnedItems);
        ArgumentNullException.ThrowIfNull(otherAssignedTasksDue);
        ArgumentNullException.ThrowIfNull(remarksDue);
        ArgumentNullException.ThrowIfNull(ideasNeedingUpdate);
        ArgumentNullException.ThrowIfNull(aotsDocuments);
        ArgumentNullException.ThrowIfNull(projectRows);

        var items = new List<WorkspaceActionQueueItemVm>();
        var projectIdsByName = BuildUniqueProjectNameIndex(projectRows);

        items.AddRange(returnedItems.Select(item => new WorkspaceActionQueueItemVm
        {
            ProjectId = ResolveProjectId(item.Title, projectIdsByName),
            Type = "Returned",
            BadgeText = item.BadgeText,
            Title = item.Title,
            Detail = item.Detail,
            Meta = "Returned item",
            PriorityReason = "Returned for correction",
            Severity = item.Severity,
            ActionText = item.ActionText,
            ActionUrl = item.ActionUrl,
            SortDateUtc = item.DueOrEventDateUtc,
            PriorityRank = ReturnedPriority
        }));

        items.AddRange(otherAssignedTasksDue.Select(task => new WorkspaceActionQueueItemVm
        {
            Type = "Task",
            BadgeText = task.IsOverdue ? "Overdue" : "Task",
            Title = task.Title,
            Detail = task.IsOverdue && task.DaysOverdue.HasValue
                ? $"Overdue by {task.DaysOverdue.Value} day{(task.DaysOverdue.Value == 1 ? string.Empty : "s")}"
                : task.DueDateUtc.HasValue ? $"Due {task.DueDateUtc.Value:dd MMM}" : "Assigned task",
            Meta = JoinMeta(task.Priority, task.Status),
            PriorityReason = task.IsOverdue ? "Overdue assigned task" : "Assigned task due soon",
            Severity = task.IsOverdue ? "Danger" : "Warning",
            ActionText = "Open",
            ActionUrl = task.OpenUrl,
            SortDateUtc = task.DueDateUtc,
            PriorityRank = task.IsOverdue ? OverdueTaskPriority : DueSoonTaskPriority
        }));

        items.AddRange(projectRows
            .Where(HasTimelineAction)
            .Select(BuildTimelineAction));

        items.AddRange(remarksDue.Select(item =>
        {
            var detail = NormalizeProjectOfficerUpdateDetail(item.Detail);
            return new WorkspaceActionQueueItemVm
            {
                ProjectId = ResolveProjectId(item.Title, projectIdsByName),
                Type = "Remark",
                BadgeText = "Remark",
                Title = item.Title,
                Detail = detail,
                Meta = string.Empty,
                PriorityReason = detail,
                Severity = item.Severity,
                ActionText = "Add Remark",
                ActionUrl = item.ActionUrl,
                SortDateUtc = item.DueOrEventDateUtc,
                PriorityRank = ProjectUpdatePriority
            };
        }));

        items.AddRange(ideasNeedingUpdate.Select(idea => new WorkspaceActionQueueItemVm
        {
            Type = "Idea",
            BadgeText = "Idea",
            Title = idea.Title,
            Detail = "Assigned idea needs an update",
            Meta = $"{idea.CommentCount} comments · {idea.DocumentCount} docs",
            PriorityReason = "Longest-inactive assigned idea",
            Severity = "Warning",
            ActionText = "Open",
            ActionUrl = idea.OpenUrl,
            SortDateUtc = idea.LastActivityAtUtc,
            PriorityRank = IdeaPriority
        }));

        items.AddRange(aotsDocuments.Select(document => new WorkspaceActionQueueItemVm
        {
            Type = "AOTS",
            BadgeText = "AOTS",
            Title = document.Subject,
            Detail = "Unread AOTS document",
            Meta = JoinMeta(document.Office, document.Category),
            PriorityReason = "Latest unread AOTS document",
            Severity = "Warning",
            ActionText = "Review",
            ActionUrl = document.OpenUrl,
            SortDateUtc = document.CreatedAtUtc,
            PriorityRank = AotsPriority
        }));

        var orderedItems = items
            .OrderBy(item => item.PriorityRank)
            .ThenBy(GetDateRank)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The AOTS preview query can be deliberately capped. Preserve the true unread
        // total in the headline even when only a subset of documents is materialised.
        var additionalUnreadAots = Math.Max(0, aotsUnreadTotalCount - aotsDocuments.Count);
        var totalCount = orderedItems.Count + additionalUnreadAots;

        var visibleItems = orderedItems.Take(MaximumVisibleItems).ToList();

        return new WorkspaceActionQueueBuildResult(
            visibleItems,
            BuildGroups(visibleItems),
            totalCount);
    }

    private static bool HasTimelineAction(WorkspaceProjectMatrixRowVm row)
        => row.HasOverdueCurrentStage || row.HasCurrentStageIssue || row.HasBackfill;

    private static WorkspaceActionQueueItemVm BuildTimelineAction(WorkspaceProjectMatrixRowVm row)
    {
        var isCurrentStageAction = row.HasOverdueCurrentStage || row.HasCurrentStageIssue;
        var detail = WorkspaceDisplayHelpers.TimelineStatusLabel(row);
        var meta = WorkspaceDisplayHelpers.TimelineStatusDetail(row);

        return new WorkspaceActionQueueItemVm
        {
            ProjectId = row.ProjectId,
            Type = "Timeline",
            BadgeText = row.HasOverdueCurrentStage ? "Overdue" : "Timeline",
            Title = row.ProjectName,
            Detail = detail,
            Meta = meta,
            PriorityReason = detail,
            Severity = row.HasOverdueCurrentStage ? "Danger" : "Warning",
            ActionText = WorkspaceDisplayHelpers.TimelineActionLabel(row),
            ActionUrl = row.TimelineUrl,
            SortDateUtc = row.CurrentStagePdc?.ToDateTime(TimeOnly.MinValue),
            PriorityRank = row.HasOverdueCurrentStage
                ? OverdueTimelinePriority
                : isCurrentStageAction
                    ? CurrentStageTimelinePriority
                    : HistoricalTimelinePriority
        };
    }

    private static IReadOnlyDictionary<string, int> BuildUniqueProjectNameIndex(
        IReadOnlyList<WorkspaceProjectMatrixRowVm> projectRows)
        => projectRows
            .Where(row => row.ProjectId > 0 && !string.IsNullOrWhiteSpace(row.ProjectName))
            .GroupBy(row => row.ProjectName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(row => row.ProjectId).Distinct().Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.First().ProjectId,
                StringComparer.OrdinalIgnoreCase);

    private static int? ResolveProjectId(
        string title,
        IReadOnlyDictionary<string, int> projectIdsByName)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return projectIdsByName.TryGetValue(title.Trim(), out var projectId)
            ? projectId
            : null;
    }

    private static IReadOnlyList<WorkspaceActionQueueGroupVm> BuildGroups(
        IReadOnlyList<WorkspaceActionQueueItemVm> visibleItems)
    {
        var orderedKeys = new List<string>();
        var itemsByKey = new Dictionary<string, List<WorkspaceActionQueueItemVm>>(StringComparer.Ordinal);

        for (var index = 0; index < visibleItems.Count; index++)
        {
            var item = visibleItems[index];
            var key = item.ProjectId is > 0
                ? $"project:{item.ProjectId.Value}"
                : $"action:{index}";

            if (!itemsByKey.TryGetValue(key, out var groupedItems))
            {
                groupedItems = new List<WorkspaceActionQueueItemVm>();
                itemsByKey[key] = groupedItems;
                orderedKeys.Add(key);
            }

            groupedItems.Add(item);
        }

        return orderedKeys
            .Select((key, index) =>
            {
                var actions = itemsByKey[key];
                var first = actions[0];

                return new WorkspaceActionQueueGroupVm
                {
                    Key = key,
                    ProjectId = first.ProjectId,
                    Title = first.Title,
                    PrimaryUrl = first.ActionUrl,
                    Severity = ResolveGroupSeverity(actions),
                    IsRecommended = index == 0,
                    Actions = actions
                };
            })
            .ToList();
    }

    private static string ResolveGroupSeverity(
        IReadOnlyList<WorkspaceActionQueueItemVm> actions)
    {
        if (actions.Any(action => action.Severity.Equals("Danger", StringComparison.OrdinalIgnoreCase)))
        {
            return "Danger";
        }

        if (actions.Any(action => action.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase)))
        {
            return "Warning";
        }

        return "Info";
    }

    private static long GetDateRank(WorkspaceActionQueueItemVm item)
    {
        var newestFirst = item.Type is "Returned" or "AOTS";

        if (!item.SortDateUtc.HasValue)
        {
            return newestFirst ? long.MaxValue : long.MinValue;
        }

        return newestFirst
            ? -item.SortDateUtc.Value.Ticks
            : item.SortDateUtc.Value.Ticks;
    }

    internal static string NormalizeProjectOfficerUpdateDetail(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return "Project Officer update required";
        }

        const string noRemarkPrefix = "No PO remark in last ";
        if (detail.StartsWith(noRemarkPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"No Project Officer update for {detail[noRemarkPrefix.Length..]}";
        }

        if (detail.Equals("No PO remark has been added yet", StringComparison.OrdinalIgnoreCase))
        {
            return "No Project Officer update has been recorded";
        }

        return detail.Replace("PO remark", "Project Officer update", StringComparison.OrdinalIgnoreCase);
    }

    private static string JoinMeta(params string?[] values)
        => string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
}
