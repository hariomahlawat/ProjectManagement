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
using ProjectManagement.Services.Usage;
using ProjectManagement.Services;
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
    private readonly IOfficerWorkloadReadService _officerWorkloadReadService;
    private readonly IProjectOfficerConferenceActionQuery _conferenceActions;
    private readonly IErpUsageQueryService _erpUsage;
    private readonly IMemoryCache _cache;

    public ProjectOfficerWorkspaceService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ProjectRecordHealthService health,
        WorkspaceNudgeService nudges,
        ActionTaskMyWorkQueueBuilder myWorkQueueBuilder,
        IActionTrackerClock clock,
        IAotsUnreadService aotsUnreadService,
        IOfficerWorkloadReadService officerWorkloadReadService,
        IProjectOfficerConferenceActionQuery conferenceActions,
        IErpUsageQueryService erpUsage,
        IMemoryCache cache)
    {
        _db = db;
        _users = users;
        _health = health;
        _nudges = nudges;
        _myWorkQueueBuilder = myWorkQueueBuilder;
        _clock = clock;
        _aotsUnreadService = aotsUnreadService;
        _officerWorkloadReadService = officerWorkloadReadService;
        _conferenceActions = conferenceActions;
        _erpUsage = erpUsage;
        _cache = cache;
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
            includeActionCount: view is not ProjectOfficerWorkspaceView.Overview and not ProjectOfficerWorkspaceView.Actions,
            ct);
        var vm = CreateWorkspaceShell(userId, principal, user, navigation);

        switch (view)
        {
            case ProjectOfficerWorkspaceView.Actions:
                await PopulateActionQueueAsync(vm, userId, today, ct);
                break;
            case ProjectOfficerWorkspaceView.Projects:
                await PopulateProjectsAsync(vm, userId, today, ct);
                break;
            case ProjectOfficerWorkspaceView.Tasks:
                await PopulateTasksAsync(vm, userId, today, ct);
                break;
            case ProjectOfficerWorkspaceView.Ideas:
                await PopulateIdeasAsync(vm, userId, today, ct);
                break;
            case ProjectOfficerWorkspaceView.FollowUps:
                await PopulateFollowUpsAsync(vm, userId, ct);
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
                await PopulateOverviewAsync(vm, userId, today, ct);
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

    private async Task PopulateOverviewAsync(
        ProjectOfficerWorkspaceVm vm,
        string userId,
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

        var commandWorkloadCard = await _officerWorkloadReadService.GetOfficerAsync(userId, ct);
        var upcomingEvents = await WorkspaceUpcomingEventQuery.LoadResultAsync(
            _db,
            userId,
            _clock.UtcNow,
            ct,
            windowDays: 14,
            maxItems: 5);
        var activityStrip = await _erpUsage.GetActivityStripAsync(userId, days: 30, cancellationToken: ct);
        var averageHealth = health.Count == 0
            ? 0
            : (int)Math.Round(health.Values.Average(item => item.HealthPercent));

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
        vm.ProjectsNeedingAttentionCount = matrixRows.Count(RequiresProjectAttention);
        vm.ProjectTimelineIssueCount = matrixRows.Count(HasProjectTimelineIssue);
        vm.PortfolioHealthPercent = averageHealth;
        vm.PortfolioHealthLabel = HealthSummaryLabel(health.Count, averageHealth);
        vm.RecordHealthSummaryLabel = HealthSummaryLabel(health.Count, averageHealth);
        vm.CommandWorkloadCard = commandWorkloadCard;
        vm.UpcomingEvents = upcomingEvents.Items;
        vm.UpcomingEventCount = upcomingEvents.TotalCount;
        vm.ActivityStrip = activityStrip;
    }

    private async Task PopulateActionQueueAsync(
        ProjectOfficerWorkspaceVm vm,
        string userId,
        DateOnly today,
        CancellationToken ct)
    {
        var projects = await LoadAssignedProjectsAsync(userId, ct);
        var tasks = await LoadOtherAssignedTasksAsync(userId, today, ct);
        var ideas = await LoadProjectIdeasAsync(userId, today, ct);
        var actionRows = projects
            .Select(project => BuildActionProjectRow(project, userId, today))
            .OrderBy(ProjectAttentionRank)
            .ThenByDescending(row => row.DaysInCurrentStage ?? 0)
            .ThenBy(row => row.ProjectName)
            .ToList();
        var operational = await LoadOperationalContextAsync(
            userId,
            today,
            projects,
            tasks,
            ideas,
            actionRows,
            vm.AotsUnreadCount,
            ct);

        ApplyOperationalContext(vm, operational);
        CacheNavigationActionCount(userId, operational.ActionQueue.TotalCount);
        vm.ProjectTimelineIssueCount = actionRows.Count(HasProjectTimelineIssue);
    }

    private async Task PopulateProjectsAsync(
        ProjectOfficerWorkspaceVm vm,
        string userId,
        DateOnly today,
        CancellationToken ct)
    {
        var projects = await LoadAssignedProjectsAsync(userId, ct);
        var health = await _health.CalculateForProjectsAsync(projects, userId, ct);
        var matrixRows = BuildProjectMatrix(projects, health, userId, today);
        var averageHealth = health.Count == 0
            ? 0
            : (int)Math.Round(health.Values.Average(item => item.HealthPercent));

        vm.ProjectMatrix = matrixRows;
        vm.RecordHealth = health.Values
            .OrderBy(item => item.HealthPercent)
            .ThenBy(item => item.ProjectName)
            .ToList();
        vm.RecordGapCount = health.Values.Sum(item => item.GapDetails.Count);
        vm.ProjectsNeedingAttentionCount = matrixRows.Count(RequiresProjectAttention);
        vm.ProjectTimelineIssueCount = matrixRows.Count(HasProjectTimelineIssue);
        vm.PortfolioHealthPercent = averageHealth;
        vm.PortfolioHealthLabel = HealthSummaryLabel(health.Count, averageHealth);
        vm.RecordHealthSummaryLabel = HealthSummaryLabel(health.Count, averageHealth);
    }

    private async Task PopulateTasksAsync(
        ProjectOfficerWorkspaceVm vm,
        string userId,
        DateOnly today,
        CancellationToken ct)
    {
        var tasks = await LoadOtherAssignedTasksAsync(userId, today, ct);
        vm.OfficialTasks = tasks;
        vm.OverdueTaskCount = tasks.Count(task => task.IsOverdue);
        vm.OfficialTaskCount = tasks.Count;
    }

    private async Task PopulateIdeasAsync(
        ProjectOfficerWorkspaceVm vm,
        string userId,
        DateOnly today,
        CancellationToken ct)
    {
        var ideas = await LoadProjectIdeasAsync(userId, today, ct);
        vm.Ideas = OrderIdeas(ideas);
        vm.AssignedIdeaCount = ideas.Count;
        vm.IdeasNeedingUpdateCount = ideas.Count(idea => idea.NeedsUpdate);
    }

    private async Task PopulateFollowUpsAsync(
        ProjectOfficerWorkspaceVm vm,
        string userId,
        CancellationToken ct)
    {
        var reminders = await LoadPersonalRemindersAsync(userId, ct);
        var upcomingEvents = await WorkspaceUpcomingEventQuery.LoadResultAsync(
            _db,
            userId,
            _clock.UtcNow,
            ct,
            windowDays: 14,
            maxItems: 5);

        vm.PersonalReminders = reminders;
        vm.UpcomingEvents = upcomingEvents.Items;
        vm.UpcomingEventCount = upcomingEvents.TotalCount;
        vm.NavigationFollowUpCount = reminders.Count;
    }

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
            OpenUrl = WorkspaceRouteHelper.ActionTask(row.Id)
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
    private async Task<IReadOnlyList<WorkspaceAttentionItemVm>> BuildReturnedItemsAsync(string userId, CancellationToken ct)
    {
        var items = new List<WorkspaceAttentionItemVm>();
        var returnedCutoffUtc = DateTimeOffset.UtcNow.AddDays(-30);
        var rejectedPlans = await _db.PlanVersions.AsNoTracking().Include(p => p.Project).Where(p => (p.CreatedByUserId == userId || p.SubmittedByUserId == userId) && p.RejectedOn.HasValue && p.RejectedOn >= returnedCutoffUtc).OrderByDescending(p => p.RejectedOn).Take(10).ToListAsync(ct);
        var rejectedPlanProjectIds = rejectedPlans.Select(p => p.ProjectId).ToArray();
        var newerPlanProjects = await _db.PlanVersions.AsNoTracking().Where(p => rejectedPlanProjectIds.Contains(p.ProjectId) && (p.Status == PlanVersionStatus.PendingApproval || p.Status == PlanVersionStatus.Approved)).Select(p => new { p.ProjectId, EventAt = p.SubmittedOn ?? p.ApprovedOn ?? p.CreatedOn }).ToListAsync(ct);
        rejectedPlans = rejectedPlans.Where(p => !newerPlanProjects.Any(n => n.ProjectId == p.ProjectId && p.RejectedOn.HasValue && n.EventAt > p.RejectedOn.Value)).Take(5).ToList();
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
            ActionUrl = WorkspaceRouteHelper.ProjectTimeline(p.ProjectId),
            DueOrEventDateUtc = p.RejectedOn?.UtcDateTime
        }));

        var rejectedStages = await _db.StageChangeRequests.AsNoTracking().Where(r => r.RequestedByUserId == userId && r.DecisionStatus == "Rejected" && r.DecidedOn.HasValue && r.DecidedOn.Value >= returnedCutoffUtc).OrderByDescending(r => r.DecidedOn).Take(10).ToListAsync(ct);
        var rejectedStageProjectIds = rejectedStages.Select(r => r.ProjectId).ToArray();
        var newerStageProjects = await _db.StageChangeRequests.AsNoTracking().Where(r => rejectedStageProjectIds.Contains(r.ProjectId) && (r.DecisionStatus == "Pending" || r.DecisionStatus == "Approved")).Select(r => new { r.ProjectId, EventAt = r.DecidedOn ?? r.RequestedOn }).ToListAsync(ct);
        rejectedStages = rejectedStages.Where(r => !newerStageProjects.Any(n => n.ProjectId == r.ProjectId && r.DecidedOn.HasValue && n.EventAt > r.DecidedOn.Value)).Take(5).ToList();
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
            DueOrEventDateUtc = r.DecidedOn?.UtcDateTime
        }));

        var rejectedMeta = await _db.ProjectMetaChangeRequests.AsNoTracking().Include(r => r.Project).Where(r => r.RequestedByUserId == userId && r.DecisionStatus == "Rejected" && r.DecidedOnUtc.HasValue && r.DecidedOnUtc.Value >= returnedCutoffUtc).OrderByDescending(r => r.DecidedOnUtc).Take(10).ToListAsync(ct);
        var rejectedMetaProjectIds = rejectedMeta.Select(r => r.ProjectId).ToArray();
        var newerMetaProjects = await _db.ProjectMetaChangeRequests.AsNoTracking().Where(r => rejectedMetaProjectIds.Contains(r.ProjectId) && (r.DecisionStatus == "Pending" || r.DecisionStatus == "Approved")).Select(r => new { r.ProjectId, EventAt = r.DecidedOnUtc ?? r.RequestedOnUtc }).ToListAsync(ct);
        rejectedMeta = rejectedMeta.Where(r => !newerMetaProjects.Any(n => n.ProjectId == r.ProjectId && r.DecidedOnUtc.HasValue && n.EventAt > r.DecidedOnUtc.Value)).Take(5).ToList();
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
            DueOrEventDateUtc = r.DecidedOnUtc?.UtcDateTime
        }));

        var rejectedDocuments = await _db.ProjectDocumentRequests.AsNoTracking().Where(r => r.RequestedByUserId == userId && r.Status == ProjectDocumentRequestStatus.Rejected && r.ReviewedAtUtc.HasValue && r.ReviewedAtUtc.Value >= returnedCutoffUtc).OrderByDescending(r => r.ReviewedAtUtc).Take(10).ToListAsync(ct);
        var rejectedDocumentProjectIds = rejectedDocuments.Select(r => r.ProjectId).ToArray();
        var newerDocumentProjects = await _db.ProjectDocumentRequests.AsNoTracking().Where(r => rejectedDocumentProjectIds.Contains(r.ProjectId) && (r.Status == ProjectDocumentRequestStatus.Submitted || r.Status == ProjectDocumentRequestStatus.Approved)).Select(r => new { r.ProjectId, EventAt = r.ReviewedAtUtc ?? r.RequestedAtUtc }).ToListAsync(ct);
        rejectedDocuments = rejectedDocuments.Where(r => !newerDocumentProjects.Any(n => n.ProjectId == r.ProjectId && r.ReviewedAtUtc.HasValue && n.EventAt > r.ReviewedAtUtc.Value)).Take(5).ToList();
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
            DueOrEventDateUtc = r.ReviewedAtUtc?.UtcDateTime
        }));
        return items.OrderByDescending(i => i.DueOrEventDateUtc).Take(12).ToList();
    }
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
