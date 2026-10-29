using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
using ProjectManagement.Services.Stages;
using ProjectManagement.Services.Arpp;
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
    private readonly IOfficerConferenceReadService _conference;
    private readonly ProjectProcurementReadService _procurement;
    private readonly ProjectManagement.Services.Stages.PlanReadService _planRead;
    private readonly ProcurementFactAvailabilityService _procurementFactAvailability;
    private readonly IArppIpaStageAuthorityService _arppIpaStageAuthority;

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
        IOfficerConferenceReadService conference,
        ProjectProcurementReadService procurement,
        ProjectManagement.Services.Stages.PlanReadService planRead,
        ProcurementFactAvailabilityService? procurementFactAvailability = null,
        IArppIpaStageAuthorityService? arppIpaStageAuthority = null)
    {
        _procurementFactAvailability = procurementFactAvailability ?? ProcurementFactAvailabilityService.Create(db);
        _arppIpaStageAuthority = arppIpaStageAuthority ?? new ArppIpaStageAuthorityService(db);
        _db = db;
        _users = users;
        _health = health;
        _nudges = nudges;
        _myWorkQueueBuilder = myWorkQueueBuilder;
        _clock = clock;
        _aotsUnreadService = aotsUnreadService;
        _conferenceActions = conferenceActions;
        _erpUsage = erpUsage;
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
        var navigation = await LoadNavigationSummaryAsync(userId, today, ct);
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
        // The desk reads only unread circulars and pending conference directions here. The old
        // action queue (returned items, remarks due, tasks due) is no longer shown anywhere, so it
        // is not built.
        var aotsDocuments = await LoadUnreadAotsDocumentsAsync(userId, ct);
        var conferenceDirections = await _conferenceActions.GetPendingAsync(
            userId,
            projects.ToDictionary(project => project.Id, project => project.Name),
            ideas.ToDictionary(idea => idea.IdeaId, idea => idea.Title),
            tasks.ToDictionary(task => task.TaskId, task => task.Title),
            ct);
        var conference = await _conference.GetForProjectOfficerAsync(userId, ct);
        var pending = await LoadPendingApprovalsAsync(userId, projects, tasks, ct);
        var lastTaskProgress = await LoadLastTaskProgressAsync(userId, tasks, ct);
        var procurementEdits = await LoadProcurementEditorsAsync(projects, health, ct);
        var planEditors = await LoadPlanEditorsAsync(userId, matrixRows, pending.PlanSinceByProject, ct);
        // A published ARPP record supplies the IPA completion date, and the date editor will not
        // take one, so the desk never asks the PO for it.
        var ipaFromArpp = await _arppIpaStageAuthority.ResolveManyAsync(projects.Select(project => project.Id).ToArray(), ct);
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
                    pending.CorrectionsByProject.GetValueOrDefault(row.ProjectId),
                    procurementEdits.GetValueOrDefault(row.ProjectId),
                    planEditors.GetValueOrDefault(row.ProjectId),
                    IpaCompletionFromArpp: ipaFromArpp.ContainsKey(row.ProjectId)))
                .ToList(),
            ideas.Select(idea => new BriefingDeskIdeaInput(idea)).ToList(),
            tasks
                .Select(task => new BriefingDeskTaskInput(
                    task,
                    lastTaskProgress.TryGetValue(task.TaskId, out var lastProgress) ? lastProgress : null,
                    task.RowVersion))
                .ToList(),
            conferenceDirections,
            conference,
            pending.Waiting,
            PlanDirectSaveRole: principal.IsInRole(ProjectManagement.Configuration.RoleNames.Admin)
                || principal.IsInRole(ProjectManagement.Configuration.RoleNames.HoD)));

        vm.AotsDocuments = aotsDocuments;
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

            var anchor = $"desk-{BriefingDeskLabels.KindKey(ConferenceItemKind.Project)}-{projectId}";
            editors[projectId] = ProcurementEditVmFactory.Create(
                projectId,
                procurement.GetValueOrDefault(projectId) ?? ProcurementAtAGlanceVm.Empty,
                await _procurementFactAvailability.GetRecordableStagesAsync(projectId, ct),
                returnUrl: WorkspaceRouteHelper.BriefingDeskItem(anchor),
                idPrefix: $"{anchor}-");
        }

        return editors;
    }

    // SECTION: Plan editor state for the desk's current-stage form
    // Read with the same PlanReadService as Project Overview, and only for projects whose
    // current-stage PDC is missing with nothing already awaiting approval.
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
            if (!hasStage || row.CurrentStagePdc is not null || pendingPlans.ContainsKey(row.ProjectId))
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
        // the card's check whoever submitted it. Plans use the canonical actionable-pending
        // rule, so a superseded or withdrawn submission never counts.
        var pendingPlans = await PlanRevisionConsistency.ActionablePending(_db)
            .AsNoTracking()
            .Where(plan => projectIds.Contains(plan.ProjectId))
            .Select(plan => new { plan.ProjectId, plan.SubmittedOn, plan.CreatedOn, SubmittedBy = plan.SubmittedByUserId ?? plan.OwnerUserId })
            .ToListAsync(ct);
        var planSince = pendingPlans
            .GroupBy(plan => plan.ProjectId)
            .ToDictionary(group => group.Key, group => group.Min(plan => plan.SubmittedOn ?? plan.CreatedOn));

        // The latest pending correction per project, as the date-correction editor reads it.
        // Its items say which completion dates it proposes; its Before values say whether the
        // official dates moved since, in which case the HoD cannot approve it until the PO
        // reviews and resubmits.
        var pendingRevisions = await _db.ActualCorrectionRevisions
            .AsNoTracking()
            .Include(revision => revision.Items)
            .Where(revision => projectIds.Contains(revision.ProjectId) && revision.Status == ActualCorrectionRevisionStatus.Pending)
            .ToListAsync(ct);
        var stagesByProject = projects.ToDictionary(project => project.Id, project => project.ProjectStages);
        var corrections = pendingRevisions
            .GroupBy(revision => revision.ProjectId)
            .Select(group => group.OrderByDescending(revision => revision.RevisionNo).First())
            .ToDictionary(
                revision => revision.ProjectId,
                revision => new BriefingDeskPendingCorrection(
                    revision.SubmittedOn,
                    revision.Items
                        .Where(item => item.ProposedCompletedOn.HasValue)
                        .Select(item => item.StageCode)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase),
                    ActualCorrectionProposalService.ChangedSinceSubmission(revision, stagesByProject[revision.ProjectId]).Count > 0
                        && !ActualCorrectionProposalService.AlreadySatisfied(revision, stagesByProject[revision.ProjectId]),
                    revision.SubmittedByUserId));

        // The side panel says who sent a project-scoped submission when it was not this PO.
        var otherSubmitterIds = pendingPlans.Select(plan => plan.SubmittedBy)
            .Concat(corrections.Values.Select(correction => correction.SubmittedByUserId))
            .Where(id => !string.IsNullOrWhiteSpace(id) && !string.Equals(id, userId, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var submitterNames = otherSubmitterIds.Length == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await _db.Users
                .AsNoTracking()
                .Where(user => otherSubmitterIds.Contains(user.Id))
                .Select(user => new { user.Id, Name = string.IsNullOrEmpty(user.FullName) ? user.UserName : user.FullName })
                .ToDictionaryAsync(user => user.Id, user => user.Name ?? "another user", StringComparer.Ordinal, ct);
        string? SentBy(string? submitterId)
            => string.IsNullOrWhiteSpace(submitterId) || string.Equals(submitterId, userId, StringComparison.Ordinal)
                ? null
                : submitterNames.GetValueOrDefault(submitterId) ?? "another user";

        waiting.AddRange(pendingPlans
            .GroupBy(plan => plan.ProjectId)
            .Select(group => group.OrderBy(plan => plan.SubmittedOn ?? plan.CreatedOn).First())
            .Select(plan => new BriefingDeskWaitingVm(
                "Timeline plan",
                projectNames[plan.ProjectId],
                "HoD",
                plan.SubmittedOn ?? plan.CreatedOn,
                WorkspaceRouteHelper.ProjectTimeline(plan.ProjectId),
                SentBy(plan.SubmittedBy))));

        // A correction that needs resubmitting is work for the PO, so it is on the card and
        // not in the "sent" list.
        waiting.AddRange(corrections
            .Where(entry => !entry.Value.NeedsResubmission)
            .Select(entry => new BriefingDeskWaitingVm(
                "Date correction",
                projectNames[entry.Key],
                "HoD",
                entry.Value.SubmittedOn,
                WorkspaceRouteHelper.ProjectTimelineActuals(entry.Key),
                SentBy(entry.Value.SubmittedByUserId))));

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

        return new BriefingDeskPendingApprovals(waiting, planSince, corrections);
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
        IReadOnlyDictionary<int, BriefingDeskPendingCorrection> CorrectionsByProject);

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
        CancellationToken ct)
    {
        // The menu shows counts only; the old action-queue badge is gone, so nothing here
        // builds the action queue.
        var assignedProjectCount = await _db.Projects
            .AsNoTracking()
            .CountAsync(project =>
                project.LeadPoUserId == userId
                && !project.IsDeleted
                && !project.IsArchived
                && project.LifecycleStatus == ProjectLifecycleStatus.Active,
                ct);

        var assignedTaskCount = await _db.ActionTasks
            .AsNoTracking()
            .CountAsync(task =>
                !task.IsDeleted
                && task.AssignedToUserId == userId
                && task.Status != ActionTaskStatuses.Closed
                && task.Status != ActionTaskStatuses.Backlog,
                ct);

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
        return new ProjectOfficerNavigationSummary(
            assignedProjectCount,
            assignedTaskCount,
            assignedIdeaCount,
            reminderFollowUpCount,
            aotsUnreadCount);
    }

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
        int AotsUnreadCount);

    // SECTION: Assigned projects include current-stage context for the workspace matrix.
    private async Task<IReadOnlyList<Project>> LoadAssignedProjectsAsync(string userId, CancellationToken ct)
    {
        return await _db.Projects
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.ProjectStages)
            // Only the PO's latest remark is read (WorkspaceNudgeService.LastPoRemark), so only it
            // is loaded rather than every remark on every project.
            .Include(p => p.Remarks
                .Where(r => !r.IsDeleted && r.AuthorUserId == userId)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(1))
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

}
