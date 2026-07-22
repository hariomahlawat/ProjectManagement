using ProjectManagement.Infrastructure;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

internal sealed record WorkspaceActionQueueBuildResult(
    IReadOnlyList<WorkspaceActionQueueItemVm> Items,
    IReadOnlyList<WorkspaceActionQueueGroupVm> Groups,
    IReadOnlyList<WorkspaceActionQueueItemVm> AllItems,
    IReadOnlyList<WorkspaceActionQueueGroupVm> AllGroups,
    int TotalCount,
    WorkspaceActionQueueSummaryVm Summary);

/// <summary>
/// Builds the Project Officer's unified operational queue. The builder has no
/// database dependency so ordering, grouping and counting rules remain deterministic
/// and independently testable.
/// </summary>
internal static class WorkspaceActionQueueBuilder
{
    private const int MaximumVisibleRows = 10;
    private const int ReturnedPriority = 0;
    private const int OverdueTaskPriority = 10;
    private const int ConferenceDirectionPriority = 15;
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
        => Build(
            returnedItems,
            otherAssignedTasksDue,
            remarksDue,
            ideasNeedingUpdate,
            aotsDocuments,
            aotsUnreadTotalCount,
            projectRows,
            Array.Empty<WorkspaceConferenceDirectionActionVm>());

