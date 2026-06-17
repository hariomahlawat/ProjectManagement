using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Plans;
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
        var monthStart = new DateTime(istNow.Year, istNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
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
        var dailyActionCount = remarksDue.Count + officialTasksDue.Count + ideasNeedingUpdate.Count + aotsUnreadCount;
        var actionQueue = BuildActionQueue(returnedItems, officialTasksDue, remarksDue, ideasNeedingUpdate, aotsDocuments, timelineAlerts);

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
        var matrix = projects.Take(6).Select(p => BuildMatrixRow(p, health[p.Id], userId, today)).ToList();
        var engagement = await BuildEngagementAsync(userId, user, monthStart, ct);
        var avgHealth = health.Count == 0 ? 100 : (int)Math.Round(health.Values.Average(h => h.HealthPercent));

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
            OfficialTaskCount = tasks.Count,
            IdeasNeedingUpdateCount = ideaVms.Count(i => i.NeedsUpdate),
            AotsUnreadCount = aotsUnreadCount,
            AotsUrl = WorkspaceRouteHelper.AotsInbox(),
            RecordGapCount = health.Values.Sum(h => h.Gaps.Count),
            AssignedIdeaCount = ideaVms.Count,
            Engagement = engagement,
            PendingWithMe = pending.Take(5).ToList(),
            ActionQueue = actionQueue,
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
            ImproveProjects = BuildImproveProjects(health.Values, maxProjects: 3),
            NextBestAction = BuildNextBestActionFromQueue(actionQueue),
            PersonalReminders = reminders,
            QuickActions = BuildQuickActions(userId),
            MyProjectsUrl = myProjectsUrl
        };

        vm.Kpis = BuildKpis(vm);
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
        return await _db.TodoItems
            .AsNoTracking()
            .Where(t => t.OwnerId == userId && t.Status != TodoStatus.Done && t.DeletedUtc == null)
            .OrderByDescending(t => t.IsPinned)
            .ThenBy(t => t.DueAtUtc)
            .Take(5)
            .Select(t => new WorkspaceReminderVm
            {
                ReminderId = t.Id,
                Title = t.Title,
                Priority = t.Priority.ToString(),
                DueAtUtc = t.DueAtUtc,
                IsPinned = t.IsPinned,
                OpenUrl = WorkspaceRouteHelper.PersonalReminders()
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
    private static IReadOnlyList<WorkspaceActionQueueItemVm> BuildActionQueue(
        IReadOnlyList<WorkspaceAttentionItemVm> returnedItems,
        IReadOnlyList<WorkspaceTaskVm> otherAssignedTasksDue,
        IReadOnlyList<WorkspaceAttentionItemVm> remarksDue,
        IReadOnlyList<WorkspaceIdeaVm> ideasNeedingUpdate,
        IReadOnlyList<WorkspaceAotsDocumentVm> aotsDocuments,
        IReadOnlyList<WorkspaceAttentionItemVm> timelineAlerts)
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

        items.AddRange(timelineAlerts.Select(item => new WorkspaceActionQueueItemVm
        {
            Type = "Timeline",
            BadgeText = item.BadgeText,
            Title = item.Title,
            Detail = item.Detail,
            Meta = "Timeline",
            Severity = item.Severity,
            ActionText = item.ActionText,
            ActionUrl = item.ActionUrl,
            SortDateUtc = item.DueOrEventDateUtc
        }));

        return items
            .OrderBy(GetActionQueuePriority)
            .ThenByDescending(i => i.SortDateUtc)
            .Take(8)
            .ToList();
    }

    // SECTION: Next best action mirrors the first row in the unified action queue.
    private static WorkspaceAttentionItemVm? BuildNextBestActionFromQueue(IReadOnlyList<WorkspaceActionQueueItemVm> queue)
    {
        var first = queue.FirstOrDefault();
        if (first is null)
        {
            return null;
        }

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
            "Timeline" => 6,
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
            TimelineStatus = p.ProjectStages.Any(s => s.RequiresBackfill) || overdue
                ? "ActionRequired"
                : issue ? "Attention" : "Ok",
            RecordStatus = health.HealthPercent >= 80 ? "Ok" : health.HealthPercent >= 60 ? "Attention" : "ActionRequired",
            RecordHealthPercent = health.HealthPercent,
            RecordGapCount = health.Gaps.Count,
            LastPoRemarkAtUtc = last,
            HasBackfill = p.ProjectStages.Any(s => s.RequiresBackfill),
            HasCurrentStageIssue = issue,
            HasOverdueCurrentStage = overdue,
            NextActionText = action,
            NextActionUrl = url,
            OpenUrl = WorkspaceRouteHelper.ProjectOverview(p.Id),
            AddRemarkUrl = WorkspaceRouteHelper.ProjectRemarks(p.Id),
            TimelineUrl = WorkspaceRouteHelper.ProjectTimeline(p.Id)
        };
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
        if (gap.Contains("remark", StringComparison.OrdinalIgnoreCase)) return WorkspaceRouteHelper.ProjectRemarks(projectId);
        if (gap.Contains("backfill", StringComparison.OrdinalIgnoreCase) || gap.Contains("current stage", StringComparison.OrdinalIgnoreCase) || gap.Contains("completion date", StringComparison.OrdinalIgnoreCase)) return WorkspaceRouteHelper.ProjectTimeline(projectId);
        if (gap.Contains("classification", StringComparison.OrdinalIgnoreCase) || gap.Contains("metadata", StringComparison.OrdinalIgnoreCase)) return WorkspaceRouteHelper.ProjectMetaRequest(projectId);
        if (gap.Contains("document", StringComparison.OrdinalIgnoreCase)) return WorkspaceRouteHelper.ProjectDocumentRequest(projectId);
        return WorkspaceRouteHelper.ProjectOverview(projectId);
    }

    // SECTION: Group record-health gaps by project to avoid repetitive right-rail rows.
    private static IReadOnlyList<WorkspaceProjectImprovementVm> BuildImproveProjects(
        IEnumerable<WorkspaceRecordHealthVm> healthRows,
        int maxProjects)
    {
        return healthRows
            .Where(h => h.Gaps.Any())
            .OrderBy(h => h.HealthPercent)
            .Take(maxProjects)
            .Select(h => new WorkspaceProjectImprovementVm
            {
                ProjectId = h.ProjectId,
                ProjectName = h.ProjectName,
                FixCount = h.Gaps.Count,
                FixLabels = h.Gaps
                    .Take(3)
                    .Select(WorkspaceDisplayHelpers.ImprovementLabel)
                    .ToList(),
                Url = WorkspaceRouteHelper.ProjectOverview(h.ProjectId),
                Severity = h.HealthPercent < 60 ? "Danger" : "Warning"
            })
            .ToList();
    }

    // SECTION: Quick actions keep only durable workspace destinations.
    private static IReadOnlyList<WorkspaceQuickActionVm> BuildQuickActions(string userId)
    {
        return new[]
        {
            new WorkspaceQuickActionVm { Text = "Open My Projects", Url = WorkspaceRouteHelper.MyProjects(userId), Icon = "bi-kanban" },
            new WorkspaceQuickActionVm { Text = "View Other Assigned Tasks", Url = WorkspaceRouteHelper.ActionTasksMyWork(), Icon = "bi-list-check" },
            new WorkspaceQuickActionVm { Text = "Open My Project Ideas", Url = WorkspaceRouteHelper.ProjectIdeasMine(), Icon = "bi-lightbulb" },
            new WorkspaceQuickActionVm { Text = "Open AOTS Inbox", Url = WorkspaceRouteHelper.AotsInbox(), Icon = "bi-file-earmark-text" },
            new WorkspaceQuickActionVm { Text = "Open Personal Reminders", Url = WorkspaceRouteHelper.PersonalReminders(), Icon = "bi-pin-angle" }
        };
    }

    // SECTION: Compact KPI strip highlights daily updates before project health.
    private static IReadOnlyList<WorkspaceKpiVm> BuildKpis(ProjectOfficerWorkspaceVm vm)
    {
        return new[]
        {
            new WorkspaceKpiVm { Title = "Remarks Due", Value = vm.RemarksDueCount.ToString(), Caption = "Project updates", Severity = vm.RemarksDueCount == 0 ? "Good" : "Warning", Icon = "bi-chat-left-text" },
            new WorkspaceKpiVm { Title = "Other Assigned Tasks", Value = vm.OfficialTaskCount.ToString(), Caption = vm.OverdueTaskCount == 0 ? "No overdue assigned task" : $"{vm.OverdueTaskCount} overdue", Severity = vm.OverdueTaskCount == 0 ? "Good" : "Danger", Icon = "bi-list-check" },
            new WorkspaceKpiVm { Title = "Project Ideas", Value = vm.AssignedIdeaCount.ToString(), Caption = vm.IdeasNeedingUpdateCount == 0 ? "No stale idea" : $"{vm.IdeasNeedingUpdateCount} need update", Severity = vm.IdeasNeedingUpdateCount == 0 ? "Good" : "Warning", Icon = "bi-lightbulb" },
            new WorkspaceKpiVm { Title = "AOTS", Value = vm.AotsUnreadCount.ToString(), Caption = vm.AotsUnreadCount == 0 ? "All read" : "Unread documents", Severity = vm.AotsUnreadCount == 0 ? "Good" : "Warning", Icon = "bi-file-earmark-text" },
            new WorkspaceKpiVm { Title = "Assigned Projects", Value = vm.AssignedProjectCount.ToString(), Caption = "Currently with you", Severity = "Info", Icon = "bi-kanban" }
        };
    }

    // SECTION: Local workspace rail anchors daily-action sections without changing global navigation.
    private static IReadOnlyList<WorkspaceRailItemVm> BuildRailItems(ProjectOfficerWorkspaceVm vm)
    {
        return new[]
        {
            new WorkspaceRailItemVm { Label = "Today", Icon = "bi-calendar-check", Count = vm.DailyActionCount, Anchor = "#today", IsPrimary = true },
            new WorkspaceRailItemVm { Label = "Remarks Due", Icon = "bi-chat-left-text", Count = vm.RemarksDueCount, Anchor = "#remarks" },
            new WorkspaceRailItemVm { Label = "Other Assigned Tasks", Icon = "bi-list-check", Count = vm.OfficialTaskCount, Anchor = "#other-tasks" },
            new WorkspaceRailItemVm { Label = "Project Ideas", Icon = "bi-lightbulb", Count = vm.AssignedIdeaCount, Anchor = "#project-ideas" },
            new WorkspaceRailItemVm { Label = "AOTS", Icon = "bi-file-earmark-text", Count = vm.AotsUnreadCount, Anchor = "#aots" },
            new WorkspaceRailItemVm { Label = "Assigned Projects", Icon = "bi-kanban", Count = vm.AssignedProjectCount, Anchor = "#assigned-projects" },
            new WorkspaceRailItemVm { Label = "Reminders", Icon = "bi-bell", Count = vm.PersonalReminders.Count, Anchor = "#reminders" }
        };
    }
}
