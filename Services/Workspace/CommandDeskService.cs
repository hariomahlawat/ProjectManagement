using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Activities;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.Approvals;
using ProjectManagement.Services.Authorization;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;
using ProjectManagement.ViewModels.Workspace;
using IprStatusPolicy = ProjectManagement.Application.Ipr.IprStatusPolicy;

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

        var progress = await LoadProgressAsync(today, ct);
        var (decisions, planReviews) = await LoadDecisionsAsync(user, userId, canOpenDecisionCentre, ct);
        var projects = await LoadProjectsAsync(userId, today, planReviews, ct);
        var input = new CommandDeskInput(
            today,
            CommandDeskBuilder.NormaliseWindow(windowDays),
            progress,
            decisions,
            canOpenDecisionCentre,
            projects.Select(project => project.Input).ToList(),
            await LoadDirectionsAsync(userId, ct),
            await LoadTasksAsync(today, ct),
            await LoadRegistersAsync(today, ct),
            userId);

        return CommandDeskBuilder.Build(input);
    }

    // SECTION: What moved forward
    private async Task<IReadOnlyList<CommandDeskProgressEvent>> LoadProgressAsync(DateOnly today, CancellationToken ct)
    {
        var from = today.AddDays(-(MaxWindowDays - 1));
        var fromUtc = IstClock.StartOfDayIstToUtc(from);
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

        // The ToT tracker's own eligibility rule, so trashed, archived or repeat-build projects never count.
        var tots = await _db.ProjectTots
            .AsNoTracking()
            .Where(ProjectTotApplicabilityPolicy.EligibleTotPredicate)
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
            .Where(IprStatusPolicy.CountedPredicate)
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

        // Institutional outputs, by the same rules as the Dashboard's (visit date; a training's
        // headcount and reference date; an activity's occurrence, including industry and academia
        // interactions).
        var visits = await _db.Visits
            .AsNoTracking()
            .Where(visit => visit.DateOfVisit >= from && visit.DateOfVisit <= today)
            .Select(visit => visit.DateOfVisit)
            .ToListAsync(ct);
        events.AddRange(visits.Select(on => new CommandDeskProgressEvent(CommandDeskProgressKind.Visit, on)));

        var trainings = await _db.Trainings
            .AsNoTracking()
            .Where(TrainingReach.MayFallBetween(from, today))
            .Select(TrainingReach.Row)
            .ToListAsync(ct);
        foreach (var training in trainings)
        {
            if (training.Total > 0 && TrainingReach.ReferenceDate(training) is DateOnly on)
            {
                events.Add(new CommandDeskProgressEvent(CommandDeskProgressKind.PeopleTrained, on, training.Total));
            }
        }

        // Up to now, as on the Dashboard: an activity scheduled later today has not happened yet.
        var activities = await _db.Activities
            .AsNoTracking()
            .Where(ActivityOccurrence.Between(fromUtc, _clock.UtcNow))
            .Select(activity => activity.ScheduledStartUtc ?? activity.CreatedAtUtc)
            .ToListAsync(ct);
        events.AddRange(activities.Select(at => new CommandDeskProgressEvent(CommandDeskProgressKind.Activity, IstDate(at))));

        return events;
    }

    // SECTION: Waiting on you
    // Also returns, by project, the review link of each timeline plan the viewer can decide, so
    // the PDC watch can say the plan is waiting on them and link to it.
    private async Task<(IReadOnlyList<CommandDeskDecisionInput> Decisions, IReadOnlyDictionary<int, string> PlanReviews)> LoadDecisionsAsync(
        ClaimsPrincipal user,
        string userId,
        bool canOpenDecisionCentre,
        CancellationToken ct)
    {
        var decisions = new List<CommandDeskDecisionInput>();
        var planReviews = new Dictionary<int, string>();
        if (canOpenDecisionCentre)
        {
            // The Decision Centre's own queue, so the desk never disagrees with it.
            var pending = await _approvals.GetPendingAsync(new ApprovalQueueQuery(), user, ct);
            foreach (var plan in pending.Where(item => item.ApprovalType == ApprovalQueueType.PlanApproval && item.ProjectId.HasValue))
            {
                planReviews.TryAdd(plan.ProjectId!.Value, CommandDeskRoutes.Approval(plan.ApprovalType.ToString(), plan.RequestId));
            }

            decisions.AddRange(pending.Select(item => new CommandDeskDecisionInput(
                item.ProjectName ?? item.Summary,
                ApprovalTypeLabel(item),
                item.RequestedByName,
                item.RequestedAtUtc,
                "Review",
                CommandDeskRoutes.Approval(item.ApprovalType.ToString(), item.RequestId))));
        }

        // A task submitted for closure waits on whoever assigned it to its current assignee: the
        // creator, or whoever later assigned it from the backlog or reassigned it (ActionTaskAssigners).
        var submittedTasks = await _db.ActionTasks
            .AsNoTracking()
            .Where(task => !task.IsDeleted && task.Status == ActionTaskStatuses.Submitted)
            .Select(task => new { task.Id, task.Title, task.AssignedToUserId, task.CreatedByUserId, task.SubmittedOn, task.AssignedOn })
            .ToListAsync(ct);
        var submittedAssigners = await ActionTaskAssigners.ResolveAsync(
            _db, submittedTasks.Select(task => (task.Id, task.CreatedByUserId)).ToList(), ct);
        var submitted = submittedTasks
            .Where(task => string.Equals(submittedAssigners.GetValueOrDefault(task.Id), userId, StringComparison.Ordinal))
            .ToList();
        var names = await LoadNamesAsync(submitted.Select(task => task.AssignedToUserId), ct);
        decisions.AddRange(submitted.Select(task => new CommandDeskDecisionInput(
            task.Title,
            "Task submitted for closure",
            names.GetValueOrDefault(task.AssignedToUserId) ?? "Assignee",
            new DateTimeOffset(DateTime.SpecifyKind(task.SubmittedOn ?? task.AssignedOn, DateTimeKind.Utc)),
            "Close or return",
            CommandDeskRoutes.Task(task.Id))));

        return (decisions, planReviews);
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

    private async Task<IReadOnlyList<ProjectFacts>> LoadProjectsAsync(
        string userId,
        DateOnly today,
        IReadOnlyDictionary<int, string> planReviews,
        CancellationToken ct)
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

        // Only a plan the HoD can still act on: the same rule as the Decision Centre's queue, so an
        // older pending version left behind by a later rejected or withdrawn one does not count.
        var withHod = (await PlanRevisionConsistency.ActionablePending(_db)
                .AsNoTracking()
                .Where(plan => projectIds.Contains(plan.ProjectId))
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
                latestByOfficer.TryGetValue((item.project.ProjectId, item.officer.UserId), out var latest) ? latest : null,
                planReviews.GetValueOrDefault(item.project.ProjectId))))
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
                    CommandDeskRoutes.Conference(conference.OfficerUserId),
                    conference.OfficerUserId));
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
                && task.Status != ActionTaskStatuses.Backlog
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

        // Who gave each task to its current assignee: the creator, or whoever later assigned it
        // from the backlog or reassigned it (one rule with Task Management).
        var assigners = await ActionTaskAssigners.ResolveAsync(_db, tasks.Select(task => (task.Id, task.CreatedByUserId)).ToList(), ct);
        var names = await LoadNamesAsync(tasks.Select(task => task.AssignedToUserId).Concat(assigners.Values), ct);
        var blockedIds = tasks.Where(task => task.Status == ActionTaskStatuses.Blocked).Select(task => task.Id).ToArray();
        // The latest move to Blocked: its reason, and since when the task has been blocked.
        var blocks = await ActionTaskBlocks.LatestAsync(_db, blockedIds, ct);

        return tasks
            .Select(task => new CommandDeskTaskInput(
                task.Id,
                task.Title,
                names.GetValueOrDefault(task.AssignedToUserId) ?? "Unassigned",
                names.GetValueOrDefault(assigners.GetValueOrDefault(task.Id) ?? string.Empty) ?? "Unknown",
                task.Priority,
                task.Status,
                DateOnly.FromDateTime(task.DueDate),
                blocks.TryGetValue(task.Id, out var block) ? block.Reason : null,
                assigners.GetValueOrDefault(task.Id),
                blocks.TryGetValue(task.Id, out var since)
                    ? new DateTimeOffset(since.BlockedAtUtc)
                    : null))
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