    public static WorkspaceActionQueueBuildResult Build(
        IReadOnlyList<WorkspaceAttentionItemVm> returnedItems,
        IReadOnlyList<WorkspaceTaskVm> otherAssignedTasksDue,
        IReadOnlyList<WorkspaceAttentionItemVm> remarksDue,
        IReadOnlyList<WorkspaceIdeaVm> ideasNeedingUpdate,
        IReadOnlyList<WorkspaceAotsDocumentVm> aotsDocuments,
        int aotsUnreadTotalCount,
        IReadOnlyList<WorkspaceProjectMatrixRowVm> projectRows,
        IReadOnlyList<WorkspaceConferenceDirectionActionVm> conferenceDirections)
    {
        ArgumentNullException.ThrowIfNull(returnedItems);
        ArgumentNullException.ThrowIfNull(otherAssignedTasksDue);
        ArgumentNullException.ThrowIfNull(remarksDue);
        ArgumentNullException.ThrowIfNull(ideasNeedingUpdate);
        ArgumentNullException.ThrowIfNull(aotsDocuments);
        ArgumentNullException.ThrowIfNull(projectRows);
        ArgumentNullException.ThrowIfNull(conferenceDirections);

        var items = new List<WorkspaceActionQueueItemVm>();
        var projectIdsByName = BuildUniqueProjectNameIndex(projectRows);

        items.AddRange(returnedItems.Select(item =>
        {
            var projectId = item.ProjectId ?? ResolveProjectId(item.Title, projectIdsByName);
            return new WorkspaceActionQueueItemVm
            {
                ProjectId = projectId,
                WorkItemKey = ResolveWorkItemKey(item.WorkItemKey, projectId, $"returned:{item.Type}:{item.ActionUrl}"),
                Type = "Returned",
                BadgeText = string.IsNullOrWhiteSpace(item.BadgeText) ? "Returned" : item.BadgeText,
                Title = item.Title,
                Detail = item.Detail,
                Meta = "Returned for correction",
                PriorityReason = "Returned for correction",
                Severity = item.Severity,
                ActionText = item.ActionText,
                ActionUrl = item.ActionUrl,
                SortDateUtc = item.DueOrEventDateUtc,
                PriorityRank = ReturnedPriority
            };
        }));

        items.AddRange(otherAssignedTasksDue.Select(task => new WorkspaceActionQueueItemVm
        {
            WorkItemKey = $"task:{task.TaskId}",
            Type = "Task",
            BadgeText = task.IsOverdue ? "Overdue" : "Task",
            Title = task.Title,
            Detail = task.IsOverdue && task.DaysOverdue.HasValue
                ? $"Overdue by {task.DaysOverdue.Value} day{(task.DaysOverdue.Value == 1 ? string.Empty : "s")}" 
                : $"Due {task.DueDate:dd MMM}",
            Meta = JoinMeta(task.Priority, task.Status),
            PriorityReason = task.IsOverdue ? "Overdue assigned task" : "Assigned task due soon",
            Severity = task.IsOverdue ? "Danger" : "Warning",
            ActionText = "Open task",
            ActionUrl = task.OpenUrl,
            SortDateUtc = task.DueDate.ToDateTime(TimeOnly.MinValue),
            PriorityRank = task.IsOverdue ? OverdueTaskPriority : DueSoonTaskPriority
        }));

        items.AddRange(conferenceDirections.Select(direction => new WorkspaceActionQueueItemVm
        {
            ProjectId = direction.ProjectId,
            WorkItemKey = ConferenceWorkItemKey(direction),
            Type = "Conference",
            BadgeText = "Direction",
            Title = direction.Title,
            Detail = string.IsNullOrWhiteSpace(direction.DirectionText)
                ? "Conference direction awaiting progress"
                : direction.DirectionText,
            Meta = $"Issued {IstClock.ToIst(direction.IssuedAtUtc):dd MMM yyyy} · Progress update required",
            PriorityReason = "Conference direction awaiting progress",
            Severity = "Warning",
            ActionText = "Add progress",
            ActionUrl = direction.ActionUrl,
            SortDateUtc = direction.IssuedAtUtc,
            PriorityRank = ConferenceDirectionPriority
        }));

        items.AddRange(projectRows
            .Where(HasTimelineAction)
            .Select(BuildTimelineAction));

        items.AddRange(remarksDue.Select(item =>
        {
            var projectId = item.ProjectId ?? ResolveProjectId(item.Title, projectIdsByName);
            var detail = NormalizeProjectOfficerUpdateDetail(item.Detail);
            return new WorkspaceActionQueueItemVm
            {
                ProjectId = projectId,
                WorkItemKey = ResolveWorkItemKey(item.WorkItemKey, projectId, $"remark:{item.ActionUrl}"),
                Type = "Remark",
                BadgeText = "Remark",
                Title = item.Title,
                Detail = detail,
                Meta = string.Empty,
                PriorityReason = detail,
                Severity = item.Severity,
                ActionText = "Add update",
                ActionUrl = item.ActionUrl,
                SortDateUtc = item.DueOrEventDateUtc,
                PriorityRank = ProjectUpdatePriority
            };
        }));

        items.AddRange(ideasNeedingUpdate.Select(idea => new WorkspaceActionQueueItemVm
        {
            WorkItemKey = $"idea:{idea.IdeaId}",
            Type = "Idea",
            BadgeText = "Idea",
            Title = idea.Title,
            Detail = "Assigned idea needs an update",
            Meta = $"{idea.CommentCount} comments · {idea.DocumentCount} docs",
            PriorityReason = "Assigned idea update required",
            Severity = "Warning",
            ActionText = "Open idea",
            ActionUrl = idea.OpenUrl,
            SortDateUtc = idea.LastActivityAtUtc,
            PriorityRank = IdeaPriority
        }));

        items.AddRange(aotsDocuments.Select(document => new WorkspaceActionQueueItemVm
        {
            WorkItemKey = $"document:{document.DocumentId:N}",
            Type = "AOTS",
            BadgeText = "AOTS",
            Title = document.Subject,
            Detail = "Unread AOTS document",
            Meta = JoinMeta(document.Office, document.Category),
            PriorityReason = "Unread AOTS document",
            Severity = "Warning",
            ActionText = "Review",
            ActionUrl = document.OpenUrl,
            SortDateUtc = document.CreatedAtUtc,
            PriorityRank = AotsPriority
        }));

        var additionalUnreadAots = Math.Max(0, aotsUnreadTotalCount - aotsDocuments.Count);
        if (additionalUnreadAots > 0)
        {
            items.Add(new WorkspaceActionQueueItemVm
            {
                WorkItemKey = "aots:inbox",
                RepresentedActionCount = additionalUnreadAots,
                Type = "AOTS",
                BadgeText = "AOTS",
                Title = "Additional unread AOTS documents",
                Detail = $"{additionalUnreadAots} more unread AOTS document{(additionalUnreadAots == 1 ? string.Empty : "s")}",
                Meta = "Open the AOTS inbox to review the remaining documents",
                PriorityReason = "Additional unread AOTS documents",
                Severity = "Warning",
                ActionText = "Open inbox",
                ActionUrl = WorkspaceRouteHelper.AotsInbox(),
                PriorityRank = AotsPriority + 1
            });
        }

        var orderedItems = items
            .OrderBy(item => item.PriorityRank)
            .ThenBy(GetDateRank)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalCount = orderedItems.Sum(item => Math.Max(1, item.RepresentedActionCount));
        var visibleItems = orderedItems.Take(MaximumVisibleRows).ToList();
        var summary = BuildSummary(orderedItems, totalCount);

        return new WorkspaceActionQueueBuildResult(
            visibleItems,
            BuildGroups(visibleItems),
            orderedItems,
            BuildGroups(orderedItems),
            totalCount,
            summary);
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
            WorkItemKey = $"project:{row.ProjectId}",
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

    private static WorkspaceActionQueueSummaryVm BuildSummary(
        IReadOnlyList<WorkspaceActionQueueItemVm> orderedItems,
        int totalCount)
    {
        var projectCount = orderedItems
            .Where(item => item.ProjectId is > 0)
            .Select(item => item.ProjectId!.Value)
            .Distinct()
            .Count();
        var ideaCount = CountDistinctWorkItems(orderedItems, "idea:");
        var taskCount = CountDistinctWorkItems(orderedItems, "task:");

        return new WorkspaceActionQueueSummaryVm
        {
            TotalCount = totalCount,
            ProjectCount = projectCount,
            IdeaCount = ideaCount,
            TaskCount = taskCount,
            ReturnedCount = CountRepresented(orderedItems, "Returned"),
            OverdueTaskCount = orderedItems
                .Where(item => item.Type == "Task" && item.PriorityRank == OverdueTaskPriority)
                .Sum(item => item.RepresentedActionCount),
            ConferenceDirectionCount = CountRepresented(orderedItems, "Conference"),
            TimelineCount = CountRepresented(orderedItems, "Timeline"),
            ProjectUpdateCount = CountRepresented(orderedItems, "Remark"),
            IdeaUpdateCount = CountRepresented(orderedItems, "Idea"),
            AotsCount = CountRepresented(orderedItems, "AOTS"),
            DueSoonTaskCount = orderedItems
                .Where(item => item.Type == "Task" && item.PriorityRank == DueSoonTaskPriority)
                .Sum(item => item.RepresentedActionCount)
        };
    }

    private static int CountRepresented(IEnumerable<WorkspaceActionQueueItemVm> items, string type)
        => items
            .Where(item => item.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .Sum(item => Math.Max(1, item.RepresentedActionCount));

    private static int CountDistinctWorkItems(
        IEnumerable<WorkspaceActionQueueItemVm> items,
        string prefix)
        => items
            .Select(item => item.WorkItemKey)
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static string ConferenceWorkItemKey(WorkspaceConferenceDirectionActionVm direction) => direction.Kind switch
    {
        ConferenceItemKind.Project => $"project:{direction.ProjectId ?? direction.ItemId}",
        ConferenceItemKind.ProjectIdea => $"idea:{direction.ItemId}",
        ConferenceItemKind.ActionTask => $"task:{direction.ItemId}",
        _ => $"conference:{direction.ItemId}"
    };

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

    private static string ResolveWorkItemKey(string explicitKey, int? projectId, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(explicitKey))
        {
            return explicitKey;
        }

        return projectId is > 0 ? $"project:{projectId.Value}" : fallback;
    }

    private static IReadOnlyList<WorkspaceActionQueueGroupVm> BuildGroups(
        IReadOnlyList<WorkspaceActionQueueItemVm> visibleItems)
    {
        var orderedKeys = new List<string>();
        var itemsByKey = new Dictionary<string, List<WorkspaceActionQueueItemVm>>(StringComparer.Ordinal);

        for (var index = 0; index < visibleItems.Count; index++)
        {
            var item = visibleItems[index];
            var key = !string.IsNullOrWhiteSpace(item.WorkItemKey)
                ? item.WorkItemKey
                : item.ProjectId is > 0
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
