using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Plans;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services.ActionTasks;
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
    public ProjectOfficerWorkspaceService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ProjectRecordHealthService health,
        WorkspaceNudgeService nudges,
        ActionTaskMyWorkQueueBuilder myWorkQueueBuilder,
        IActionTrackerClock clock)
    {
        _db = db;
        _users = users;
        _health = health;
        _nudges = nudges;
        _myWorkQueueBuilder = myWorkQueueBuilder;
        _clock = clock;
    }

    // SECTION: Workspace composition
    public async Task<ProjectOfficerWorkspaceVm> GetProjectOfficerWorkspaceAsync(string userId, ClaimsPrincipal principal, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(_clock.IstToday);
        var istNow = IstClock.ToIst(_clock.UtcNow);
        var monthStart = new DateTime(istNow.Year, istNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var user = await _users.FindByIdAsync(userId);
        var myProjectsUrl = WorkspaceRouteHelper.MyProjects(userId);
        var projects = await _db.Projects.AsNoTracking().Include(p => p.ProjectStages).Include(p => p.Remarks).Include(p => p.Documents).Where(p => p.LeadPoUserId == userId && !p.IsDeleted && p.LifecycleStatus != ProjectLifecycleStatus.Completed).OrderBy(p => p.Name).Take(50).ToListAsync(ct);
        var taskRows = await _db.ActionTasks.AsNoTracking()
            .Where(t => !t.IsDeleted && t.AssignedToUserId == userId && t.Status != ActionTaskStatuses.Closed && t.Status != ActionTaskStatuses.Backlog)
            .OrderBy(t => t.DueDate)
            .Take(50)
            .ToListAsync(ct);
        var myWorkQueue = _myWorkQueueBuilder.Build(taskRows, activeSprint: null);
        var orderedTaskRows = myWorkQueue.ActionRequiredTasks
            .Concat(myWorkQueue.CurrentWorkTasks)
            .Concat(myWorkQueue.SubmittedAwaitingClosureTasks)
            .Concat(myWorkQueue.AllMyTasks)
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .Take(8)
            .ToList();
        var overdueTaskIds = myWorkQueue.OverdueTasks.Select(t => t.Id).ToHashSet();
        var tasks = orderedTaskRows.Select(t =>
        {
            // SECTION: Date-only action task dates are normalized after materialization because Npgsql cannot translate SpecifyKind for date columns.
            var dueDateUtc = DateTime.SpecifyKind(t.DueDate.Date, DateTimeKind.Utc);
            var daysOverdue = overdueTaskIds.Contains(t.Id) ? (_clock.IstToday - t.DueDate.Date).Days : (int?)null;
            return new WorkspaceTaskVm { TaskId = t.Id, Title = t.Title, Priority = t.Priority, Status = t.Status, DueDateUtc = dueDateUtc, IsOverdue = daysOverdue.HasValue, DaysOverdue = daysOverdue, OpenUrl = WorkspaceRouteHelper.ActionTask(t.Id) };
        }).ToList();
        var ideas = await _db.ProjectIdeas.AsNoTracking().Include(i => i.Comments).Include(i => i.Notes).Include(i => i.Documents).Where(i => !i.IsDeleted && (i.AssignedProjectOfficerUserId == userId || i.CreatedByUserId == userId) && i.Status != ProjectIdeaStatuses.Archived).OrderByDescending(i => i.UpdatedAt).Take(6).ToListAsync(ct);
        var ideaVms = ideas.Select(i => { var last = new[] { i.UpdatedAt }.Concat(i.Comments.Where(c => !c.IsDeleted).Select(c => c.CreatedAt)).Concat(i.Notes.Where(n => !n.IsDeleted).Select(n => n.UpdatedAt)).Concat(i.Documents.Where(d => !d.IsDeleted).Select(d => d.UploadedAt)).DefaultIfEmpty(i.UpdatedAt).Max(); return new WorkspaceIdeaVm { IdeaId = i.Id, Title = i.Title, Status = ProjectIdeaStatuses.ToDisplay(i.Status), LastActivityAtUtc = last, NeedsUpdate = (today.DayNumber - WorkspaceNudgeService.ToIstDate(last).DayNumber) > 15 && (i.Status == ProjectIdeaStatuses.Active || i.Status == ProjectIdeaStatuses.OnHold), CommentCount = i.Comments.Count(c => !c.IsDeleted), DocumentCount = i.Documents.Count(d => !d.IsDeleted), OpenUrl = WorkspaceRouteHelper.ProjectIdea(i.Id) }; }).ToList();
        var reminders = await _db.TodoItems.AsNoTracking().Where(t => t.OwnerId == userId && t.Status != TodoStatus.Done && t.DeletedUtc == null).OrderByDescending(t => t.IsPinned).ThenBy(t => t.DueAtUtc).Take(5).Select(t => new WorkspaceReminderVm { ReminderId = t.Id, Title = t.Title, Priority = t.Priority.ToString(), DueAtUtc = t.DueAtUtc, IsPinned = t.IsPinned, OpenUrl = WorkspaceRouteHelper.PersonalReminders() }).ToListAsync(ct);
        var health = await _health.CalculateForProjectsAsync(projects, userId, ct);
        var returnedItems = await BuildReturnedItemsAsync(userId, ct);
        var pending = returnedItems
            .Concat(_nudges.BuildPendingWithMe(projects, tasks, ideaVms, userId, today))
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
            AssignedProjectCount = projects.Count,
            PendingWithMeCount = pending.Count,
            OverdueTaskCount = tasks.Count(t => t.IsOverdue),
            RecordGapCount = health.Values.Sum(h => h.Gaps.Count),
            AssignedIdeaCount = ideaVms.Count,
            Engagement = engagement,
            PendingWithMe = pending.Take(5).ToList(),
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
            PersonalReminders = reminders,
            QuickActions = BuildQuickActions(userId, pending),
            MyProjectsUrl = myProjectsUrl
        };
        vm.Kpis = BuildKpis(vm);
        return vm;
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

    // SECTION: Quick actions prioritize the highest pending correction before durable workspace destinations.
    private static IReadOnlyList<WorkspaceQuickActionVm> BuildQuickActions(string userId, IReadOnlyList<WorkspaceAttentionItemVm> pendingItems)
    {
        var actions = new List<WorkspaceQuickActionVm>();
        var topPriority = pendingItems.FirstOrDefault();
        if (topPriority is not null)
        {
            actions.Add(new WorkspaceQuickActionVm { Text = $"Fix Top Priority: {topPriority.ActionText}", Url = topPriority.ActionUrl, Icon = "bi-lightning-charge" });
        }

        actions.AddRange(new[]
        {
            new WorkspaceQuickActionVm { Text = "Open My Projects", Url = WorkspaceRouteHelper.MyProjects(userId), Icon = "bi-kanban" },
            new WorkspaceQuickActionVm { Text = "View My Official Tasks", Url = WorkspaceRouteHelper.ActionTasksMyWork(), Icon = "bi-list-check" },
            new WorkspaceQuickActionVm { Text = "Open My Project Ideas", Url = WorkspaceRouteHelper.ProjectIdeasMine(), Icon = "bi-lightbulb" },
            new WorkspaceQuickActionVm { Text = "Open Personal Reminders", Url = WorkspaceRouteHelper.PersonalReminders(), Icon = "bi-pin-angle" }
        });

        return actions;
    }

    private static IReadOnlyList<WorkspaceKpiVm> BuildKpis(ProjectOfficerWorkspaceVm vm) => new[]
    {
        new WorkspaceKpiVm
        {
            Title = "Portfolio Health",
            Value = $"{vm.PortfolioHealthPercent}%",
            Caption = vm.PortfolioHealthLabel,
            Severity = vm.PortfolioHealthPercent >= 80 ? "Good" : vm.PortfolioHealthPercent >= 60 ? "Warning" : "Danger",
            Icon = "bi-heart-pulse"
        },
        new WorkspaceKpiVm
        {
            Title = "Pending With Me",
            Value = vm.PendingWithMeCount.ToString(),
            Caption = "Actionable nudges",
            Severity = vm.PendingWithMeCount == 0 ? "Good" : "Warning",
            Icon = "bi-inbox"
        },
        new WorkspaceKpiVm
        {
            Title = "Overdue Tasks",
            Value = vm.OverdueTaskCount.ToString(),
            Caption = "Official tasks",
            Severity = vm.OverdueTaskCount == 0 ? "Good" : "Danger",
            Icon = "bi-exclamation-triangle"
        },
        new WorkspaceKpiVm
        {
            Title = "Record Gaps",
            Value = vm.RecordGapCount.ToString(),
            Caption = $"Across {vm.AssignedProjectCount} assigned projects",
            Severity = vm.RecordGapCount == 0 ? "Good" : "Warning",
            Icon = "bi-folder-check"
        },
        new WorkspaceKpiVm
        {
            Title = "ERP Engagement",
            Value = vm.Engagement.ActiveDaysThisMonth.ToString(),
            Caption = "Active days this month",
            Severity = "Info",
            Icon = "bi-activity"
        }
    };
}
