using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Execution;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public sealed class ProjectOfficerWorkspaceService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ProjectRecordHealthService _health;
    private readonly WorkspaceNudgeService _nudges;
    private readonly ActionTaskMyWorkQueueBuilder _myWorkQueueBuilder;
    private readonly IActionTrackerClock _clock;
    private readonly IAotsUnreadService _aotsUnreadService;

    private sealed record WorkspaceActionQueueBuildResult(
        IReadOnlyList<WorkspaceActionQueueItemVm> Items,
        int TotalCount);

    public ProjectOfficerWorkspaceService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ProjectRecordHealthService health,
        WorkspaceNudgeService nudges,
        ActionTaskMyWorkQueueBuilder myWorkQueueBuilder,
        IActionTrackerClock clock,
        IAotsUnreadService aotsUnreadService)
    {
        _db = db;
        _users = users;
        _health = health;
        _nudges = nudges;
        _myWorkQueueBuilder = myWorkQueueBuilder;
        _clock = clock;
        _aotsUnreadService = aotsUnreadService;
    }

    // SECTION: Workspace composition
    public async Task<ProjectOfficerWorkspaceVm> GetProjectOfficerWorkspaceAsync(string userId, ClaimsPrincipal principal, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(_clock.IstToday);
        var istNow = IstClock.ToIst(_clock.UtcNow);
        var istMonthStart = new DateTime(
            istNow.Year,
            istNow.Month,
            1,
            0,
            0,
            0,
            DateTimeKind.Unspecified);
        var monthStartUtc = istMonthStart
            .AddHours(-5)
            .AddMinutes(-30);
        var user = await _users.FindByIdAsync(userId);
        var myProjectsUrl = WorkspaceRouteHelper.MyProjects(userId);

        var projects = await LoadAssignedProjectsAsync(userId, ct);
        var tasks = await LoadOtherAssignedTasksAsync(userId, today, ct);
        var ideaVms = await LoadProjectIdeasAsync(userId, today, ct);
        var reminders = await LoadPersonalRemindersAsync(userId, ct);
        var health = await _health.CalculateForProjectsAsync(projects, userId, ct);

        var remarksDue = _nudges.BuildRemarksDue(projects, userId, today).ToList();
        var returnedItems = await BuildReturnedItemsAsync(userId, ct);
        var officialTasksDue = tasks
            .Where(t => t.IsOverdue || IsDueSoon(t.DueDateUtc, today))
            .OrderByDescending(t => t.IsOverdue)
            .ThenBy(t => t.DueDateUtc ?? DateTime.MaxValue)
            .Take(5)
            .ToList();
        var ideasNeedingUpdate = ideaVms
            .Where(i => i.NeedsUpdate)
            .OrderByDescending(i => i.LastActivityAtUtc)
            .Take(5)
            .ToList();
        var aotsUnreadCount = await _aotsUnreadService.GetUnreadCountAsync(userId, ct);
        var aotsDocuments = await LoadUnreadAotsDocumentsAsync(userId, ct);
        var timelineAlerts = _nudges.BuildTimelineAlerts(projects, today).ToList();
        var actionQueueResult = BuildActionQueue(
            returnedItems,
            officialTasksDue,
            remarksDue,
            ideasNeedingUpdate,
            aotsDocuments,
            aotsUnreadCount);
        var actionQueue = actionQueueResult.Items;
        var dailyActionCount = actionQueueResult.TotalCount;

        var pending = returnedItems
            .Concat(remarksDue)
            .Concat(officialTasksDue.Select(ToAttentionItem))
            .Concat(ideasNeedingUpdate.Select(ToAttentionItem))
            .Concat(aotsDocuments.Select(ToAttentionItem))
            .Concat(timelineAlerts)
            .GroupBy(i => $"{i.Type}:{i.Title}")
            .Select(g => g
                .OrderBy(i => WorkspaceSeverityRank(i.Severity))
                .ThenBy(i => WorkspacePendingPriority(i.Detail))
                .ThenByDescending(i => i.DueOrEventDateUtc)
                .First())
            .OrderBy(i => WorkspaceSeverityRank(i.Severity))
            .ThenBy(i => WorkspacePendingPriority(i.Detail))
            .ThenByDescending(i => i.DueOrEventDateUtc)
            .ToList();

        var taskRows = await _db.ActionTasks
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.AssignedToUserId == userId && t.Status != ActionTaskStatuses.Closed && t.Status != ActionTaskStatuses.Backlog)
            .OrderBy(t => t.DueDate)
            .ToListAsync(ct);
        var myWorkQueue = _myWorkQueueBuilder.Build(taskRows, activeSprint: null);
        var waitingOnOthers = await BuildWaitingOnOthersAsync(userId, myWorkQueue.SubmittedAwaitingClosureTasks, ct);
        var matrix = projects
            .Select(p => BuildMatrixRow(p, health[p.Id], userId, today))
            .OrderBy(ProjectAttentionRank)
            .ThenByDescending(row => row.RecordGapCount)
            .ThenByDescending(row => row.DaysInCurrentStage ?? 0)
            .ThenBy(row => row.ProjectName)
            .Take(8)
            .ToList();
        var engagement = await BuildEngagementAsync(userId, user, monthStartUtc, ct);
        var avgHealth = health.Count == 0 ? 100 : (int)Math.Round(health.Values.Average(h => h.HealthPercent));
        var improveProjectsResult = BuildImproveProjects(health.Values, maxProjects: 3);

        var vm = new ProjectOfficerWorkspaceVm
        {
            UserDisplayName = string.IsNullOrWhiteSpace(user?.FullName)
                ? principal.Identity?.Name ?? "Project Officer"
                : user.FullName,
            PortfolioHealthPercent = avgHealth,
            PortfolioHealthLabel = avgHealth >= 80 ? "Good" : avgHealth >= 60 ? "Attention" : "Needs Work",
            RecordHealthSummaryLabel = avgHealth >= 80 ? "Good" : avgHealth >= 60 ? "Attention" : "Needs Work",
            AssignedProjectCount = projects.Count,
            PendingWithMeCount = pending.Count,
            DailyActionCount = dailyActionCount,
            OverdueTaskCount = tasks.Count(t => t.IsOverdue),
            RemarksDueCount = remarksDue.Count,
            OfficialTaskCount = officialTasksDue.Count,
            IdeasNeedingUpdateCount = ideaVms.Count(i => i.NeedsUpdate),
            AotsUnreadCount = aotsUnreadCount,
            AotsUrl = WorkspaceRouteHelper.AotsInbox(),
            RecordGapCount = health.Values.Sum(h => h.GapDetails.Count),
            AssignedIdeaCount = ideaVms.Count,
            Engagement = engagement,
            CommandChips = BuildCommandChips(
                remarksDue.Count,
                aotsUnreadCount,
                officialTasksDue.Count,
                ideasNeedingUpdate.Count),
            DataCompletenessInsight = BuildDataCompletenessInsight(projects.Count, health),
            PendingWithMe = pending.Take(5).ToList(),
            ActionQueue = actionQueue,
            ActionQueueTotalCount = actionQueueResult.TotalCount,
            RemarksDue = remarksDue.Take(5).ToList(),
            OfficialTasksDue = officialTasksDue,
            IdeasNeedingUpdate = ideasNeedingUpdate,
            AotsDocuments = aotsDocuments,
            ReturnedItems = returnedItems.Take(5).ToList(),
            TimelineAlerts = timelineAlerts.Take(5).ToList(),
            WaitingOnOthers = waitingOnOthers.Take(5).ToList(),
            ProjectMatrix = matrix,
            OfficialTasks = tasks.Take(5).ToList(),
            Ideas = ideaVms
                .OrderByDescending(i => i.NeedsUpdate)
                .ThenByDescending(i => i.LastActivityAtUtc)
                .Take(4)
                .ToList(),
            RecordHealth = health.Values.OrderBy(h => h.HealthPercent).Take(5).ToList(),
            ImproveScoreItems = BuildImproveScoreItems(health.Values, maxItems: 4),
            ImproveProjects = improveProjectsResult.Items,
            ImproveProjectsTotalCount = improveProjectsResult.TotalCount,
            NextBestAction = BuildNextBestAction(actionQueue, timelineAlerts),
            PersonalReminders = reminders,
            QuickActions = BuildQuickActions(userId),
            MyProjectsUrl = myProjectsUrl
        };

        vm.RailItems = BuildRailItems(vm);
        return vm;
    }

    // SECTION: Assigned projects include current-stage context for the workspace matrix.
    private async Task<IReadOnlyList<Project>> LoadAssignedProjectsAsync(string userId, CancellationToken ct)
    {
        return await _db.Projects
            .AsNoTracking()
            .Include(p => p.ProjectStages)
            .Include(p => p.Remarks)
            .Include(p => p.Documents)
            .Where(p =>
                p.LeadPoUserId == userId &&
                !p.IsDeleted &&
                p.LifecycleStatus != ProjectLifecycleStatus.Completed)
            .OrderBy(p => p.Name)
            .Take(50)
            .ToListAsync(ct);
    }

    // SECTION: Other assigned tasks are loaded in full before previews are trimmed.
    private async Task<IReadOnlyList<WorkspaceTaskVm>> LoadOtherAssignedTasksAsync(string userId, DateOnly today, CancellationToken ct)
    {
        var taskRows = await _db.ActionTasks
            .AsNoTracking()
            .Where(t =>
                !t.IsDeleted &&
                t.AssignedToUserId == userId &&
                t.Status != ActionTaskStatuses.Closed &&
                t.Status != ActionTaskStatuses.Backlog)
            .OrderBy(t => t.DueDate)
            .ToListAsync(ct);
        var myWorkQueue = _myWorkQueueBuilder.Build(taskRows, activeSprint: null);
        var overdueTaskIds = myWorkQueue.OverdueTasks.Select(t => t.Id).ToHashSet();

        return myWorkQueue.ActionRequiredTasks
            .Concat(myWorkQueue.CurrentWorkTasks)
            .Concat(myWorkQueue.SubmittedAwaitingClosureTasks)
            .Concat(myWorkQueue.AllMyTasks)
            .GroupBy(t => t.Id)
            .Select(g => BuildWorkspaceTaskVm(g.First(), overdueTaskIds, today))
            .ToList();
    }

    // SECTION: Project ideas are loaded before preview trimming so stale and assigned counts remain accurate.
    private async Task<IReadOnlyList<WorkspaceIdeaVm>> LoadProjectIdeasAsync(string userId, DateOnly today, CancellationToken ct)
    {
        var ideas = await _db.ProjectIdeas
            .AsNoTracking()
            .Include(i => i.Comments)
            .Include(i => i.Notes)
            .Include(i => i.Documents)
            .Where(i =>
                !i.IsDeleted &&
                (i.AssignedProjectOfficerUserId == userId || i.CreatedByUserId == userId) &&
                i.Status != ProjectIdeaStatuses.Archived)
            .OrderByDescending(i => i.UpdatedAt)
            .ToListAsync(ct);

        return ideas.Select(i => BuildWorkspaceIdeaVm(i, today)).ToList();
    }

    // SECTION: Personal reminders stay separate from the operational action queue.
    private async Task<IReadOnlyList<WorkspaceReminderVm>> LoadPersonalRemindersAsync(string userId, CancellationToken ct)
    {
        var endTodayLocal = DateTime.SpecifyKind(_clock.IstToday.AddDays(1), DateTimeKind.Unspecified);
        var endTodayUtc = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(endTodayLocal, IstClock.TimeZone),
            TimeSpan.Zero);

        return await _db.NotebookItems
            .AsNoTracking()
            .Where(item =>
                item.OwnerId == userId &&
                item.DeletedAtUtc == null &&
                item.Status == NotebookItemStatus.Active &&
                item.ReminderAtUtc != null &&
                item.ReminderAtUtc < endTodayUtc)
            .OrderByDescending(item => item.IsPinned)
            .ThenBy(item => item.ReminderAtUtc)
            .Take(5)
            .Select(item => new WorkspaceReminderVm
            {
                ReminderId = item.Id,
                Title = item.Title,
                Priority = item.Priority.ToString(),
                DueAtUtc = item.ReminderAtUtc,
                IsPinned = item.IsPinned,
                OpenUrl = WorkspaceRouteHelper.PersonalReminder(item.Id)
            })
            .ToListAsync(ct);
    }

    // SECTION: Task rows are converted once so counts and previews share one model.
    private static WorkspaceTaskVm BuildWorkspaceTaskVm(ActionTaskItem row, HashSet<int> overdueTaskIds, DateOnly today)
    {
        var dueDateUtc = DateTime.SpecifyKind(row.DueDate.Date, DateTimeKind.Utc);
        var daysOverdue = overdueTaskIds.Contains(row.Id) ? today.DayNumber - DateOnly.FromDateTime(row.DueDate.Date).DayNumber : (int?)null;

        return new WorkspaceTaskVm
        {
            TaskId = row.Id,
            Title = row.Title,
            ContextLabel = "Other assigned task",
            Priority = row.Priority,
            Status = row.Status,
            DueDateUtc = dueDateUtc,
            IsOverdue = daysOverdue.HasValue,
            DaysOverdue = daysOverdue,
            OpenUrl = WorkspaceRouteHelper.ActionTask(row.Id)
        };
    }

    // SECTION: Idea activity is centralized so preview and stale counts use identical rules.
    private static WorkspaceIdeaVm BuildWorkspaceIdeaVm(ProjectIdea idea, DateOnly today)
    {
        var last = new[] { idea.UpdatedAt }
            .Concat(idea.Comments.Where(c => !c.IsDeleted).Select(c => c.CreatedAt))
            .Concat(idea.Notes.Where(n => !n.IsDeleted).Select(n => n.UpdatedAt))
            .Concat(idea.Documents.Where(d => !d.IsDeleted).Select(d => d.UploadedAt))
            .DefaultIfEmpty(idea.UpdatedAt)
            .Max();

        return new WorkspaceIdeaVm
        {
            IdeaId = idea.Id,
            Title = idea.Title,
            Status = ProjectIdeaStatuses.ToDisplay(idea.Status),
            LastActivityAtUtc = last,
            NeedsUpdate = (today.DayNumber - WorkspaceNudgeService.ToIstDate(last).DayNumber) > 15 &&
                (idea.Status == ProjectIdeaStatuses.Active || idea.Status == ProjectIdeaStatuses.OnHold),
            CommentCount = idea.Comments.Count(c => !c.IsDeleted),
            DocumentCount = idea.Documents.Count(d => !d.IsDeleted),
            OpenUrl = WorkspaceRouteHelper.ProjectIdea(idea.Id)
        };
    }

    // SECTION: Due-soon detection uses the workspace IST operating date.
    private static bool IsDueSoon(DateTime? dueDateUtc, DateOnly today)
    {
        if (!dueDateUtc.HasValue)
        {
            return false;
        }

        var due = DateOnly.FromDateTime(dueDateUtc.Value);
        var days = due.DayNumber - today.DayNumber;

        return days >= 0 && days <= 3;
    }

    // SECTION: Unified action queue prioritizes operational work without four competing inbox columns.
    private static WorkspaceActionQueueBuildResult BuildActionQueue(
        IReadOnlyList<WorkspaceAttentionItemVm> returnedItems,
        IReadOnlyList<WorkspaceTaskVm> otherAssignedTasksDue,
        IReadOnlyList<WorkspaceAttentionItemVm> remarksDue,
        IReadOnlyList<WorkspaceIdeaVm> ideasNeedingUpdate,
        IReadOnlyList<WorkspaceAotsDocumentVm> aotsDocuments,
        int aotsUnreadTotalCount)
    {
        var items = new List<WorkspaceActionQueueItemVm>();

        items.AddRange(returnedItems.Select(item => new WorkspaceActionQueueItemVm
        {
            Type = "Returned",
            BadgeText = item.BadgeText,
            Title = item.Title,
            Detail = item.Detail,
            Meta = "Returned item",
            Severity = item.Severity,
            ActionText = item.ActionText,
            ActionUrl = item.ActionUrl,
            SortDateUtc = item.DueOrEventDateUtc
        }));

        items.AddRange(otherAssignedTasksDue.Select(task => new WorkspaceActionQueueItemVm
        {
            Type = "Task",
            BadgeText = task.IsOverdue ? "Overdue" : "Task",
            Title = task.Title,
            Detail = task.IsOverdue && task.DaysOverdue.HasValue
                ? $"Overdue by {task.DaysOverdue.Value} days"
                : task.DueDateUtc.HasValue ? $"Due {task.DueDateUtc.Value:dd MMM}" : "Assigned task",
            Meta = $"{task.Priority} · {task.Status}",
            Severity = task.IsOverdue ? "Danger" : "Warning",
            ActionText = "Open",
            ActionUrl = task.OpenUrl,
            SortDateUtc = task.DueDateUtc
        }));

        items.AddRange(remarksDue.Select(item => new WorkspaceActionQueueItemVm
        {
            Type = "Remark",
            BadgeText = "Remark",
            Title = item.Title,
            Detail = item.Detail,
            Meta = "Project update",
            Severity = item.Severity,
            ActionText = "Add Remark",
            ActionUrl = item.ActionUrl,
            SortDateUtc = item.DueOrEventDateUtc
        }));

        items.AddRange(ideasNeedingUpdate.Select(idea => new WorkspaceActionQueueItemVm
        {
            Type = "Idea",
            BadgeText = "Idea",
            Title = idea.Title,
            Detail = "Project idea needs update",
            Meta = $"{idea.CommentCount} comments · {idea.DocumentCount} docs",
            Severity = "Warning",
            ActionText = "Open",
            ActionUrl = idea.OpenUrl,
            SortDateUtc = idea.LastActivityAtUtc
        }));

        items.AddRange(aotsDocuments.Select(document => new WorkspaceActionQueueItemVm
        {
            Type = "AOTS",
            BadgeText = "AOTS",
            Title = document.Subject,
            Detail = "Unread AOTS document",
            Meta = $"{document.Office} · {document.Category}",
            Severity = "Warning",
            ActionText = "Review",
            ActionUrl = document.OpenUrl,
            SortDateUtc = document.CreatedAtUtc
        }));

        var orderedItems = items
            .OrderBy(GetActionQueuePriority)
            .ThenByDescending(i => i.SortDateUtc)
            .ToList();

        var totalCount =
            returnedItems.Count
            + otherAssignedTasksDue.Count
            + remarksDue.Count
            + ideasNeedingUpdate.Count
            + aotsUnreadTotalCount;

        return new WorkspaceActionQueueBuildResult(
            orderedItems.Take(8).ToList(),
            totalCount);
    }

    // SECTION: Next best action mirrors the daily queue, falling back to timeline follow-up only when daily actions are clear.
    private static WorkspaceAttentionItemVm? BuildNextBestAction(
        IReadOnlyList<WorkspaceActionQueueItemVm> actionQueue,
        IReadOnlyList<WorkspaceAttentionItemVm> timelineAlerts)
    {
        var first = actionQueue.FirstOrDefault();

        if (first is not null)
        {
            return new WorkspaceAttentionItemVm
            {
                Type = first.Type,
                Title = first.Title,
                Detail = first.Detail,
                Severity = first.Severity,
                BadgeText = first.BadgeText,
                ActionText = first.ActionText,
                ActionUrl = first.ActionUrl,
                DueOrEventDateUtc = first.SortDateUtc
            };
        }

        return timelineAlerts.FirstOrDefault();
    }

    // SECTION: Queue priority keeps returned corrections and overdue work first.
    private static int GetActionQueuePriority(WorkspaceActionQueueItemVm item)
    {
        return item.Type switch
        {
            "Returned" => 0,
            "Task" when item.Severity == "Danger" => 1,
            "Remark" => 2,
            "Idea" => 3,
            "AOTS" => 4,
            "Task" => 5,
            _ => 9
        };
    }

    private static WorkspaceAttentionItemVm ToAttentionItem(WorkspaceTaskVm task) => new()
    {
        Type = "Task",
        Title = task.Title,
        Detail = task.IsOverdue
            ? task.DaysOverdue.HasValue ? $"Assigned task overdue by {task.DaysOverdue.Value} days" : "Assigned task overdue"
            : "Assigned task due soon",
        Severity = task.IsOverdue ? "Danger" : "Warning",
        BadgeText = "Task",
        ActionText = "Open Task",
        ActionUrl = task.OpenUrl,
        DueOrEventDateUtc = task.DueDateUtc
    };

    private static WorkspaceAttentionItemVm ToAttentionItem(WorkspaceAotsDocumentVm document) => new()
    {
        Type = "AOTS",
        Title = document.Subject,
        Detail = "Unread AOTS document",
        Severity = "Warning",
        BadgeText = "AOTS",
        ActionText = "Review",
        ActionUrl = document.OpenUrl,
        DueOrEventDateUtc = document.CreatedAtUtc
    };

    private static WorkspaceAttentionItemVm ToAttentionItem(WorkspaceIdeaVm idea) => new()
    {
        Type = "Idea",
        Title = idea.Title,
        Detail = "Project idea needs update",
        Severity = "Warning",
        BadgeText = "Idea",
        ActionText = "Open Idea",
        ActionUrl = idea.OpenUrl,
        DueOrEventDateUtc = idea.LastActivityAtUtc
    };

    // SECTION: AOTS documents reuse the repository unread-view rule for daily review.
    private async Task<IReadOnlyList<WorkspaceAotsDocumentVm>> LoadUnreadAotsDocumentsAsync(
        string userId,
        CancellationToken ct)
    {
        return await _db.Documents
            .AsNoTracking()
            .Where(document =>
                document.IsAots &&
                !document.IsDeleted &&
                !document.IsExternal &&
                document.IsActive &&
                !_db.DocRepoAotsViews.Any(view =>
                    view.DocumentId == document.Id &&
                    view.UserId == userId))
            .OrderByDescending(document => document.CreatedAtUtc)
            .Take(5)
            .Select(document => new WorkspaceAotsDocumentVm
            {
                DocumentId = document.Id,
                Subject = document.Subject,
                Category = document.DocumentCategory.Name,
                Office = document.OfficeCategory.Name,
                CreatedAtUtc = document.CreatedAtUtc,
                OpenUrl = WorkspaceRouteHelper.AotsReader(document.Id)
            })
            .ToListAsync(ct);
    }


    // SECTION: Waiting-on-others summarizes submissions that are no longer actionable by the PO.
    private async Task<IReadOnlyList<WorkspaceAttentionItemVm>> BuildWaitingOnOthersAsync(string userId, IReadOnlyList<ActionTaskItem> submittedAwaitingClosureTasks, CancellationToken ct)
    {
        var items = new List<WorkspaceAttentionItemVm>();
        items.AddRange(submittedAwaitingClosureTasks.Select(t => new WorkspaceAttentionItemVm { Type = "Task", Title = t.Title, Detail = "Submitted and awaiting closure", Severity = "Info", BadgeText = "Task", ActionText = "View", ActionUrl = WorkspaceRouteHelper.ActionTask(t.Id), DueOrEventDateUtc = DateTime.SpecifyKind(t.SubmittedOn?.Date ?? t.DueDate.Date, DateTimeKind.Utc) }));

        var pendingPlans = await _db.PlanVersions.AsNoTracking().Include(p => p.Project).Where(p => p.SubmittedByUserId == userId && p.Status == PlanVersionStatus.PendingApproval).OrderByDescending(p => p.SubmittedOn).Take(5).ToListAsync(ct);
        items.AddRange(pendingPlans.Select(p => new WorkspaceAttentionItemVm { Type = "Timeline", Title = p.Project?.Name ?? "Timeline plan", Detail = "Timeline plan submitted, pending approval", Severity = "Info", BadgeText = "Plan", ActionText = "View status", ActionUrl = WorkspaceRouteHelper.ProjectTimeline(p.ProjectId), DueOrEventDateUtc = p.SubmittedOn?.UtcDateTime }));

        var pendingStages = await _db.StageChangeRequests.AsNoTracking().Where(r => r.RequestedByUserId == userId && r.DecisionStatus == "Pending").OrderByDescending(r => r.RequestedOn).Take(5).ToListAsync(ct);
        items.AddRange(pendingStages.Select(r => new WorkspaceAttentionItemVm { Type = "Stage", Title = r.StageCode, Detail = "Stage change request pending approval", Severity = "Info", BadgeText = "Stage", ActionText = "View", ActionUrl = WorkspaceRouteHelper.ProjectOverview(r.ProjectId), DueOrEventDateUtc = r.RequestedOn.UtcDateTime }));

        var pendingMeta = await _db.ProjectMetaChangeRequests.AsNoTracking().Include(r => r.Project).Where(r => r.RequestedByUserId == userId && r.DecisionStatus == "Pending").OrderByDescending(r => r.RequestedOnUtc).Take(5).ToListAsync(ct);
        items.AddRange(pendingMeta.Select(r => new WorkspaceAttentionItemVm { Type = "Metadata", Title = r.Project?.Name ?? "Metadata change", Detail = "Metadata change request pending approval", Severity = "Info", BadgeText = "Meta", ActionText = "View", ActionUrl = WorkspaceRouteHelper.ProjectOverview(r.ProjectId), DueOrEventDateUtc = r.RequestedOnUtc.UtcDateTime }));

        var pendingDocuments = await _db.ProjectDocumentRequests.AsNoTracking().Where(r => r.RequestedByUserId == userId && r.Status == ProjectDocumentRequestStatus.Submitted).OrderByDescending(r => r.RequestedAtUtc).Take(5).ToListAsync(ct);
        items.AddRange(pendingDocuments.Select(r => new WorkspaceAttentionItemVm { Type = "Document", Title = r.Title, Detail = "Document request pending moderation", Severity = "Info", BadgeText = "Doc", ActionText = "View", ActionUrl = WorkspaceRouteHelper.ProjectOverview(r.ProjectId), DueOrEventDateUtc = r.RequestedAtUtc.UtcDateTime }));
        return items.OrderByDescending(i => i.DueOrEventDateUtc).Take(12).ToList();
    }

    // SECTION: Returned items are surfaced as actionable corrections for the PO.
    private async Task<IReadOnlyList<WorkspaceAttentionItemVm>> BuildReturnedItemsAsync(string userId, CancellationToken ct)
    {
        var items = new List<WorkspaceAttentionItemVm>();
        var returnedCutoffUtc = DateTimeOffset.UtcNow.AddDays(-30);
        var rejectedPlans = await _db.PlanVersions.AsNoTracking().Include(p => p.Project).Where(p => (p.CreatedByUserId == userId || p.SubmittedByUserId == userId) && p.RejectedOn.HasValue && p.RejectedOn >= returnedCutoffUtc).OrderByDescending(p => p.RejectedOn).Take(10).ToListAsync(ct);
        var rejectedPlanProjectIds = rejectedPlans.Select(p => p.ProjectId).ToArray();
        var newerPlanProjects = await _db.PlanVersions.AsNoTracking().Where(p => rejectedPlanProjectIds.Contains(p.ProjectId) && (p.Status == PlanVersionStatus.PendingApproval || p.Status == PlanVersionStatus.Approved)).Select(p => new { p.ProjectId, EventAt = p.SubmittedOn ?? p.ApprovedOn ?? p.CreatedOn }).ToListAsync(ct);
        rejectedPlans = rejectedPlans.Where(p => !newerPlanProjects.Any(n => n.ProjectId == p.ProjectId && p.RejectedOn.HasValue && n.EventAt > p.RejectedOn.Value)).Take(5).ToList();
        items.AddRange(rejectedPlans.Select(p => new WorkspaceAttentionItemVm { Type = "Timeline", Title = p.Project?.Name ?? "Timeline plan", Detail = "Timeline plan returned for correction", Severity = "Danger", BadgeText = "Returned", ActionText = "Correct", ActionUrl = WorkspaceRouteHelper.ProjectTimeline(p.ProjectId), DueOrEventDateUtc = p.RejectedOn?.UtcDateTime }));

        var rejectedStages = await _db.StageChangeRequests.AsNoTracking().Where(r => r.RequestedByUserId == userId && r.DecisionStatus == "Rejected" && r.DecidedOn.HasValue && r.DecidedOn.Value >= returnedCutoffUtc).OrderByDescending(r => r.DecidedOn).Take(10).ToListAsync(ct);
        var rejectedStageProjectIds = rejectedStages.Select(r => r.ProjectId).ToArray();
        var newerStageProjects = await _db.StageChangeRequests.AsNoTracking().Where(r => rejectedStageProjectIds.Contains(r.ProjectId) && (r.DecisionStatus == "Pending" || r.DecisionStatus == "Approved")).Select(r => new { r.ProjectId, EventAt = r.DecidedOn ?? r.RequestedOn }).ToListAsync(ct);
        rejectedStages = rejectedStages.Where(r => !newerStageProjects.Any(n => n.ProjectId == r.ProjectId && r.DecidedOn.HasValue && n.EventAt > r.DecidedOn.Value)).Take(5).ToList();
        items.AddRange(rejectedStages.Select(r => new WorkspaceAttentionItemVm { Type = "Stage", Title = r.StageCode, Detail = "Stage change request rejected", Severity = "Danger", BadgeText = "Returned", ActionText = "Correct", ActionUrl = WorkspaceRouteHelper.ProjectTimeline(r.ProjectId), DueOrEventDateUtc = r.DecidedOn?.UtcDateTime }));

        var rejectedMeta = await _db.ProjectMetaChangeRequests.AsNoTracking().Include(r => r.Project).Where(r => r.RequestedByUserId == userId && r.DecisionStatus == "Rejected" && r.DecidedOnUtc.HasValue && r.DecidedOnUtc.Value >= returnedCutoffUtc).OrderByDescending(r => r.DecidedOnUtc).Take(10).ToListAsync(ct);
        var rejectedMetaProjectIds = rejectedMeta.Select(r => r.ProjectId).ToArray();
        var newerMetaProjects = await _db.ProjectMetaChangeRequests.AsNoTracking().Where(r => rejectedMetaProjectIds.Contains(r.ProjectId) && (r.DecisionStatus == "Pending" || r.DecisionStatus == "Approved")).Select(r => new { r.ProjectId, EventAt = r.DecidedOnUtc ?? r.RequestedOnUtc }).ToListAsync(ct);
        rejectedMeta = rejectedMeta.Where(r => !newerMetaProjects.Any(n => n.ProjectId == r.ProjectId && r.DecidedOnUtc.HasValue && n.EventAt > r.DecidedOnUtc.Value)).Take(5).ToList();
        items.AddRange(rejectedMeta.Select(r => new WorkspaceAttentionItemVm { Type = "Metadata", Title = r.Project?.Name ?? "Metadata change", Detail = "Metadata change request rejected", Severity = "Danger", BadgeText = "Returned", ActionText = "Correct", ActionUrl = WorkspaceRouteHelper.ProjectMetaRequest(r.ProjectId), DueOrEventDateUtc = r.DecidedOnUtc?.UtcDateTime }));

        var rejectedDocuments = await _db.ProjectDocumentRequests.AsNoTracking().Where(r => r.RequestedByUserId == userId && r.Status == ProjectDocumentRequestStatus.Rejected && r.ReviewedAtUtc.HasValue && r.ReviewedAtUtc.Value >= returnedCutoffUtc).OrderByDescending(r => r.ReviewedAtUtc).Take(10).ToListAsync(ct);
        var rejectedDocumentProjectIds = rejectedDocuments.Select(r => r.ProjectId).ToArray();
        var newerDocumentProjects = await _db.ProjectDocumentRequests.AsNoTracking().Where(r => rejectedDocumentProjectIds.Contains(r.ProjectId) && (r.Status == ProjectDocumentRequestStatus.Submitted || r.Status == ProjectDocumentRequestStatus.Approved)).Select(r => new { r.ProjectId, EventAt = r.ReviewedAtUtc ?? r.RequestedAtUtc }).ToListAsync(ct);
        rejectedDocuments = rejectedDocuments.Where(r => !newerDocumentProjects.Any(n => n.ProjectId == r.ProjectId && r.ReviewedAtUtc.HasValue && n.EventAt > r.ReviewedAtUtc.Value)).Take(5).ToList();
        items.AddRange(rejectedDocuments.Select(r => new WorkspaceAttentionItemVm { Type = "Document", Title = r.Title, Detail = "Document request rejected", Severity = "Danger", BadgeText = "Returned", ActionText = "Correct", ActionUrl = WorkspaceRouteHelper.ProjectDocumentRequest(r.ProjectId), DueOrEventDateUtc = r.ReviewedAtUtc?.UtcDateTime }));
        return items.OrderByDescending(i => i.DueOrEventDateUtc).Take(12).ToList();
    }
    private WorkspaceProjectMatrixRowVm BuildMatrixRow(Project p, WorkspaceRecordHealthVm health, string userId, DateOnly today)
    {
        var stage = WorkspaceNudgeService.GetCurrentStage(p);
        var action = _nudges.GetNextAction(p, health, userId, today, out var url);
        var last = WorkspaceNudgeService.LastPoRemark(p, userId);
        var overdue = _nudges.IsCurrentStageOverdue(stage, today);
        var issue = _nudges.HasCurrentStageTimelineIssue(stage);

        return new WorkspaceProjectMatrixRowVm
        {
            ProjectId = p.Id,
            ProjectName = p.Name,
            CurrentStageCode = stage?.StageCode ?? "—",
            CurrentStageName = stage?.StageCode ?? "Not started",
            DaysInCurrentStage = WorkspaceNudgeService.GetCurrentStageAgeDays(p, today),
            UpdateStatus = _nudges.GetUpdateStatus(last, today),
            TimelineStatus = p.ProjectStages.Any(s => s.Status == StageStatus.Completed && !s.CompletedOn.HasValue) || overdue
                ? "ActionRequired"
                : issue ? "Attention" : "Ok",
            RecordStatus = health.HealthPercent >= 80 ? "Ok" : health.HealthPercent >= 60 ? "Attention" : "ActionRequired",
            RecordHealthPercent = health.HealthPercent,
            RecordGapCount = health.GapDetails.Count,
            RecordHealth = health,
            LastPoRemarkAtUtc = last,
            HasBackfill = p.ProjectStages.Any(s => s.Status == StageStatus.Completed && !s.CompletedOn.HasValue),
            HasCurrentStageIssue = issue,
            HasOverdueCurrentStage = overdue,
            NextActionText = action,
            NextActionUrl = url,
            OpenUrl = WorkspaceRouteHelper.ProjectOverview(p.Id),
            AddRemarkUrl = WorkspaceRouteHelper.ProjectRemarks(p.Id),
            TimelineUrl = WorkspaceRouteHelper.ProjectTimeline(p.Id)
        };
    }


    // SECTION: Project table ordering surfaces operational risk before routine records.
    private static int ProjectAttentionRank(WorkspaceProjectMatrixRowVm row)
    {
        if (row.HasOverdueCurrentStage || row.UpdateStatus == "ActionRequired") return 0;
        if (row.HasBackfill || row.TimelineStatus == "ActionRequired") return 1;
        if (row.HasCurrentStageIssue || row.UpdateStatus == "Attention") return 2;
        if (row.RecordGapCount > 0) return 3;
        return 4;
    }

    // SECTION: Pending item ordering keeps returned corrections above lower-priority nudges.
    private static int WorkspaceSeverityRank(string severity) => severity switch
    {
        "Danger" => 0,
        "Warning" => 1,
        "Info" => 2,
        _ => 3
    };

    private static int WorkspacePendingPriority(string detail)
    {
        if (detail.Contains("returned", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("rejected", StringComparison.OrdinalIgnoreCase)) return 0;
        if (detail.Contains("backfill", StringComparison.OrdinalIgnoreCase)) return 1;
        if (detail.Contains("overdue", StringComparison.OrdinalIgnoreCase)) return 2;
        if (detail.Contains("actual start", StringComparison.OrdinalIgnoreCase)) return 3;
        if (detail.Contains("planned due", StringComparison.OrdinalIgnoreCase)) return 4;
        if (detail.Contains("remark", StringComparison.OrdinalIgnoreCase)) return 5;
        return 6;
    }

    private async Task<WorkspaceEngagementVm> BuildEngagementAsync(string userId, ApplicationUser? user, DateTime monthStart, CancellationToken ct)
    {
        // SECTION: ERP engagement dates are materialized before DateTimeOffset-to-DateTime conversion to avoid provider coercion failures.
        var monthStartOffset = new DateTimeOffset(monthStart, TimeSpan.Zero);
        var authEvents = await _db.AuthEvents.AsNoTracking().Where(a => a.UserId == userId && a.Event == "LoginSucceeded" && a.WhenUtc >= monthStartOffset).Select(a => a.WhenUtc).ToListAsync(ct);
        var auditDates = await _db.AuditLogs.AsNoTracking().Where(a => a.UserId == userId && a.TimeUtc >= monthStart).Select(a => a.TimeUtc.Date).ToListAsync(ct);
        var remarkRows = await _db.Remarks.AsNoTracking().Where(r => r.AuthorUserId == userId && !r.IsDeleted && r.CreatedAtUtc >= monthStart).Select(r => r.CreatedAtUtc).ToListAsync(ct);
        var taskAuditRows = await _db.ActionTaskAuditLogs.AsNoTracking().Where(a => a.PerformedByUserId == userId && a.PerformedAt >= monthStart).Select(a => a.PerformedAt).ToListAsync(ct);
        var documentRows = await _db.ProjectDocuments.AsNoTracking().Where(d => d.UploadedByUserId == userId && d.Status == ProjectDocumentStatus.Published && d.UploadedAtUtc >= monthStartOffset).Select(d => d.UploadedAtUtc).ToListAsync(ct);
        var ideaCommentRows = await _db.ProjectIdeaComments.AsNoTracking().Where(c => c.CreatedByUserId == userId && !c.IsDeleted && c.CreatedAt >= monthStart).Select(c => c.CreatedAt).ToListAsync(ct);
        var ideaNoteRows = await _db.ProjectIdeaNotes.AsNoTracking().Where(n => n.CreatedByUserId == userId && !n.IsDeleted && n.UpdatedAt >= monthStart).Select(n => n.UpdatedAt).ToListAsync(ct);
        var ideaDocumentRows = await _db.ProjectIdeaDocuments.AsNoTracking().Where(d => d.UploadedByUserId == userId && !d.IsDeleted && d.UploadedAt >= monthStart).Select(d => d.UploadedAt).ToListAsync(ct);

        var activeDates = authEvents.Select(x => x.UtcDateTime.Date)
            .Concat(auditDates)
            .Concat(remarkRows.Select(x => x.Date))
            .Concat(taskAuditRows.Select(x => x.Date))
            .Concat(documentRows.Select(x => x.UtcDateTime.Date))
            .Concat(ideaCommentRows.Select(x => x.Date))
            .Concat(ideaNoteRows.Select(x => x.Date))
            .Concat(ideaDocumentRows.Select(x => x.Date))
            .Distinct()
            .ToList();

        return new WorkspaceEngagementVm
        {
            LastLoginUtc = user?.LastLoginUtc,
            LastActivityUtc = activeDates
                .OrderByDescending(d => d)
                .Select(d => (DateTime?)d)
                .FirstOrDefault() ?? user?.LastLoginUtc,
            LoginsThisMonth = authEvents.Count,
            ActiveDaysThisMonth = activeDates.Count,
            ActionsRecordedThisMonth = auditDates.Count
                + remarkRows.Count
                + taskAuditRows.Count
                + documentRows.Count
                + ideaCommentRows.Count
                + ideaNoteRows.Count
                + ideaDocumentRows.Count,
            RemarksPostedThisMonth = remarkRows.Count,
            TasksUpdatedThisMonth = taskAuditRows.Count,
            DocumentsUploadedThisMonth = documentRows.Count + ideaDocumentRows.Count,
            EngagementLabel = activeDates.Count >= 8 ? "Active" : "Getting Started"
        };
    }
    // SECTION: Improve-score actions convert record health gaps into direct correction links.
    private static IReadOnlyList<WorkspaceImprovementVm> BuildImproveScoreItems(IEnumerable<WorkspaceRecordHealthVm> healthRows, int maxItems)
    {
        var items = new List<WorkspaceImprovementVm>();

        foreach (var health in healthRows.OrderBy(h => h.HealthPercent))
        {
            foreach (var gap in health.Gaps)
            {
                items.Add(new WorkspaceImprovementVm
                {
                    ProjectId = health.ProjectId,
                    ProjectName = health.ProjectName,
                    Gap = gap,
                    Label = WorkspaceDisplayHelpers.ImprovementLabel(gap),
                    Url = ResolveImprovementUrl(health.ProjectId, gap),
                    Severity = health.HealthPercent < 60 ? "Danger" : "Warning"
                });
            }
        }

        return items
            .GroupBy(i => i.Label)
            .Select(g => g.First())
            .Take(maxItems)
            .ToList();
    }

    // SECTION: Improvement routing points each gap to the most relevant correction workflow.
    private static string ResolveImprovementUrl(int projectId, string gap)
    {
        if (gap.Contains("remark", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceRouteHelper.ProjectRemarks(projectId);
        }

        if (gap.Contains("backfill", StringComparison.OrdinalIgnoreCase) ||
            gap.Contains("current stage timeline", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceRouteHelper.ProjectTimeline(projectId);
        }

        if (gap.Contains("photo", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceRouteHelper.ProjectPhotos(projectId);
        }

        if (gap.Contains("video", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceRouteHelper.ProjectVideos(projectId);
        }

        if (gap.Contains("document", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceRouteHelper.ProjectDocumentsTab(projectId);
        }

        if (gap.Contains("budget", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceRouteHelper.ProjectOverview(projectId);
        }

        if (gap.Contains("description", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceRouteHelper.ProjectMetaRequest(projectId);
        }

        return WorkspaceRouteHelper.ProjectOverview(projectId);
    }

    private sealed record WorkspaceImproveProjectsBuildResult(
        IReadOnlyList<WorkspaceProjectImprovementVm> Items,
        int TotalCount);

    // SECTION: Group record-health gaps by project to avoid repetitive right-rail rows.
    private static WorkspaceImproveProjectsBuildResult BuildImproveProjects(
        IEnumerable<WorkspaceRecordHealthVm> healthRows,
        int maxProjects)
    {
        var projectsWithGaps = healthRows
            .Where(h => h.GapDetails.Any())
            .OrderBy(h => h.HealthPercent)
            .ThenByDescending(h => h.GapDetails.Count)
            .ToList();

        var items = projectsWithGaps
            .Take(maxProjects)
            .Select(h =>
            {
                var gapDetails = h.GapDetails
                    .Select(gap => new WorkspaceProjectGapDetailVm
                    {
                        Label = gap.FieldLabel,
                        ActionText = gap.ActionText,
                        ActionUrl = gap.ActionUrl,
                        Icon = gap.Icon,
                        Severity = gap.Status == "Pending" ? "Warning" : "Info"
                    })
                    .ToList();

                return new WorkspaceProjectImprovementVm
                {
                    ProjectId = h.ProjectId,
                    ProjectName = h.ProjectName,
                    FixCount = h.GapDetails.Count,
                    FixLabels = gapDetails
                        .Take(3)
                        .Select(detail => detail.Label)
                        .ToList(),
                    GapDetails = gapDetails,
                    HealthPercent = h.HealthPercent,
                    RecordHealth = h,
                    HealthLabel = WorkspaceDisplayHelpers.HealthBandLabel(h.HealthPercent),
                    HealthCss = WorkspaceDisplayHelpers.HealthCss(h.HealthPercent),
                    Url = WorkspaceRouteHelper.ProjectOverview(h.ProjectId),
                    Severity = h.HealthPercent < 60 ? "Danger" : "Warning"
                };
            })
            .ToList();

        return new WorkspaceImproveProjectsBuildResult(items, projectsWithGaps.Count);
    }

    // SECTION: Gap details map record-completeness output to direct correction actions.
    private static WorkspaceProjectGapDetailVm BuildGapDetail(int projectId, string gap)
    {
        if (gap.Contains("description", StringComparison.OrdinalIgnoreCase))
        {
            return new WorkspaceProjectGapDetailVm
            {
                Label = "Brief description pending",
                ActionText = "Edit details",
                ActionUrl = WorkspaceRouteHelper.ProjectMetaRequest(projectId),
                Icon = "bi-card-text",
                Severity = "Warning"
            };
        }

        if (gap.Contains("photo", StringComparison.OrdinalIgnoreCase))
        {
            return new WorkspaceProjectGapDetailVm
            {
                Label = "Add project photos",
                ActionText = "Add photos",
                ActionUrl = WorkspaceRouteHelper.ProjectPhotos(projectId),
                Icon = "bi-images",
                Severity = "Warning"
            };
        }

        if (gap.Contains("document", StringComparison.OrdinalIgnoreCase))
        {
            return new WorkspaceProjectGapDetailVm
            {
                Label = "Upload project documents",
                ActionText = "Upload",
                ActionUrl = WorkspaceRouteHelper.ProjectDocumentsTab(projectId),
                Icon = "bi-file-earmark-text",
                Severity = "Warning"
            };
        }

        if (gap.Contains("video", StringComparison.OrdinalIgnoreCase))
        {
            return new WorkspaceProjectGapDetailVm
            {
                Label = "Add project video",
                ActionText = "Add video",
                ActionUrl = WorkspaceRouteHelper.ProjectVideos(projectId),
                Icon = "bi-camera-video",
                Severity = "Warning"
            };
        }

        if (IsProcurementGap(gap))
        {
            return new WorkspaceProjectGapDetailVm
            {
                Label = gap,
                ActionText = "Update procurement",
                ActionUrl = WorkspaceRouteHelper.ProjectOverview(projectId),
                Icon = "bi-currency-rupee",
                Severity = "Warning"
            };
        }

        if (IsTimelineGap(gap))
        {
            return new WorkspaceProjectGapDetailVm
            {
                Label = gap,
                ActionText = gap.Contains("Current-stage", StringComparison.OrdinalIgnoreCase)
                    ? "Update PDC"
                    : "Complete dates",
                ActionUrl = WorkspaceRouteHelper.ProjectTimeline(projectId),
                Icon = "bi-calendar-check",
                Severity = "Warning"
            };
        }

        return new WorkspaceProjectGapDetailVm
        {
            Label = gap,
            ActionText = "Open",
            ActionUrl = WorkspaceRouteHelper.ProjectOverview(projectId),
            Icon = "bi-exclamation-circle",
            Severity = "Warning"
        };
    }

    private static bool IsProcurementGap(string gap)
        => gap.Contains("Cost pending", StringComparison.OrdinalIgnoreCase)
           || gap.Contains("Supply Order Date pending", StringComparison.OrdinalIgnoreCase);

    private static bool IsTimelineGap(string gap)
        => gap.Contains("Current-stage", StringComparison.OrdinalIgnoreCase)
           || gap.Contains("actual start missing", StringComparison.OrdinalIgnoreCase)
           || gap.Contains("actual completion missing", StringComparison.OrdinalIgnoreCase);

    // SECTION: Quick actions keep only durable workspace destinations.
    private static IReadOnlyList<WorkspaceQuickActionVm> BuildQuickActions(string userId)
    {
        return new[]
        {
            new WorkspaceQuickActionVm { Text = "Open My Projects", Url = WorkspaceRouteHelper.MyProjects(userId), Icon = "bi-kanban" },
            new WorkspaceQuickActionVm { Text = "Open Task Management", Url = WorkspaceRouteHelper.ActionTasksMyWork(), Icon = "bi-list-check" },
            new WorkspaceQuickActionVm { Text = "Open My Project Ideas", Url = WorkspaceRouteHelper.ProjectIdeasMine(), Icon = "bi-lightbulb" },
            new WorkspaceQuickActionVm { Text = "Open AOTS Inbox", Url = WorkspaceRouteHelper.AotsInbox(), Icon = "bi-file-earmark-text" },
            new WorkspaceQuickActionVm { Text = "Open My Notebook", Url = WorkspaceRouteHelper.PersonalReminders(), Icon = "bi-journal-bookmark" }
        };
    }

    // SECTION: Command-bar composition chips summarize actionable work without adding dashboard clutter.
    private static IReadOnlyList<WorkspaceCommandChipVm> BuildCommandChips(
        int remarksDueCount,
        int aotsUnreadCount,
        int otherAssignedTasksDueCount,
        int ideasNeedingUpdateCount)
    {
        return new List<WorkspaceCommandChipVm>
        {
            new() { Label = remarksDueCount == 1 ? "Remark" : "Remarks", Count = remarksDueCount, Icon = "bi-chat-left-text", State = remarksDueCount > 0 ? "Attention" : "Clear" },
            new() { Label = "AOTS", Count = aotsUnreadCount, Icon = "bi-file-earmark-text", State = aotsUnreadCount > 0 ? "Attention" : "Clear" },
            new() { Label = "Other Tasks", Count = otherAssignedTasksDueCount, Icon = "bi-list-check", State = otherAssignedTasksDueCount > 0 ? "Attention" : "Clear" },
            new() { Label = "Ideas", Count = ideasNeedingUpdateCount, Icon = "bi-lightbulb", State = ideasNeedingUpdateCount > 0 ? "Attention" : "Clear" }
        };
    }

    // SECTION: Project data completeness insights convert record-health gaps into useful portfolio signals.
    private static WorkspaceDataCompletenessInsightVm BuildDataCompletenessInsight(
        int assignedProjectsCount,
        IReadOnlyDictionary<int, WorkspaceRecordHealthVm> health)
    {
        if (assignedProjectsCount == 0)
        {
            return new WorkspaceDataCompletenessInsightVm();
        }

        var healthItems = health.Values.ToList();
        if (!healthItems.Any())
        {
            return new WorkspaceDataCompletenessInsightVm { AssignedProjectsCount = assignedProjectsCount };
        }

        var gapGroups = healthItems
            .SelectMany(h => h.Gaps)
            .Select(WorkspaceDisplayHelpers.ShortGapLabel)
            .GroupBy(label => label)
            .Select(group => new { Label = group.Key, Count = group.Count() })
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Label)
            .Take(6)
            .ToList();

        var maxGapCount = gapGroups.Count > 0 ? gapGroups.Max(group => group.Count) : 0;
        var gapFrequencies = gapGroups
            .Select(group => new WorkspaceGapFrequencyVm
            {
                Label = group.Label,
                Count = group.Count,
                PercentOfMax = maxGapCount == 0 ? 0 : Math.Max(8, (int)Math.Round(group.Count * 100m / maxGapCount))
            })
            .ToList();

        var best = healthItems
            .OrderByDescending(h => h.HealthPercent)
            .ThenBy(h => h.ProjectName)
            .FirstOrDefault();
        var worst = healthItems
            .OrderBy(h => h.HealthPercent)
            .ThenByDescending(h => h.GapDetails.Count)
            .ThenBy(h => h.ProjectName)
            .FirstOrDefault();

        return new WorkspaceDataCompletenessInsightVm
        {
            AverageCompletenessPercent = (int)Math.Round(healthItems.Average(h => h.HealthPercent)),
            ProjectsWithGapsCount = healthItems.Count(h => h.Gaps.Any()),
            AssignedProjectsCount = assignedProjectsCount,
            MostCommonGapLabel = gapFrequencies.FirstOrDefault()?.Label ?? "None",
            BestProjectName = best?.ProjectName,
            BestProjectScore = best?.HealthPercent,
            NeedsMostAttentionProjectName = worst?.ProjectName,
            NeedsMostAttentionProjectScore = worst?.HealthPercent,
            GapFrequencies = gapFrequencies
        };
    }

    // SECTION: Local workspace rail uses unique section anchors for deterministic scroll navigation.
    private static IReadOnlyList<WorkspaceRailItemVm> BuildRailItems(ProjectOfficerWorkspaceVm vm)
    {
        return new List<WorkspaceRailItemVm>
        {
            new() { Label = "Today", Icon = "bi-calendar-check", Anchor = "#today", Count = vm.DailyActionCount, IsPrimary = true },
            new() { Label = "Action Queue", Icon = "bi-list-check", Anchor = "#action-queue", Count = vm.ActionQueueTotalCount },
            new() { Label = "Assigned Projects", Icon = "bi-kanban", Anchor = "#assigned-projects", Count = vm.AssignedProjectCount },
            new() { Label = "Project Data Gaps", Icon = "bi-folder-x", Anchor = "#project-data-gaps", Count = vm.ImproveProjectsTotalCount },
            new() { Label = "My Ideas", Icon = "bi-lightbulb", Anchor = "#my-ideas-reminders", Count = vm.AssignedIdeaCount },
            new() { Label = "Reminders", Icon = "bi-bell", Anchor = "#reminders", Count = vm.PersonalReminders.Count }
        };
    }
}
