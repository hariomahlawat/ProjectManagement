using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Usage;
using ProjectManagement.Services;
using ProjectManagement.ViewModels;
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
    private readonly IProjectOfficerConferenceActionQuery _conferenceActions;
    private readonly IErpUsageQueryService _erpUsage;
    private readonly IMemoryCache _cache;
    private readonly IOfficerConferenceReadService _conference;
    private readonly ProjectProcurementReadService _procurement;
    private readonly ProjectManagement.Services.Stages.PlanReadService _planRead;

    public ProjectOfficerWorkspaceService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ProjectRecordHealthService health,
        WorkspaceNudgeService nudges,
        ActionTaskMyWorkQueueBuilder myWorkQueueBuilder,
        IActionTrackerClock clock,
        IAotsUnreadService aotsUnreadService,
        IProjectOfficerConferenceActionQuery conferenceActions,
        IErpUsageQueryService erpUsage,
        IMemoryCache cache,
        IOfficerConferenceReadService conference,
        ProjectProcurementReadService procurement,
        ProjectManagement.Services.Stages.PlanReadService planRead)
    {
        _db = db;
        _users = users;
        _health = health;
        _nudges = nudges;
        _myWorkQueueBuilder = myWorkQueueBuilder;
        _clock = clock;
        _aotsUnreadService = aotsUnreadService;
        _conferenceActions = conferenceActions;
        _erpUsage = erpUsage;
        _cache = cache;
        _conference = conference;
        _procurement = procurement;
        _planRead = planRead;
    }

    // SECTION: Workspace composition
    public Task<ProjectOfficerWorkspaceVm> GetProjectOfficerWorkspaceAsync(
        string userId,
        ClaimsPrincipal principal,
        CancellationToken ct)
        => GetProjectOfficerWorkspaceAsync(
            userId,
            principal,
            ProjectOfficerWorkspaceView.Overview,
            includeDocuments: true,
            ct);

    public Task<ProjectOfficerWorkspaceVm> GetProjectOfficerWorkspaceAsync(
        string userId,
        ClaimsPrincipal principal,
        bool includeDocuments,
        CancellationToken ct)
        => GetProjectOfficerWorkspaceAsync(
            userId,
            principal,
            ProjectOfficerWorkspaceView.Overview,
            includeDocuments,
            ct);

    public async Task<ProjectOfficerWorkspaceVm> GetProjectOfficerWorkspaceAsync(
        string userId,
        ClaimsPrincipal principal,
        ProjectOfficerWorkspaceView view,
        bool includeDocuments,
        CancellationToken ct,
        string? activityPeriod = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(principal);

        var today = DateOnly.FromDateTime(_clock.IstToday);
        var user = await _users.FindByIdAsync(userId);
        var navigation = await LoadNavigationSummaryAsync(
            userId,
            today,
            // The menu no longer shows an action-queue badge; the desk carries its own counts.
            includeActionCount: false,
            ct);
        var vm = CreateWorkspaceShell(userId, principal, user, navigation);

        switch (view)
        {
            case ProjectOfficerWorkspaceView.Projects:
                // The project register reads the desk's own data, so the two always agree.
                await PopulateOverviewAsync(vm, userId, principal, today, ct);
                break;
            case ProjectOfficerWorkspaceView.Conference:
                // Conference content is composed by OfficerConferenceReadService. This
                // service still provides the lightweight PO workspace navigation shell.
                break;
            case ProjectOfficerWorkspaceView.Documents:
                if (includeDocuments)
                {
                    vm.DocumentHub = await LoadDocumentHubAsync(userId, navigation.AotsUnreadCount, ct);
                }
                break;
            case ProjectOfficerWorkspaceView.Activity:
                vm.ActivityYear = await _erpUsage.GetActivityYearAsync(
                    userId,
                    period: activityPeriod,
                    recentDays: 30,
                    cancellationToken: ct);
                vm.ActivityStrip = vm.ActivityYear.Recent;
                break;
            default:
                await PopulateOverviewAsync(vm, userId, principal, today, ct);
                break;
        }

        vm.GeneratedAtUtc = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        return vm;
    }

    private ProjectOfficerWorkspaceVm CreateWorkspaceShell(
        string userId,
        ClaimsPrincipal principal,
        ApplicationUser? user,
        ProjectOfficerNavigationSummary navigation)
        => new()
        {
            UserDisplayName = string.IsNullOrWhiteSpace(user?.FullName)
                ? principal.Identity?.Name ?? "Project Officer"
                : user.FullName,
            MyProjectsUrl = WorkspaceRouteHelper.MyProjects(userId),
            AssignedProjectCount = navigation.AssignedProjectCount,
            OfficialTaskCount = navigation.AssignedTaskCount,
            AssignedIdeaCount = navigation.AssignedIdeaCount,
            NavigationActionCount = navigation.ActionCount,
            NavigationFollowUpCount = navigation.FollowUpCount,
            AotsUnreadCount = navigation.AotsUnreadCount,
            AotsUrl = WorkspaceRouteHelper.AotsInbox()
        };

    // SECTION: Briefing desk (the Project Officer landing page)
    private async Task PopulateOverviewAsync(
        ProjectOfficerWorkspaceVm vm,
        string userId,
        ClaimsPrincipal principal,
        DateOnly today,
        CancellationToken ct)
    {
        var projects = await LoadAssignedProjectsAsync(userId, ct);
        var tasks = await LoadOtherAssignedTasksAsync(userId, today, ct);
        var ideas = await LoadProjectIdeasAsync(userId, today, ct);
        var reminders = await LoadPersonalRemindersAsync(userId, ct);
        var health = await _health.CalculateForProjectsAsync(projects, userId, ct);
        var matrixRows = BuildProjectMatrix(projects, health, userId, today);
        var operational = await LoadOperationalContextAsync(
            userId,
            today,
            projects,
            tasks,
            ideas,
            matrixRows,
            vm.AotsUnreadCount,
            ct);
        var conference = await _conference.GetForProjectOfficerAsync(userId, ct);
        var pending = await LoadPendingApprovalsAsync(userId, projects, tasks, ct);
        var lastTaskProgress = await LoadLastTaskProgressAsync(userId, tasks, ct);
        var procurementEdits = await LoadProcurementEditorsAsync(projects, health, ct);
        var planEditors = await LoadPlanEditorsAsync(userId, matrixRows, pending.PlanSinceByProject, ct);
        var upcomingEvents = await WorkspaceUpcomingEventQuery.LoadResultAsync(
            _db,
            userId,
            _clock.UtcNow,
            ct,
            windowDays: 14,
            maxItems: 5);

        var stagesByProject = projects.ToDictionary(
            project => project.Id,
            project => (IReadOnlyList<BriefingDeskStageInput>)project.ProjectStages
                .Select(stage => new BriefingDeskStageInput(
                    stage.StageCode,
                    StageCodes.DisplayNameOf(stage.StageCode),
                    stage.Status,
                    stage.SortOrder))
                .ToList());

        vm.BriefingDesk = BriefingDeskBuilder.Build(new BriefingDeskInput(
            today,
            matrixRows
                .Select(row => new BriefingDeskProjectInput(
                    row,
                    stagesByProject.GetValueOrDefault(row.ProjectId) ?? Array.Empty<BriefingDeskStageInput>(),
                    pending.PlanSinceByProject.TryGetValue(row.ProjectId, out var planSince) ? planSince : null,
                    pending.ActualsSinceByProject.TryGetValue(row.ProjectId, out var actualsSince) ? actualsSince : null,
                    procurementEdits.GetValueOrDefault(row.ProjectId),
                    planEditors.GetValueOrDefault(row.ProjectId)))
                .ToList(),
            ideas.Select(idea => new BriefingDeskIdeaInput(idea)).ToList(),
            tasks
                .Select(task => new BriefingDeskTaskInput(
                    task,
                    lastTaskProgress.TryGetValue(task.TaskId, out var lastProgress) ? lastProgress : null,
                    task.RowVersion))
                .ToList(),
            operational.ConferenceDirections,
            conference,
            pending.Waiting,
            PlanDirectSaveRole: principal.IsInRole(ProjectManagement.Configuration.RoleNames.Admin)
                || principal.IsInRole(ProjectManagement.Configuration.RoleNames.HoD)));

        ApplyOperationalContext(vm, operational);
        CacheNavigationActionCount(userId, operational.ActionQueue.TotalCount);
        vm.NavigationFollowUpCount = reminders.Count;
        vm.ProjectMatrix = matrixRows;
        vm.OfficialTasks = tasks;
        vm.Ideas = OrderIdeas(ideas);
        vm.PersonalReminders = reminders;
        vm.RecordHealth = health.Values
            .OrderBy(item => item.HealthPercent)
            .ThenBy(item => item.ProjectName)
            .ToList();
        vm.RecordGapCount = health.Values.Sum(item => item.GapDetails.Count);
        vm.UpcomingEvents = upcomingEvents.Items;
        vm.UpcomingEventCount = upcomingEvents.TotalCount;
    }

    // SECTION: Procurement editors for the desk
    // Loaded only for projects with a procurement gap, and built by the same factory as
    // Project Overview so the desk panel has identical locks and ARPP messages.
    private async Task<IReadOnlyDictionary<int, ProcurementEditVm>> LoadProcurementEditorsAsync(
        IReadOnlyList<Project> projects,
        IReadOnlyDictionary<int, WorkspaceRecordHealthVm> health,
        CancellationToken ct)
    {
        var projectIds = health.Values
            .Where(item => item.GapDetails.Any(gap => BriefingDeskBuilder.IsProcurementEditor(gap.ActionUrl)))
            .Select(item => item.ProjectId)
            .ToArray();
        if (projectIds.Length == 0)
        {
            return new Dictionary<int, ProcurementEditVm>();
        }

        var procurement = await _procurement.GetManyAsync(projectIds, ct);
        var projectsById = projects.ToDictionary(project => project.Id);
        var editors = new Dictionary<int, ProcurementEditVm>();
        foreach (var projectId in projectIds)
        {
            if (!projectsById.TryGetValue(projectId, out var project))
            {
                continue;
            }

            var completed = project.ProjectStages
                .Where(stage => stage.Status == StageStatus.Completed && !string.IsNullOrWhiteSpace(stage.StageCode))
                .Select(stage => stage.StageCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var anchor = $"desk-{BriefingDeskLabels.KindKey(ConferenceItemKind.Project)}-{projectId}";
            editors[projectId] = ProcurementEditVmFactory.Create(
                projectId,
                procurement.GetValueOrDefault(projectId) ?? ProcurementAtAGlanceVm.Empty,
                completed.Contains,
                returnUrl: WorkspaceRouteHelper.BriefingDeskItem(anchor),
                idPrefix: $"{anchor}-");
        }

        return editors;
    }

    // SECTION: Plan editor state for the desk's current-stage form
    // Read with the same PlanReadService as Project Overview, and only for projects whose
    // current-stage PDC is missing or overdue with nothing already awaiting approval.
    private async Task<IReadOnlyDictionary<int, ProjectManagement.ViewModels.PlanEditorVm>> LoadPlanEditorsAsync(
        string userId,
        IReadOnlyList<WorkspaceProjectMatrixRowVm> rows,
        IReadOnlyDictionary<int, DateTimeOffset> pendingPlans,
        CancellationToken ct)
    {
        var editors = new Dictionary<int, ProjectManagement.ViewModels.PlanEditorVm>();
        foreach (var row in rows)
        {
            var hasStage = !string.IsNullOrWhiteSpace(row.CurrentStageCode) && row.CurrentStageCode != "—";
            var needsPdc = row.HasOverdueCurrentStage || row.CurrentStagePdc is null;
            if (!hasStage || !needsPdc || pendingPlans.ContainsKey(row.ProjectId))
            {
                continue;
            }

            editors[row.ProjectId] = await _planRead.GetAsync(row.ProjectId, userId, ct);
        }

        return editors;
    }

    // SECTION: Work the Project Officer has submitted and is waiting on
    private async Task<BriefingDeskPendingApprovals> LoadPendingApprovalsAsync(
        string userId,
        IReadOnlyList<Project> projects,
        IReadOnlyList<WorkspaceTaskVm> tasks,
        CancellationToken ct)
    {
        var projectNames = projects.ToDictionary(project => project.Id, project => project.Name);
        var projectIds = projectNames.Keys.ToArray();
        var waiting = new List<BriefingDeskWaitingVm>();

        // Plan and actual-date corrections are project-scoped: a pending submission answers
        // the current-stage check on the card whoever submitted it. Plans use the canonical
        // actionable-pending rule, so a superseded or withdrawn submission never counts.
        var pendingPlans = await PlanRevisionConsistency.ActionablePending(_db)
            .AsNoTracking()
            .Where(plan => projectIds.Contains(plan.ProjectId))
            .Select(plan => new { plan.ProjectId, plan.SubmittedOn, plan.CreatedOn })
            .ToListAsync(ct);
        var planSince = pendingPlans
            .GroupBy(plan => plan.ProjectId)
            .ToDictionary(group => group.Key, group => group.Min(plan => plan.SubmittedOn ?? plan.CreatedOn));
        waiting.AddRange(planSince.Select(entry => new BriefingDeskWaitingVm(
            "Timeline plan",
            projectNames[entry.Key],
            "HoD",
            entry.Value,
            WorkspaceRouteHelper.ProjectTimeline(entry.Key))));

        var pendingActuals = await _db.ActualCorrectionRevisions
            .AsNoTracking()
            .Where(revision => projectIds.Contains(revision.ProjectId) && revision.Status == ActualCorrectionRevisionStatus.Pending)
            .Select(revision => new { revision.ProjectId, revision.SubmittedOn })
            .ToListAsync(ct);
        var actualsSince = pendingActuals
            .GroupBy(revision => revision.ProjectId)
            .ToDictionary(group => group.Key, group => group.Min(revision => revision.SubmittedOn));
        waiting.AddRange(actualsSince.Select(entry => new BriefingDeskWaitingVm(
            "Date correction",
            projectNames[entry.Key],
            "HoD",
            entry.Value,
            WorkspaceRouteHelper.ProjectTimelineActuals(entry.Key))));

        var stageRequests = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(request => request.RequestedByUserId == userId && request.DecisionStatus == "Pending")
            .Select(request => new { request.ProjectId, request.StageCode, request.RequestedOn })
            .ToListAsync(ct);
        waiting.AddRange(stageRequests.Select(request => new BriefingDeskWaitingVm(
            $"{request.StageCode} stage change",
            projectNames.GetValueOrDefault(request.ProjectId) ?? "Project",
            "HoD",
            request.RequestedOn,
            WorkspaceRouteHelper.ProjectTimeline(request.ProjectId))));

        var metaRequests = await _db.ProjectMetaChangeRequests
            .AsNoTracking()
            .Where(request => request.RequestedByUserId == userId && request.DecisionStatus == "Pending")
            .Select(request => new { request.ProjectId, request.RequestedOnUtc })
            .ToListAsync(ct);
        waiting.AddRange(metaRequests.Select(request => new BriefingDeskWaitingVm(
            "Project details change",
            projectNames.GetValueOrDefault(request.ProjectId) ?? "Project",
            "HoD",
            request.RequestedOnUtc,
            WorkspaceRouteHelper.ProjectOverview(request.ProjectId))));

        var documentRequests = await _db.ProjectDocumentRequests
            .AsNoTracking()
            .Where(request => request.RequestedByUserId == userId && request.Status == ProjectDocumentRequestStatus.Submitted)
            .Select(request => new { request.ProjectId, request.Title, request.RequestedAtUtc })
            .ToListAsync(ct);
        waiting.AddRange(documentRequests.Select(request => new BriefingDeskWaitingVm(
            $"Document: {request.Title}",
            projectNames.GetValueOrDefault(request.ProjectId) ?? "Project",
            "HoD",
            request.RequestedAtUtc,
            WorkspaceRouteHelper.ProjectDocuments(request.ProjectId))));

        waiting.AddRange(tasks
            .Where(task => string.Equals(task.Status, ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase))
            .Select(task => new BriefingDeskWaitingVm(
                task.Title,
                "Submitted for closure",
                "assigner",
                new DateTimeOffset(DateTime.SpecifyKind(task.SubmittedOnUtc ?? _clock.UtcNow, DateTimeKind.Utc)),
                task.OpenUrl)));

        return new BriefingDeskPendingApprovals(waiting, planSince, actualsSince);
    }

    // Latest progress the PO posted on each task; conference directions are not progress.
    private async Task<IReadOnlyDictionary<int, DateTime>> LoadLastTaskProgressAsync(
        string userId,
        IReadOnlyList<WorkspaceTaskVm> tasks,
        CancellationToken ct)
    {
        var taskIds = tasks.Select(task => task.TaskId).ToArray();
        if (taskIds.Length == 0)
        {
            return new Dictionary<int, DateTime>();
        }

        var rows = await _db.ActionTaskUpdates
            .AsNoTracking()
            .Where(update =>
                taskIds.Contains(update.TaskId)
                && !update.IsDeleted
                && update.CreatedByUserId == userId
                && update.UpdateType != ActionTaskUpdateTypes.Conference)
            .GroupBy(update => update.TaskId)
            .Select(group => new { TaskId = group.Key, Last = group.Max(update => update.CreatedAtUtc) })
            .ToListAsync(ct);

        return rows.ToDictionary(row => row.TaskId, row => row.Last);
    }

    private sealed record BriefingDeskPendingApprovals(
        IReadOnlyList<BriefingDeskWaitingVm> Waiting,
        IReadOnlyDictionary<int, DateTimeOffset> PlanSinceByProject,
        IReadOnlyDictionary<int, DateTimeOffset> ActualsSinceByProject);

    private async Task<ProjectOfficerOperationalContext> LoadOperationalContextAsync(
        string userId,
        DateOnly today,
        IReadOnlyList<Project> projects,
        IReadOnlyList<WorkspaceTaskVm> tasks,
        IReadOnlyList<WorkspaceIdeaVm> ideas,
        IReadOnlyList<WorkspaceProjectMatrixRowVm> projectRows,
        int aotsUnreadCount,
        CancellationToken ct)
    {
        var remarksDue = _nudges.BuildRemarksDue(projects, userId, today).ToList();
        var returnedItems = await BuildReturnedItemsAsync(userId, ct);
        var officialTasksDue = tasks
            .Where(task => task.IsOverdue || IsDueSoon(task.DueDate, today))
            .OrderByDescending(task => task.IsOverdue)
            .ThenBy(task => task.DueDate)
            .ToList();
        var ideasNeedingUpdate = ideas
            .Where(idea => idea.NeedsUpdate)
            .OrderBy(idea => idea.LastActivityAtUtc)
            .ToList();
        var aotsDocuments = await LoadUnreadAotsDocumentsAsync(userId, ct);
        var pendingConferenceDirections = await _conferenceActions.GetPendingAsync(
            userId,
            projects.ToDictionary(project => project.Id, project => project.Name),
            ideas.ToDictionary(idea => idea.IdeaId, idea => idea.Title),
            tasks.ToDictionary(task => task.TaskId, task => task.Title),
            ct);
        var actionQueue = WorkspaceActionQueueBuilder.Build(
            returnedItems,
            officialTasksDue,
            remarksDue,
            ideasNeedingUpdate,
            aotsDocuments,
            aotsUnreadCount,
            projectRows,
            pendingConferenceDirections);

        return new ProjectOfficerOperationalContext(
            actionQueue,
            remarksDue,
            returnedItems,
            officialTasksDue,
            ideasNeedingUpdate,
            aotsDocuments,
            pendingConferenceDirections);
    }

    private static void ApplyOperationalContext(
        ProjectOfficerWorkspaceVm vm,
        ProjectOfficerOperationalContext operational)
    {
        var queue = operational.ActionQueue;
        vm.ActionQueue = queue.Items;
        vm.ActionQueueGroups = queue.Groups;
        vm.AllActionQueue = queue.AllItems;
        vm.AllActionQueueGroups = queue.AllGroups;
        vm.ActionQueueTotalCount = queue.TotalCount;
        vm.NavigationActionCount = queue.TotalCount;
        vm.DailyActionCount = queue.TotalCount;
        vm.ActionSummary = queue.Summary;
        vm.ActionProjectCount = queue.Summary.ProjectCount;
        vm.ActionIdeaCount = queue.Summary.IdeaCount;
        vm.ActionTaskCount = queue.Summary.TaskCount;
        vm.PendingConferenceDirectionCount = queue.Summary.ConferenceDirectionCount;
        vm.RemarksDueCount = operational.RemarksDue.Count;
        vm.OfficialTasksDue = operational.OfficialTasksDue;
        vm.IdeasNeedingUpdate = operational.IdeasNeedingUpdate;
        vm.IdeasNeedingUpdateCount = operational.IdeasNeedingUpdate.Count;
        vm.AotsDocuments = operational.AotsDocuments;
        vm.ReturnedItems = operational.ReturnedItems.Take(5).ToList();
    }

    private IReadOnlyList<WorkspaceProjectMatrixRowVm> BuildProjectMatrix(
        IReadOnlyList<Project> projects,
        IReadOnlyDictionary<int, WorkspaceRecordHealthVm> health,
        string userId,
        DateOnly today)
        => projects
            .Select(project => BuildMatrixRow(project, health[project.Id], userId, today))
            .OrderBy(ProjectAttentionRank)
            .ThenByDescending(row => row.RecordGapCount)
            .ThenByDescending(row => row.DaysInCurrentStage ?? 0)
            .ThenBy(row => row.ProjectName)
            .ToList();

    private static IReadOnlyList<WorkspaceIdeaVm> OrderIdeas(IReadOnlyList<WorkspaceIdeaVm> ideas)
        => ideas
            .OrderByDescending(idea => idea.NeedsUpdate)
            .ThenByDescending(idea => idea.LastActivityAtUtc)
            .ToList();

    private static string HealthSummaryLabel(int projectCount, int averageHealth)
        => projectCount == 0
            ? "Not applicable"
            : averageHealth >= 80
                ? "Good"
                : averageHealth >= 60
                    ? "Attention"
                    : "Needs Work";

    private async Task<ProjectOfficerNavigationSummary> LoadNavigationSummaryAsync(
        string userId,
        DateOnly today,
        bool includeActionCount,
        CancellationToken ct)
    {
        var cacheKey = NavigationActionCountCacheKey(userId);
        var cachedActionCount = 0;
        var hasCachedActionCount = includeActionCount
            && _cache.TryGetValue(cacheKey, out cachedActionCount);
        var calculateActionCount = includeActionCount && !hasCachedActionCount;

        IReadOnlyList<Project>? projects = null;
        IReadOnlyList<WorkspaceTaskVm>? tasks = null;

        int assignedProjectCount;
        if (calculateActionCount)
        {
            projects = await LoadAssignedProjectsAsync(userId, ct);
            assignedProjectCount = projects.Count;
        }
        else
        {
            assignedProjectCount = await _db.Projects
                .AsNoTracking()
                .CountAsync(project =>
                    project.LeadPoUserId == userId
                    && !project.IsDeleted
                    && !project.IsArchived
                    && project.LifecycleStatus == ProjectLifecycleStatus.Active,
                    ct);
        }

        int assignedTaskCount;
        if (calculateActionCount)
        {
            tasks = await LoadOtherAssignedTasksAsync(userId, today, ct);
            assignedTaskCount = tasks.Count;
        }
        else
        {
            assignedTaskCount = await _db.ActionTasks
                .AsNoTracking()
                .CountAsync(task =>
                    !task.IsDeleted
                    && task.AssignedToUserId == userId
                    && task.Status != ActionTaskStatuses.Closed
                    && task.Status != ActionTaskStatuses.Backlog,
                    ct);
        }

        var ideaSummaries = await LoadProjectIdeaProjectionsAsync(userId, ct);
        var ideas = ideaSummaries.Select(idea => BuildWorkspaceIdeaVm(idea, today)).ToList();
        var assignedIdeaCount = ideas.Count;
        var endTodayUtc = EndOfIstOperatingDayUtc();
        var reminderFollowUpCount = await _db.NotebookItems
            .AsNoTracking()
            .CountAsync(item =>
                item.OwnerId == userId
                && item.DeletedAtUtc == null
                && item.Status == NotebookItemStatus.Active
                && (item.IsPinned || (item.ReminderAtUtc != null && item.ReminderAtUtc < endTodayUtc)),
                ct);

        var aotsUnreadCount = await _aotsUnreadService.GetUnreadCountAsync(userId, ct);
        int? actionCount = hasCachedActionCount ? cachedActionCount : null;

        if (calculateActionCount && projects is not null && tasks is not null)
        {
            actionCount = await CalculateNavigationActionCountAsync(
                userId,
                today,
                projects,
                tasks,
                ideas,
                aotsUnreadCount,
                ct);
            CacheNavigationActionCount(userId, actionCount.Value);
        }

        return new ProjectOfficerNavigationSummary(
            assignedProjectCount,
            assignedTaskCount,
            assignedIdeaCount,
            reminderFollowUpCount,
            aotsUnreadCount,
            actionCount);
    }

    private async Task<int> CalculateNavigationActionCountAsync(
        string userId,
        DateOnly today,
        IReadOnlyList<Project> projects,
        IReadOnlyList<WorkspaceTaskVm> tasks,
        IReadOnlyList<WorkspaceIdeaVm> ideas,
        int aotsUnreadCount,
        CancellationToken ct)
    {
        var projectRows = projects.Select(project => BuildActionProjectRow(project, userId, today)).ToList();
        var remarksDue = _nudges.BuildRemarksDue(projects, userId, today).ToList();
        var returnedItems = await BuildReturnedItemsAsync(userId, ct);
        var officialTasksDue = tasks
            .Where(task => task.IsOverdue || IsDueSoon(task.DueDate, today))
            .ToList();
        var ideasNeedingUpdate = ideas.Where(idea => idea.NeedsUpdate).ToList();
        var aotsDocuments = await LoadUnreadAotsDocumentsAsync(userId, ct);
        var conferenceDirections = await _conferenceActions.GetPendingAsync(
            userId,
            projects.ToDictionary(project => project.Id, project => project.Name),
            ideas.ToDictionary(idea => idea.IdeaId, idea => idea.Title),
            tasks.ToDictionary(task => task.TaskId, task => task.Title),
            ct);

        return WorkspaceActionQueueBuilder.Build(
            returnedItems,
            officialTasksDue,
            remarksDue,
            ideasNeedingUpdate,
            aotsDocuments,
            aotsUnreadCount,
            projectRows,
            conferenceDirections).TotalCount;
    }

    private static string NavigationActionCountCacheKey(string userId)
        => $"workspace:project-officer:{userId}:action-count";

    private void CacheNavigationActionCount(string userId, int count)
        => _cache.Set(
            NavigationActionCountCacheKey(userId),
            count,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
            });

    private DateTimeOffset EndOfIstOperatingDayUtc()
    {
        var endTodayLocal = DateTime.SpecifyKind(_clock.IstToday.AddDays(1), DateTimeKind.Unspecified);
        return new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(endTodayLocal, IstClock.TimeZone),
            TimeSpan.Zero);
    }

    private sealed record ProjectOfficerNavigationSummary(
        int AssignedProjectCount,
        int AssignedTaskCount,
        int AssignedIdeaCount,
        int FollowUpCount,
        int AotsUnreadCount,
        int? ActionCount);

    private sealed record ProjectOfficerOperationalContext(
        WorkspaceActionQueueBuildResult ActionQueue,
        IReadOnlyList<WorkspaceAttentionItemVm> RemarksDue,
        IReadOnlyList<WorkspaceAttentionItemVm> ReturnedItems,
        IReadOnlyList<WorkspaceTaskVm> OfficialTasksDue,
        IReadOnlyList<WorkspaceIdeaVm> IdeasNeedingUpdate,
        IReadOnlyList<WorkspaceAotsDocumentVm> AotsDocuments,
        IReadOnlyList<WorkspaceConferenceDirectionActionVm> ConferenceDirections);

    // SECTION: Assigned projects include current-stage context for the workspace matrix.
    private async Task<IReadOnlyList<Project>> LoadAssignedProjectsAsync(string userId, CancellationToken ct)
    {
        return await _db.Projects
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.ProjectStages)
            .Include(p => p.Remarks)
            .Where(p =>
                p.LeadPoUserId == userId &&
                !p.IsDeleted &&
                !p.IsArchived &&
                p.LifecycleStatus == ProjectLifecycleStatus.Active)
            .OrderBy(p => p.Name)
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

    // SECTION: Project-idea projections avoid loading three child collections for count-only workspace views.
    private async Task<IReadOnlyList<ProjectIdeaProjection>> LoadProjectIdeaProjectionsAsync(
        string userId,
        CancellationToken ct)
    {
        return await _db.ProjectIdeas
            .AsNoTracking()
            .Where(idea =>
                !idea.IsDeleted
                && idea.AssignedProjectOfficerUserId == userId
                && idea.Status != ProjectIdeaStatuses.Archived)
            .OrderByDescending(idea => idea.UpdatedAt)
            .Select(idea => new ProjectIdeaProjection(
                idea.Id,
                idea.Title,
                idea.Status,
                idea.UpdatedAt,
                idea.Comments
                    .Where(comment => !comment.IsDeleted)
                    .Select(comment => (DateTime?)comment.CreatedAt)
                    .Max(),
                idea.Notes
                    .Where(note => !note.IsDeleted)
                    .Select(note => (DateTime?)note.UpdatedAt)
                    .Max(),
                idea.Documents
                    .Where(document => !document.IsDeleted)
                    .Select(document => (DateTime?)document.UploadedAt)
                    .Max(),
                idea.Comments.Count(comment => !comment.IsDeleted),
                idea.Documents.Count(document => !document.IsDeleted)))
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<WorkspaceIdeaVm>> LoadProjectIdeasAsync(
        string userId,
        DateOnly today,
        CancellationToken ct)
    {
        var ideas = await LoadProjectIdeaProjectionsAsync(userId, ct);
        return ideas.Select(idea => BuildWorkspaceIdeaVm(idea, today)).ToList();
    }

    // SECTION: Follow-ups contain reminders that are due or intentionally pinned by the officer.
    private async Task<IReadOnlyList<WorkspaceReminderVm>> LoadPersonalRemindersAsync(
        string userId,
        CancellationToken ct)
    {
        var endTodayUtc = EndOfIstOperatingDayUtc();

        return await _db.NotebookItems
            .AsNoTracking()
            .Where(item =>
                item.OwnerId == userId
                && item.DeletedAtUtc == null
                && item.Status == NotebookItemStatus.Active
                && (item.IsPinned || (item.ReminderAtUtc != null && item.ReminderAtUtc < endTodayUtc)))
            .OrderByDescending(item => item.IsPinned)
            .ThenBy(item => item.ReminderAtUtc)
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

    // SECTION: Task due dates are date-only business values; no artificial UTC conversion is applied.
    private static WorkspaceTaskVm BuildWorkspaceTaskVm(
        ActionTaskItem row,
        HashSet<int> overdueTaskIds,
        DateOnly today)
    {
        var dueDate = DateOnly.FromDateTime(row.DueDate);
        var isOverdue = overdueTaskIds.Contains(row.Id);
        var daysOverdue = isOverdue
            ? Math.Max(0, today.DayNumber - dueDate.DayNumber)
            : (int?)null;
        var daysUntilDue = dueDate.DayNumber - today.DayNumber;

        return new WorkspaceTaskVm
        {
            TaskId = row.Id,
            Title = row.Title,
            ContextLabel = "Other assigned task",
            Priority = row.Priority,
            Status = row.Status,
            DueDate = dueDate,
            IsOverdue = isOverdue,
            IsDueSoon = !isOverdue && daysUntilDue is >= 0 and <= 3,
            DaysOverdue = daysOverdue,
            OpenUrl = WorkspaceRouteHelper.ActionTask(row.Id),
            RowVersion = Convert.ToBase64String(row.RowVersion ?? Array.Empty<byte>()),
            SubmittedOnUtc = row.SubmittedOn
        };
    }

    // SECTION: Idea activity is computed from the projected latest child timestamps.
    private static WorkspaceIdeaVm BuildWorkspaceIdeaVm(ProjectIdeaProjection idea, DateOnly today)
    {
        var lastActivity = Latest(
            idea.UpdatedAt,
            idea.LastCommentAt,
            idea.LastNoteAt,
            idea.LastDocumentAt);

        return new WorkspaceIdeaVm
        {
            IdeaId = idea.Id,
            Title = idea.Title,
            Status = ProjectIdeaStatuses.ToDisplay(idea.Status),
            LastActivityAtUtc = lastActivity,
            NeedsUpdate = IsIdeaUpdateDue(idea, today),
            CommentCount = idea.CommentCount,
            DocumentCount = idea.DocumentCount,
            OpenUrl = WorkspaceRouteHelper.ProjectIdea(idea.Id)
        };
    }

    private static bool IsIdeaUpdateDue(ProjectIdeaProjection idea, DateOnly today)
    {
        if (idea.Status != ProjectIdeaStatuses.Active && idea.Status != ProjectIdeaStatuses.OnHold)
        {
            return false;
        }

        var lastActivity = Latest(
            idea.UpdatedAt,
            idea.LastCommentAt,
            idea.LastNoteAt,
            idea.LastDocumentAt);
        return today.DayNumber - WorkspaceNudgeService.ToIstDate(lastActivity).DayNumber > 15;
    }

    private static DateTime Latest(DateTime baseline, params DateTime?[] candidates)
        => candidates
            .Where(candidate => candidate.HasValue)
            .Select(candidate => candidate!.Value)
            .Append(baseline)
            .Max();

    // SECTION: Due-soon detection uses the workspace operating date without timezone coercion.
    private static bool IsDueSoon(DateOnly dueDate, DateOnly today)
    {
        var days = dueDate.DayNumber - today.DayNumber;
        return days is >= 0 and <= 3;
    }

    private sealed record ProjectIdeaProjection(
        int Id,
        string Title,
        string Status,
        DateTime UpdatedAt,
        DateTime? LastCommentAt,
        DateTime? LastNoteAt,
        DateTime? LastDocumentAt,
        int CommentCount,
        int DocumentCount);

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


    // SECTION: Personal document hub reuses canonical repository records.
    private async Task<WorkspaceDocumentHubVm> LoadDocumentHubAsync(
        string userId,
        int aotsUnreadCount,
        CancellationToken ct)
    {
        const int previewLimit = 12;
        var returnUrl = WorkspaceRouteHelper.ProjectOfficerWorkspace("documents");

        var favouriteRows = await (
                from favourite in _db.DocRepoFavourites.AsNoTracking()
                join document in _db.Documents.AsNoTracking() on favourite.DocumentId equals document.Id
                where favourite.UserId == userId
                      && !document.IsDeleted
                      && !document.IsExternal
                      && document.IsActive
                orderby favourite.CreatedAtUtc descending
                select new
                {
                    document.Id,
                    document.Subject,
                    document.DocumentDate,
                    document.CreatedAtUtc,
                    document.IsAots,
                    Office = document.OfficeCategory.Name,
                    Category = document.DocumentCategory.Name
                })
            .Take(previewLimit)
            .ToListAsync(ct);

        var favouriteCount = await (
                from favourite in _db.DocRepoFavourites.AsNoTracking()
                join document in _db.Documents.AsNoTracking() on favourite.DocumentId equals document.Id
                where favourite.UserId == userId
                      && !document.IsDeleted
                      && !document.IsExternal
                      && document.IsActive
                select document.Id)
            .CountAsync(ct);

        var viewedRows = await _db.DocRepoAudits
            .AsNoTracking()
            .Where(audit =>
                audit.ActorUserId == userId
                && audit.DocumentId.HasValue
                && audit.EventType == DocRepoAuditEventTypes.Viewed)
            .GroupBy(audit => audit.DocumentId!.Value)
            .Select(group => new
            {
                DocumentId = group.Key,
                LastViewedAtUtc = group.Max(audit => audit.OccurredAtUtc)
            })
            .OrderByDescending(row => row.LastViewedAtUtc)
            .Take(previewLimit)
            .ToListAsync(ct);

        var recentCount = await (
                from audit in _db.DocRepoAudits.AsNoTracking()
                join document in _db.Documents.AsNoTracking() on audit.DocumentId equals (Guid?)document.Id
                where audit.ActorUserId == userId
                      && audit.EventType == DocRepoAuditEventTypes.Viewed
                      && !document.IsDeleted
                      && !document.IsExternal
                      && document.IsActive
                select document.Id)
            .Distinct()
            .CountAsync(ct);

        var viewedIds = viewedRows.Select(row => row.DocumentId).ToArray();
        var viewedDocuments = viewedIds.Length == 0
            ? new Dictionary<Guid, WorkspaceDocumentProjection>()
            : (await _db.Documents
                .AsNoTracking()
                .Where(document =>
                    viewedIds.Contains(document.Id)
                    && !document.IsDeleted
                    && !document.IsExternal
                    && document.IsActive)
                .Select(document => new WorkspaceDocumentProjection(
                    document.Id,
                    document.Subject,
                    document.DocumentDate,
                    document.CreatedAtUtc,
                    document.IsAots,
                    document.OfficeCategory.Name,
                    document.DocumentCategory.Name))
                .ToListAsync(ct))
                .ToDictionary(document => document.Id);

        var recent = viewedRows
            .Where(row => viewedDocuments.ContainsKey(row.DocumentId))
            .Select(row =>
            {
                var document = viewedDocuments[row.DocumentId];
                return new WorkspaceDocumentVm
                {
                    DocumentId = document.Id,
                    Subject = document.Subject,
                    Office = document.Office,
                    Category = document.Category,
                    DocumentDate = document.DocumentDate,
                    CreatedAtUtc = document.CreatedAtUtc,
                    LastViewedAtUtc = row.LastViewedAtUtc,
                    IsAots = document.IsAots,
                    OpenUrl = WorkspaceRouteHelper.DocumentReader(document.Id, returnUrl)
                };
            })
            .ToList();

        var aotsRows = await _db.Documents
            .AsNoTracking()
            .Where(document =>
                document.IsAots
                && !document.IsDeleted
                && !document.IsExternal
                && document.IsActive)
            .OrderByDescending(document => document.DocumentDate.HasValue)
            .ThenByDescending(document => document.DocumentDate)
            .ThenByDescending(document => document.CreatedAtUtc)
            .Take(previewLimit)
            .Select(document => new
            {
                document.Id,
                document.Subject,
                document.DocumentDate,
                document.CreatedAtUtc,
                Office = document.OfficeCategory.Name,
                Category = document.DocumentCategory.Name,
                IsSeen = _db.DocRepoAotsViews.Any(view => view.DocumentId == document.Id && view.UserId == userId)
            })
            .ToListAsync(ct);

        var uploadedRows = await _db.Documents
            .AsNoTracking()
            .Where(document =>
                document.CreatedByUserId == userId
                && !document.IsDeleted
                && !document.IsExternal
                && document.IsActive)
            .OrderByDescending(document => document.CreatedAtUtc)
            .Take(previewLimit)
            .Select(document => new
            {
                document.Id,
                document.Subject,
                document.DocumentDate,
                document.CreatedAtUtc,
                document.IsAots,
                Office = document.OfficeCategory.Name,
                Category = document.DocumentCategory.Name
            })
            .ToListAsync(ct);

        var uploadedCount = await _db.Documents
            .AsNoTracking()
            .CountAsync(document =>
                document.CreatedByUserId == userId
                && !document.IsDeleted
                && !document.IsExternal
                && document.IsActive, ct);

        return new WorkspaceDocumentHubVm
        {
            FavouriteCount = favouriteCount,
            AotsUnreadCount = aotsUnreadCount,
            RecentCount = recentCount,
            UploadedByMeCount = uploadedCount,
            Favourites = favouriteRows.Select(row => new WorkspaceDocumentVm
            {
                DocumentId = row.Id, Subject = row.Subject, Office = row.Office, Category = row.Category,
                DocumentDate = row.DocumentDate, CreatedAtUtc = row.CreatedAtUtc, IsAots = row.IsAots, IsFavourite = true,
                OpenUrl = WorkspaceRouteHelper.DocumentReader(row.Id, returnUrl)
            }).ToList(),
            Aots = aotsRows.Select(row => new WorkspaceDocumentVm
            {
                DocumentId = row.Id, Subject = row.Subject, Office = row.Office, Category = row.Category,
                DocumentDate = row.DocumentDate, CreatedAtUtc = row.CreatedAtUtc, IsAots = true, IsUnreadAots = !row.IsSeen,
                OpenUrl = WorkspaceRouteHelper.DocumentReader(row.Id, returnUrl)
            }).ToList(),
            Recent = recent,
            UploadedByMe = uploadedRows.Select(row => new WorkspaceDocumentVm
            {
                DocumentId = row.Id, Subject = row.Subject, Office = row.Office, Category = row.Category,
                DocumentDate = row.DocumentDate, CreatedAtUtc = row.CreatedAtUtc, IsAots = row.IsAots,
                OpenUrl = WorkspaceRouteHelper.DocumentReader(row.Id, returnUrl)
            }).ToList()
        };
    }

    private sealed record WorkspaceDocumentProjection(
        Guid Id,
        string Subject,
        DateOnly? DocumentDate,
        DateTime CreatedAtUtc,
        bool IsAots,
        string Office,
        string Category);

    // SECTION: Returned items are surfaced as actionable corrections for the PO.
    // The action queue and side list show a short preview (30 days, a few per type). The
    // briefing desk does not list them: a return is a finished HoD decision.
    private async Task<IReadOnlyList<WorkspaceAttentionItemVm>> BuildReturnedItemsAsync(string userId, CancellationToken ct)
    {
        const int fetchLimit = 10;
        const int keepLimit = 5;
        const int totalLimit = 12;
        var items = new List<WorkspaceAttentionItemVm>();
        var returnedCutoffUtc = DateTimeOffset.UtcNow.AddDays(-30);
        var rejectedPlans = await _db.PlanVersions.AsNoTracking().Include(p => p.Project).Where(p => (p.CreatedByUserId == userId || p.SubmittedByUserId == userId) && p.RejectedOn.HasValue && p.RejectedOn >= returnedCutoffUtc).OrderByDescending(p => p.RejectedOn).Take(fetchLimit).ToListAsync(ct);
        var rejectedPlanProjectIds = rejectedPlans.Select(p => p.ProjectId).ToArray();
        var newerPlanProjects = await _db.PlanVersions.AsNoTracking().Where(p => rejectedPlanProjectIds.Contains(p.ProjectId) && (p.Status == PlanVersionStatus.PendingApproval || p.Status == PlanVersionStatus.Approved)).Select(p => new { p.ProjectId, EventAt = p.SubmittedOn ?? p.ApprovedOn ?? p.CreatedOn }).ToListAsync(ct);
        rejectedPlans = rejectedPlans.Where(p => !newerPlanProjects.Any(n => n.ProjectId == p.ProjectId && p.RejectedOn.HasValue && n.EventAt > p.RejectedOn.Value)).Take(keepLimit).ToList();
        items.AddRange(rejectedPlans.Select(p => new WorkspaceAttentionItemVm
        {
            ProjectId = p.ProjectId,
            WorkItemKey = $"project:{p.ProjectId}",
            Type = "Timeline",
            Title = p.Project?.Name ?? "Timeline plan",
            Detail = "Timeline plan returned for correction",
            Severity = "Danger",
            BadgeText = "Returned",
            ActionText = "Correct",
            ActionUrl = WorkspaceRouteHelper.ProjectPlanEdit(p.ProjectId),
            DueOrEventDateUtc = p.RejectedOn?.UtcDateTime,
            ReviewerNote = p.RejectionNote
        }));

        var rejectedStages = await _db.StageChangeRequests.AsNoTracking().Where(r => r.RequestedByUserId == userId && r.DecisionStatus == "Rejected" && r.DecidedOn.HasValue && r.DecidedOn.Value >= returnedCutoffUtc).OrderByDescending(r => r.DecidedOn).Take(fetchLimit).ToListAsync(ct);
        var rejectedStageProjectIds = rejectedStages.Select(r => r.ProjectId).ToArray();
        var newerStageProjects = await _db.StageChangeRequests.AsNoTracking().Where(r => rejectedStageProjectIds.Contains(r.ProjectId) && (r.DecisionStatus == "Pending" || r.DecisionStatus == "Approved")).Select(r => new { r.ProjectId, r.StageCode, EventAt = r.DecidedOn ?? r.RequestedOn }).ToListAsync(ct);
        // Only a newer request for the same stage resolves a returned one.
        rejectedStages = rejectedStages.Where(r => !newerStageProjects.Any(n => n.ProjectId == r.ProjectId && string.Equals(n.StageCode, r.StageCode, StringComparison.OrdinalIgnoreCase) && r.DecidedOn.HasValue && n.EventAt > r.DecidedOn.Value)).Take(keepLimit).ToList();
        items.AddRange(rejectedStages.Select(r => new WorkspaceAttentionItemVm
        {
            ProjectId = r.ProjectId,
            WorkItemKey = $"project:{r.ProjectId}",
            Type = "Stage",
            Title = r.StageCode,
            Detail = "Stage update returned by HoD",
            Severity = "Danger",
            BadgeText = "Returned",
            ActionText = "Correct",
            ActionUrl = WorkspaceRouteHelper.ProjectTimeline(r.ProjectId),
            DueOrEventDateUtc = r.DecidedOn?.UtcDateTime,
            ReviewerNote = r.DecisionNote
        }));

        var rejectedMeta = await _db.ProjectMetaChangeRequests.AsNoTracking().Include(r => r.Project).Where(r => r.RequestedByUserId == userId && r.DecisionStatus == "Rejected" && r.DecidedOnUtc.HasValue && r.DecidedOnUtc.Value >= returnedCutoffUtc).OrderByDescending(r => r.DecidedOnUtc).Take(fetchLimit).ToListAsync(ct);
        var rejectedMetaProjectIds = rejectedMeta.Select(r => r.ProjectId).ToArray();
        var newerMetaProjects = await _db.ProjectMetaChangeRequests.AsNoTracking().Where(r => rejectedMetaProjectIds.Contains(r.ProjectId) && (r.DecisionStatus == "Pending" || r.DecisionStatus == "Approved")).Select(r => new { r.ProjectId, EventAt = r.DecidedOnUtc ?? r.RequestedOnUtc }).ToListAsync(ct);
        rejectedMeta = rejectedMeta.Where(r => !newerMetaProjects.Any(n => n.ProjectId == r.ProjectId && r.DecidedOnUtc.HasValue && n.EventAt > r.DecidedOnUtc.Value)).Take(keepLimit).ToList();
        items.AddRange(rejectedMeta.Select(r => new WorkspaceAttentionItemVm
        {
            ProjectId = r.ProjectId,
            WorkItemKey = $"project:{r.ProjectId}",
            Type = "Metadata",
            Title = r.Project?.Name ?? "Metadata change",
            Detail = "Project details update returned by HoD",
            Severity = "Danger",
            BadgeText = "Returned",
            ActionText = "Correct",
            ActionUrl = WorkspaceRouteHelper.ProjectMetaRequest(r.ProjectId),
            DueOrEventDateUtc = r.DecidedOnUtc?.UtcDateTime,
            ReviewerNote = r.DecisionNote
        }));

        var rejectedDocuments = await _db.ProjectDocumentRequests.AsNoTracking().Where(r => r.RequestedByUserId == userId && r.Status == ProjectDocumentRequestStatus.Rejected && r.ReviewedAtUtc.HasValue && r.ReviewedAtUtc.Value >= returnedCutoffUtc).OrderByDescending(r => r.ReviewedAtUtc).Take(fetchLimit).ToListAsync(ct);
        var rejectedDocumentProjectIds = rejectedDocuments.Select(r => r.ProjectId).ToArray();
        var newerDocumentRequests = await _db.ProjectDocumentRequests.AsNoTracking().Where(r => rejectedDocumentProjectIds.Contains(r.ProjectId) && (r.Status == ProjectDocumentRequestStatus.Submitted || r.Status == ProjectDocumentRequestStatus.Approved)).Select(r => new { r.ProjectId, r.RequestType, r.DocumentId, r.StageId, r.Title, EventAt = r.ReviewedAtUtc ?? r.RequestedAtUtc }).ToListAsync(ct);
        // Document requests are independent, so only a newer request for the same item
        // (same document for a replace or delete, same stage and title for an upload)
        // resolves a returned one; other document activity on the project does not.
        rejectedDocuments = rejectedDocuments.Where(r => !newerDocumentRequests.Any(n =>
                n.ProjectId == r.ProjectId
                && r.ReviewedAtUtc.HasValue
                && n.EventAt > r.ReviewedAtUtc.Value
                && DocumentRequestIdentity(n.RequestType, n.DocumentId, n.StageId, n.Title) == DocumentRequestIdentity(r.RequestType, r.DocumentId, r.StageId, r.Title)))
            .Take(keepLimit)
            .ToList();
        items.AddRange(rejectedDocuments.Select(r => new WorkspaceAttentionItemVm
        {
            ProjectId = r.ProjectId,
            WorkItemKey = $"project:{r.ProjectId}",
            Type = "Document",
            Title = r.Title,
            Detail = "Document request rejected",
            Severity = "Danger",
            BadgeText = "Returned",
            ActionText = "Correct",
            ActionUrl = WorkspaceRouteHelper.ProjectDocumentRequest(r.ProjectId),
            DueOrEventDateUtc = r.ReviewedAtUtc?.UtcDateTime,
            ReviewerNote = r.ReviewerNote
        }));

        // Date corrections the HoD returned, unless a newer one is pending or approved.
        var rejectedActuals = await _db.ActualCorrectionRevisions.AsNoTracking().Include(r => r.Project).Where(r => r.SubmittedByUserId == userId && r.Status == ActualCorrectionRevisionStatus.Rejected && r.ClosedOn.HasValue && r.ClosedOn.Value >= returnedCutoffUtc).OrderByDescending(r => r.ClosedOn).Take(fetchLimit).ToListAsync(ct);
        var rejectedActualProjectIds = rejectedActuals.Select(r => r.ProjectId).ToArray();
        var newerActualProjects = await _db.ActualCorrectionRevisions.AsNoTracking().Where(r => rejectedActualProjectIds.Contains(r.ProjectId) && (r.Status == ActualCorrectionRevisionStatus.Pending || r.Status == ActualCorrectionRevisionStatus.Approved)).Select(r => new { r.ProjectId, EventAt = r.ClosedOn ?? r.SubmittedOn }).ToListAsync(ct);
        rejectedActuals = rejectedActuals.Where(r => !newerActualProjects.Any(n => n.ProjectId == r.ProjectId && r.ClosedOn.HasValue && n.EventAt > r.ClosedOn.Value)).Take(keepLimit).ToList();
        items.AddRange(rejectedActuals.Select(r => new WorkspaceAttentionItemVm
        {
            ProjectId = r.ProjectId,
            WorkItemKey = $"project:{r.ProjectId}",
            Type = "Actuals",
            Title = r.Project?.Name ?? "Date correction",
            Detail = "Date correction returned by HoD",
            Severity = "Danger",
            BadgeText = "Returned",
            ActionText = "Correct",
            ActionUrl = WorkspaceRouteHelper.ProjectTimelineActuals(r.ProjectId),
            DueOrEventDateUtc = r.ClosedOn?.UtcDateTime,
            ReviewerNote = r.DecisionNote
        }));
        return items.OrderByDescending(i => i.DueOrEventDateUtc).Take(totalLimit).ToList();
    }
    internal static string DocumentRequestIdentity(ProjectDocumentRequestType type, int? documentId, int? stageId, string? title)
        => type == ProjectDocumentRequestType.Upload
            ? $"upload|{stageId}|{(title ?? string.Empty).Trim().ToUpperInvariant()}"
            : $"{type}|{documentId}";

    private WorkspaceProjectMatrixRowVm BuildActionProjectRow(Project project, string userId, DateOnly today)
    {
        var neutralHealth = new WorkspaceRecordHealthVm
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            HealthPercent = 100
        };

        return BuildMatrixRow(project, neutralHealth, userId, today);
    }

    private WorkspaceProjectMatrixRowVm BuildMatrixRow(Project p, WorkspaceRecordHealthVm health, string userId, DateOnly today)
    {
        var stage = WorkspaceNudgeService.GetCurrentStage(p);
        var last = WorkspaceNudgeService.LastPoRemark(p, userId);
        var updateStatus = _nudges.GetUpdateStatus(last, today);
        var action = _nudges.GetNextAction(p, health, userId, today, out var url);
        var overdue = _nudges.IsCurrentStageOverdue(stage, today);
        var issue = _nudges.HasCurrentStageTimelineIssue(stage);

        if (!overdue
            && !issue
            && !p.ProjectStages.Any(s => s.Status == StageStatus.Completed && !s.CompletedOn.HasValue)
            && updateStatus is "ActionRequired" or "Attention")
        {
            action = "Add remark";
            url = WorkspaceRouteHelper.ProjectRemarks(p.Id);
        }

        var currentStagePdc = stage?.PlannedDue;
        var daysUntilCurrentStagePdc = currentStagePdc.HasValue
            ? currentStagePdc.Value.DayNumber - today.DayNumber
            : (int?)null;
        var daysSinceLastPoRemark = last.HasValue
            ? Math.Max(0, today.DayNumber - WorkspaceNudgeService.ToIstDate(last.Value).DayNumber)
            : (int?)null;

        return new WorkspaceProjectMatrixRowVm
        {
            ProjectId = p.Id,
            ProjectName = p.Name,
            CurrentStageCode = stage?.StageCode ?? "—",
            CurrentStageName = stage is null ? "Not started" : StageCodes.DisplayNameOf(stage.StageCode),
            DaysInCurrentStage = WorkspaceNudgeService.GetCurrentStageAgeDays(p, today),
            CurrentStagePdc = currentStagePdc,
            DaysUntilCurrentStagePdc = daysUntilCurrentStagePdc,
            DaysSinceLastPoRemark = daysSinceLastPoRemark,
            IsCurrentStageStartMissing = stage?.Status == StageStatus.InProgress && !stage.ActualStart.HasValue,
            IsCurrentStagePdcMissing = stage?.Status == StageStatus.InProgress && !stage.PlannedDue.HasValue,
            IsCurrentStageNotStarted = stage?.Status == StageStatus.NotStarted,
            UpdateStatus = updateStatus,
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

    private static bool HasProjectTimelineIssue(WorkspaceProjectMatrixRowVm row)
        => row.HasOverdueCurrentStage || row.HasCurrentStageIssue || row.HasBackfill;

    private static bool RequiresProjectAttention(WorkspaceProjectMatrixRowVm row)
        => row.UpdateStatus is "ActionRequired" or "Attention"
            || HasProjectTimelineIssue(row)
            || row.RecordGapCount > 0;
}
