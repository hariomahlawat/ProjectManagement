using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Approvals;
using ProjectManagement.Services.Authorization;
using ProjectManagement.ViewModels;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// Loads the facts behind the Command desk. The HoD and the Comdt see the same data; only
/// Waiting on you depends on the viewer. Every query is read-only and runs sequentially on the
/// scoped DbContext.
/// </summary>
public sealed class CommandDeskService
{
    // SECTION: Expected register frequencies (days between entries)
    internal static readonly IReadOnlyList<(string Label, int ExpectedDays, string Url)> Registers = new[]
    {
        ("Visits", 15, CommandDeskRoutes.Visits),
        ("Misc activities", 15, CommandDeskRoutes.Activities),
        ("Training", 30, CommandDeskRoutes.Training),
        ("Proliferation", 30, CommandDeskRoutes.Proliferation),
        ("Social media", 30, CommandDeskRoutes.SocialMedia)
    };

    private const int MaxWindowDays = 30;

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IOfficerWorkloadReadService _workload;
    private readonly IOfficerConferenceReadService _conference;
    private readonly IApprovalQueueService _approvals;

    public CommandDeskService(
        ApplicationDbContext db,
        IClock clock,
        IOfficerWorkloadReadService workload,
        IOfficerConferenceReadService conference,
        IApprovalQueueService approvals)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _workload = workload ?? throw new ArgumentNullException(nameof(workload));
        _conference = conference ?? throw new ArgumentNullException(nameof(conference));
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
    }

    public async Task<CommandDeskVm> GetAsync(ClaimsPrincipal user, string userId, int? windowDays, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);
        var canOpenDecisionCentre = ApprovalAuthorization.CanApproveProjectChanges(
            user.IsInRole(RoleNames.Admin),
            user.IsInRole(RoleNames.HoD));

        var projects = await LoadProjectsAsync(userId, today, ct);
        var input = new CommandDeskInput(
            today,
            CommandDeskBuilder.NormaliseWindow(windowDays),
            await LoadProgressAsync(today, ct),
            await LoadDecisionsAsync(user, userId, canOpenDecisionCentre, ct),
            canOpenDecisionCentre,
            projects.Select(project => project.Input).ToList(),
            await LoadDirectionsAsync(userId, ct),
            await LoadTasksAsync(today, ct),
            await LoadReturnedAsync(projects, ct),
            await LoadRegistersAsync(today, ct));

        return CommandDeskBuilder.Build(input);
    }

    // SECTION: What moved forward
    private async Task<IReadOnlyList<CommandDeskProgressEvent>> LoadProgressAsync(DateOnly today, CancellationToken ct)
    {
        var from = today.AddDays(-(MaxWindowDays - 1));
        var fromUtc = IstClock.StartOfDayIstToUtc(from);
        var fromUtcDateTime = fromUtc.UtcDateTime;
        var events = new List<CommandDeskProgressEvent>();

        var stages = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => stage.Status == StageStatus.Completed
                && !stage.IsAutoCompleted
                && stage.CompletedOn >= from && stage.CompletedOn <= today
                && stage.Project != null && !stage.Project.IsDeleted && !stage.Project.IsArchived)
            .Select(stage => new
            {
                stage.ProjectId,
                stage.StageCode,
                CompletedOn = stage.CompletedOn!.Value,
                ProjectName = stage.Project!.Name,
                stage.Project.WorkflowVersion,
                Officer = stage.Project.LeadPoUser == null ? null : stage.Project.LeadPoUser.FullName
            })
            .ToListAsync(ct);
        events.AddRange(stages.Select(stage => new CommandDeskProgressEvent(
            CommandDeskProgressKind.StageCompleted,
            stage.CompletedOn,
            Text: $"{stage.ProjectName} completed {StageCodes.DisplayNameOf(stage.WorkflowVersion, stage.StageCode)}",
            Meta: stage.Officer,
            Url: WorkspaceRouteHelper.ProjectOverview(stage.ProjectId))));

        var completedProjects = await _db.Projects
            .AsNoTracking()
            .Where(project => !project.IsDeleted
                && project.LifecycleStatus == ProjectLifecycleStatus.Completed
                && project.CompletedOn >= from && project.CompletedOn <= today)
            .Select(project => new
            {
                project.Id,
                project.Name,
                CompletedOn = project.CompletedOn!.Value,
                Officer = project.LeadPoUser == null ? null : project.LeadPoUser.FullName
            })
            .ToListAsync(ct);
        events.AddRange(completedProjects.Select(project => new CommandDeskProgressEvent(
            CommandDeskProgressKind.ProjectCompleted,
            project.CompletedOn,
            Text: $"{project.Name} completed",
            Meta: project.Officer,
            Url: WorkspaceRouteHelper.ProjectOverview(project.Id))));

        var closedTasks = await _db.ActionTasks
            .AsNoTracking()
            .Where(task => !task.IsDeleted && task.Status == ActionTaskStatuses.Closed && task.ClosedOn >= fromUtcDateTime)
            .Select(task => task.ClosedOn!.Value)
            .ToListAsync(ct);
        events.AddRange(closedTasks.Select(on => new CommandDeskProgressEvent(CommandDeskProgressKind.TaskClosed, IstDate(on))));

        var tots = await _db.ProjectTots
            .AsNoTracking()
            .Where(tot => tot.Status == ProjectTotStatus.Completed && tot.CompletedOn >= from && tot.CompletedOn <= today)
            .Select(tot => new { tot.ProjectId, CompletedOn = tot.CompletedOn!.Value, ProjectName = tot.Project != null ? tot.Project.Name : null })
            .ToListAsync(ct);
        events.AddRange(tots.Select(tot => new CommandDeskProgressEvent(
            CommandDeskProgressKind.TotCompleted,
            tot.CompletedOn,
            Text: $"ToT completed: {tot.ProjectName ?? "project"}",
            Url: WorkspaceRouteHelper.ProjectOverview(tot.ProjectId))));

        var iprs = await _db.IprRecords
            .AsNoTracking()
            .Where(ipr => ipr.FiledAtUtc >= fromUtc || ipr.GrantedAtUtc >= fromUtc)
            .Select(ipr => new { ipr.Title, ipr.IprFilingNumber, ipr.FiledAtUtc, ipr.GrantedAtUtc, ipr.Status })
            .ToListAsync(ct);
        foreach (var ipr in iprs)
        {
            if (ipr.FiledAtUtc >= fromUtc)
            {
                events.Add(new CommandDeskProgressEvent(CommandDeskProgressKind.IprFiled, IstDate(ipr.FiledAtUtc.Value)));
            }

            if (ipr.GrantedAtUtc >= fromUtc && ipr.Status == IprStatus.Granted)
            {
                events.Add(new CommandDeskProgressEvent(
                    CommandDeskProgressKind.IprGranted,
                    IstDate(ipr.GrantedAtUtc.Value),
                    Text: $"IPR granted: {(string.IsNullOrWhiteSpace(ipr.Title) ? ipr.IprFilingNumber : ipr.Title)}",
                    Url: CommandDeskRoutes.Ipr));
            }
        }

        var proliferation = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(entry => entry.ApprovalStatus == ApprovalStatus.Approved
                && entry.ProliferationDate >= from && entry.ProliferationDate <= today)
            .Select(entry => new { entry.ProliferationDate, entry.Quantity })
            .ToListAsync(ct);
        events.AddRange(proliferation
            .Where(entry => entry.Quantity > 0)
            .Select(entry => new CommandDeskProgressEvent(CommandDeskProgressKind.UnitsProliferated, entry.ProliferationDate, entry.Quantity)));

        var ffc = await _db.FfcRecords
            .AsNoTracking()
            .Where(record => !record.IsDeleted
                && ((record.DeliveryYes && record.DeliveryDate >= from && record.DeliveryDate <= today)
                    || (record.InstallationYes && record.InstallationDate >= from && record.InstallationDate <= today)))
            .Select(record => new { record.DeliveryYes, record.DeliveryDate, record.InstallationYes, record.InstallationDate })
            .ToListAsync(ct);
        foreach (var record in ffc)
        {
            if (record.DeliveryYes && record.DeliveryDate >= from && record.DeliveryDate <= today)
            {
                events.Add(new CommandDeskProgressEvent(CommandDeskProgressKind.FfcDelivered, record.DeliveryDate!.Value));
            }

            if (record.InstallationYes && record.InstallationDate >= from && record.InstallationDate <= today)
            {
                events.Add(new CommandDeskProgressEvent(CommandDeskProgressKind.FfcInstalled, record.InstallationDate!.Value));
            }
        }

        return events;
    }

    // SECTION: Waiting on you
    private async Task<IReadOnlyList<CommandDeskDecisionInput>> LoadDecisionsAsync(
        ClaimsPrincipal user,
        string userId,
        bool canOpenDecisionCentre,
        CancellationToken ct)
    {
        var decisions = new List<CommandDeskDecisionInput>();
        if (canOpenDecisionCentre)
        {
            // The Decision Centre's own queue, so the desk never disagrees with it.
            var pending = await _approvals.GetPendingAsync(new ApprovalQueueQuery(), user, ct);
            decisions.AddRange(pending.Select(item => new CommandDeskDecisionInput(
                item.ProjectName ?? item.Summary,
                ApprovalTypeLabel(item),
                item.RequestedByName,
                item.RequestedAtUtc,
                "Review",
                CommandDeskRoutes.Approval(item.ApprovalType.ToString(), item.RequestId))));
        }

        // A task submitted for closure waits on whoever assigned it.
        var submitted = await _db.ActionTasks
            .AsNoTracking()
            .Where(task => !task.IsDeleted
                && task.Status == ActionTaskStatuses.Submitted
                && task.CreatedByUserId == userId)
            .Select(task => new { task.Id, task.Title, task.AssignedToUserId, task.SubmittedOn, task.AssignedOn })
            .ToListAsync(ct);
        var names = await LoadNamesAsync(submitted.Select(task => task.AssignedToUserId), ct);
        decisions.AddRange(submitted.Select(task => new CommandDeskDecisionInput(
            task.Title,
            "Task submitted for closure",
            names.GetValueOrDefault(task.AssignedToUserId) ?? "Assignee",
            new DateTimeOffset(DateTime.SpecifyKind(task.SubmittedOn ?? task.AssignedOn, DateTimeKind.Utc)),
            "Close or return",
            CommandDeskRoutes.Task(task.Id))));

        return decisions;
    }

    private static string ApprovalTypeLabel(ApprovalQueueItemVm item) => item.ApprovalType switch
    {
        ApprovalQueueType.PlanApproval => "Timeline plan",
        ApprovalQueueType.StageChange => string.IsNullOrWhiteSpace(item.StageCode) ? "Stage update" : $"Stage update: {item.StageCode}",
        ApprovalQueueType.ProjectMeta => "Project details",
        ApprovalQueueType.DocRequest => string.IsNullOrWhiteSpace(item.Summary) ? "Document" : item.Summary,
        ApprovalQueueType.TotRequest => "ToT update",
        ApprovalQueueType.ProliferationYearly or ApprovalQueueType.ProliferationGranular => "Proliferation entry",
        ApprovalQueueType.ActivityDelete => "Activity deletion",
        ApprovalQueueType.TrainingDelete => "Training deletion",
        ApprovalQueueType.RepositoryDocumentDelete => "Document deletion",
        ApprovalQueueType.ActualCorrection => "Actuals correction",
        _ => item.Summary
    };

    // SECTION: Projects (PDC watch and needs attention)
    private sealed record ProjectFacts(CommandDeskProjectInput Input);

    private async Task<IReadOnlyList<ProjectFacts>> LoadProjectsAsync(string userId, DateOnly today, CancellationToken ct)
    {
        // The workload service owns assignment, lifecycle and current-stage resolution.
        var officers = await _workload.GetAllAsync(userId, ct);
        var assigned = officers
            .SelectMany(officer => officer.Projects.Select(project => new { officer, project }))
            .GroupBy(item => item.project.ProjectId)
            .Select(group => group.First())
            .ToList();
        if (assigned.Count == 0)
        {
            return Array.Empty<ProjectFacts>();
        }

        var projectIds = assigned.Select(item => item.project.ProjectId).ToArray();
        var stages = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => projectIds.Contains(stage.ProjectId))
            .Select(stage => new { stage.ProjectId, stage.StageCode, stage.PlannedDue })
            .ToListAsync(ct);
        var pdcByStage = stages.ToDictionary(
            stage => (stage.ProjectId, stage.StageCode.ToUpperInvariant()),
            stage => stage.PlannedDue);

        var withHod = (await _db.PlanVersions
                .AsNoTracking()
                .Where(plan => projectIds.Contains(plan.ProjectId) && plan.Status == PlanVersionStatus.PendingApproval)
                .Select(plan => plan.ProjectId)
                .ToListAsync(ct))
            .ToHashSet();

        var updatesFromUtc = IstClock.StartOfDayIstToUtc(today.AddDays(-CommandDeskBuilder.StaleUpdateDays)).UtcDateTime;
        var recentRemarks = await _db.Remarks
            .AsNoTracking()
            .Where(remark => projectIds.Contains(remark.ProjectId) && !remark.IsDeleted && remark.CreatedAtUtc >= updatesFromUtc)
            .GroupBy(remark => new { remark.ProjectId, remark.AuthorUserId })
            .Select(group => new { group.Key.ProjectId, group.Key.AuthorUserId, Latest = group.Max(remark => remark.CreatedAtUtc) })
            .ToListAsync(ct);
        var latestByOfficer = recentRemarks.ToDictionary(
            remark => (remark.ProjectId, remark.AuthorUserId),
            remark => DateOnly.FromDateTime(IstClock.ToIst(DateTime.SpecifyKind(remark.Latest, DateTimeKind.Utc))));

        return assigned
            .Select(item => new ProjectFacts(new CommandDeskProjectInput(
                item.project.ProjectId,
                item.project.Name,
                item.officer.UserId,
                OfficerName(item.officer),
                item.project.StageName,
                pdcByStage.GetValueOrDefault((item.project.ProjectId, (item.project.StageCode ?? string.Empty).ToUpperInvariant())),
                withHod.Contains(item.project.ProjectId),
                latestByOfficer.TryGetValue((item.project.ProjectId, item.officer.UserId), out var latest) ? latest : null)))
            .ToList();
    }

    // SECTION: Directions awaiting progress
    private async Task<IReadOnlyList<CommandDeskDirectionInput>> LoadDirectionsAsync(string userId, CancellationToken ct)
    {
        var conferences = await _conference.GetAllForCommandAsync(userId, ct);
        var result = new List<CommandDeskDirectionInput>();
        foreach (var conference in conferences)
        {
            foreach (var item in conference.Sections.SelectMany(section => section.Items))
            {
                if (item.LatestDirection is null)
                {
                    continue;
                }

                result.Add(new CommandDeskDirectionInput(
                    item.Kind,
                    item.Title,
                    conference.OfficerName,
                    item.LatestDirection.Body,
                    item.LatestDirection.AuthorName,
                    DateOnly.FromDateTime(IstClock.ToIst(DateTime.SpecifyKind(item.LatestDirection.CreatedAtUtc, DateTimeKind.Utc))),
                    IsAnswered(item),
                    CommandDeskRoutes.Conference(conference.OfficerUserId)));
            }
        }

        return result;
    }

    /// <summary>
    /// A direction is answered once the officer (or task assignee) has recorded real progress
    /// after it. The conference view adds placeholder entries with only EmptyText, and an MCO
    /// note is not the officer's reply.
    /// </summary>
    internal static bool IsAnswered(OfficerConferenceItemVm item)
        => item.ProgressEntries.Any(entry => !string.Equals(entry.Label, "MCO", StringComparison.Ordinal)
            && (entry.ActivityAtUtc.HasValue || !string.IsNullOrWhiteSpace(entry.Body)));

    // SECTION: Tasks
    private async Task<IReadOnlyList<CommandDeskTaskInput>> LoadTasksAsync(DateOnly today, CancellationToken ct)
    {
        var dueBy = today.AddDays(CommandDeskBuilder.TaskDueSoonDays + 1).ToDateTime(TimeOnly.MinValue);
        var tasks = await _db.ActionTasks
            .AsNoTracking()
            .Where(task => !task.IsDeleted
                && task.Status != ActionTaskStatuses.Closed
                && task.Status != ActionTaskStatuses.Submitted
                && (task.Status == ActionTaskStatuses.Blocked || task.DueDate < dueBy))
            .Select(task => new
            {
                task.Id,
                task.Title,
                task.AssignedToUserId,
                task.CreatedByUserId,
                task.Priority,
                task.Status,
                task.DueDate
            })
            .ToListAsync(ct);
        if (tasks.Count == 0)
        {
            return Array.Empty<CommandDeskTaskInput>();
        }

        var names = await LoadNamesAsync(tasks.SelectMany(task => new[] { task.AssignedToUserId, task.CreatedByUserId }), ct);
        var blockedIds = tasks.Where(task => task.Status == ActionTaskStatuses.Blocked).Select(task => task.Id).ToArray();
        var reasons = blockedIds.Length == 0
            ? new Dictionary<int, string?>()
            : (await _db.ActionTaskAuditLogs
                    .AsNoTracking()
                    .Where(log => blockedIds.Contains(log.TaskId)
                        && log.ActionType == "StatusUpdated"
                        && log.NewValue == ActionTaskStatuses.Blocked)
                    .Select(log => new { log.TaskId, log.PerformedAt, log.Id, log.Remarks })
                    .ToListAsync(ct))
                .GroupBy(log => log.TaskId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(log => log.PerformedAt).ThenByDescending(log => log.Id).First().Remarks);

        return tasks
            .Select(task => new CommandDeskTaskInput(
                task.Id,
                task.Title,
                names.GetValueOrDefault(task.AssignedToUserId) ?? "Unassigned",
                names.GetValueOrDefault(task.CreatedByUserId) ?? "Unknown",
                task.Priority,
                task.Status,
                DateOnly.FromDateTime(task.DueDate),
                reasons.GetValueOrDefault(task.Id)))
            .ToList();
    }

    // SECTION: Returned and not yet corrected
    // Command-wide form of the Project Officer desk's returned items: a returned plan, stage
    // update or project-details request stays open until a newer request is pending or approved.
    private async Task<IReadOnlyList<CommandDeskReturnedInput>> LoadReturnedAsync(IReadOnlyList<ProjectFacts> projects, CancellationToken ct)
    {
        if (projects.Count == 0)
        {
            return Array.Empty<CommandDeskReturnedInput>();
        }

        var byProject = projects.ToDictionary(project => project.Input.ProjectId, project => project.Input);
        var projectIds = byProject.Keys.ToArray();
        var cutoff = _clock.UtcNow.AddDays(-ProjectOfficerWorkspaceService.DeskReturnedHorizonDays);
        var returnedProjectIds = new List<int>();

        var rejectedPlans = await _db.PlanVersions
            .AsNoTracking()
            .Where(plan => projectIds.Contains(plan.ProjectId) && plan.RejectedOn >= cutoff)
            .Select(plan => new { plan.ProjectId, RejectedOn = plan.RejectedOn!.Value })
            .ToListAsync(ct);
        var laterPlans = await _db.PlanVersions
            .AsNoTracking()
            .Where(plan => projectIds.Contains(plan.ProjectId)
                && (plan.Status == PlanVersionStatus.PendingApproval || plan.Status == PlanVersionStatus.Approved))
            .Select(plan => new { plan.ProjectId, EventAt = plan.SubmittedOn ?? plan.ApprovedOn ?? plan.CreatedOn })
            .ToListAsync(ct);
        returnedProjectIds.AddRange(rejectedPlans
            .GroupBy(plan => plan.ProjectId)
            .Where(group => !laterPlans.Any(later => later.ProjectId == group.Key && later.EventAt > group.Max(plan => plan.RejectedOn)))
            .Select(group => group.Key));

        var rejectedStages = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(request => projectIds.Contains(request.ProjectId) && request.DecisionStatus == "Rejected" && request.DecidedOn >= cutoff)
            .Select(request => new { request.ProjectId, request.StageCode, DecidedOn = request.DecidedOn!.Value })
            .ToListAsync(ct);
        var laterStages = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(request => projectIds.Contains(request.ProjectId) && (request.DecisionStatus == "Pending" || request.DecisionStatus == "Approved"))
            .Select(request => new { request.ProjectId, request.StageCode, EventAt = request.DecidedOn ?? request.RequestedOn })
            .ToListAsync(ct);
        returnedProjectIds.AddRange(rejectedStages
            .Where(request => !laterStages.Any(later => later.ProjectId == request.ProjectId
                && string.Equals(later.StageCode, request.StageCode, StringComparison.OrdinalIgnoreCase)
                && later.EventAt > request.DecidedOn))
            .Select(request => request.ProjectId));

        var rejectedMeta = await _db.ProjectMetaChangeRequests
            .AsNoTracking()
            .Where(request => projectIds.Contains(request.ProjectId) && request.DecisionStatus == "Rejected" && request.DecidedOnUtc >= cutoff)
            .Select(request => new { request.ProjectId, DecidedOn = request.DecidedOnUtc!.Value })
            .ToListAsync(ct);
        var laterMeta = await _db.ProjectMetaChangeRequests
            .AsNoTracking()
            .Where(request => projectIds.Contains(request.ProjectId) && (request.DecisionStatus == "Pending" || request.DecisionStatus == "Approved"))
            .Select(request => new { request.ProjectId, EventAt = request.DecidedOnUtc ?? request.RequestedOnUtc })
            .ToListAsync(ct);
        returnedProjectIds.AddRange(rejectedMeta
            .Where(request => !laterMeta.Any(later => later.ProjectId == request.ProjectId && later.EventAt > request.DecidedOn))
            .Select(request => request.ProjectId));

        return returnedProjectIds
            .Select(projectId => byProject[projectId])
            .Select(project => new CommandDeskReturnedInput(project.ProjectId, project.OfficerUserId, project.OfficerName))
            .ToList();
    }

    // SECTION: What's gone quiet
    private async Task<IReadOnlyList<CommandDeskRegisterInput>> LoadRegistersAsync(DateOnly today, CancellationToken ct)
    {
        var endOfTodayUtc = IstClock.ExclusiveEndOfDayIstToUtc(today);
        var lastVisit = await _db.Visits.AsNoTracking()
            .Where(visit => visit.DateOfVisit <= today)
            .MaxAsync(visit => (DateOnly?)visit.DateOfVisit, ct);
        var lastActivity = await _db.Activities.AsNoTracking()
            .Where(activity => !activity.IsDeleted && activity.ScheduledStartUtc < endOfTodayUtc)
            .MaxAsync(activity => activity.ScheduledStartUtc, ct);
        var lastTrainingStart = await _db.Trainings.AsNoTracking()
            .Where(training => training.StartDate <= today)
            .MaxAsync(training => training.StartDate, ct);
        // Some training records carry only a month and year.
        var monthOnly = await _db.Trainings.AsNoTracking()
            .Where(training => training.StartDate == null && training.TrainingYear != null && training.TrainingMonth != null)
            .Select(training => new { Year = training.TrainingYear!.Value, Month = training.TrainingMonth!.Value })
            .ToListAsync(ct);
        var lastTrainingMonth = monthOnly
            .Where(item => item.Month is >= 1 and <= 12 && item.Year is > 0 and < 10000)
            .Select(item => (DateOnly?)new DateOnly(item.Year, item.Month, 1))
            .Where(date => date <= today)
            .Max();
        var lastProliferation = await _db.ProliferationGranularEntries.AsNoTracking()
            .Where(entry => entry.ApprovalStatus != ApprovalStatus.Rejected && entry.ProliferationDate <= today)
            .MaxAsync(entry => (DateOnly?)entry.ProliferationDate, ct);
        var lastSocial = await _db.SocialMediaEvents.AsNoTracking()
            .Where(item => item.DateOfEvent <= today)
            .MaxAsync(item => (DateOnly?)item.DateOfEvent, ct);

        var last = new Dictionary<string, DateOnly?>(StringComparer.Ordinal)
        {
            ["Visits"] = lastVisit,
            ["Misc activities"] = lastActivity.HasValue ? IstDate(lastActivity.Value) : null,
            ["Training"] = Later(lastTrainingStart, lastTrainingMonth),
            ["Proliferation"] = lastProliferation,
            ["Social media"] = lastSocial
        };

        return Registers
            .Select(register => new CommandDeskRegisterInput(register.Label, last.GetValueOrDefault(register.Label), register.ExpectedDays, register.Url))
            .ToList();
    }

    // SECTION: Helpers
    private async Task<IReadOnlyDictionary<string, string>> LoadNamesAsync(IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return await _db.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(
                user => user.Id,
                user => string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? user.Id : user.FullName,
                StringComparer.Ordinal,
                ct);
    }

    private static string OfficerName(CommandOfficerWorkloadVm officer)
        => string.IsNullOrWhiteSpace(officer.OfficerName) ? "Unnamed officer" : officer.OfficerName;

    private static DateOnly? Later(DateOnly? a, DateOnly? b)
        => a is null ? b : b is null ? a : (a > b ? a : b);

    private static DateOnly IstDate(DateTimeOffset utc) => DateOnly.FromDateTime(IstClock.ToIst(utc).DateTime);

    private static DateOnly IstDate(DateTime utc) => DateOnly.FromDateTime(IstClock.ToIst(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));
}
