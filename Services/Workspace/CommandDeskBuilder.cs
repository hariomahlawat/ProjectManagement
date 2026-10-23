using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

// SECTION: Inputs
// CommandDeskService loads these facts; the rules that turn them into the desk live here so they
// can be tested without a database.
public sealed record CommandDeskInput(
    DateOnly Today,
    int WindowDays,
    IReadOnlyList<CommandDeskProgressEvent> Progress,
    IReadOnlyList<CommandDeskDecisionInput> Decisions,
    bool CanOpenDecisionCentre,
    IReadOnlyList<CommandDeskProjectInput> Projects,
    IReadOnlyList<CommandDeskDirectionInput> Directions,
    IReadOnlyList<CommandDeskTaskInput> Tasks,
    IReadOnlyList<CommandDeskReturnedInput> Returned,
    IReadOnlyList<CommandDeskRegisterInput> Registers,
    string? ViewerUserId = null);

public enum CommandDeskProgressKind
{
    StageCompleted,
    ProjectCompleted,
    TotCompleted,
    IprFiled,
    IprGranted,
    UnitsProliferated,
    FfcDelivered,
    FfcInstalled
}

/// <summary>One dated piece of good news. Quantity is the number of units for proliferation, else 1.</summary>
public sealed record CommandDeskProgressEvent(
    CommandDeskProgressKind Kind,
    DateOnly On,
    int Quantity = 1,
    string? Text = null,
    string? Meta = null,
    string? Url = null);

public sealed record CommandDeskDecisionInput(
    string Title,
    string TypeLabel,
    string ByName,
    DateTimeOffset RequestedAtUtc,
    string ActionLabel,
    string Url);

/// <summary>
/// An active project with its assigned officer and current stage. PlanReviewUrl is set when the
/// viewer can decide the plan waiting for approval.
/// </summary>
public sealed record CommandDeskProjectInput(
    int ProjectId,
    string Name,
    string OfficerUserId,
    string OfficerName,
    string StageName,
    DateOnly? Pdc,
    bool PlanWithHod,
    DateOnly? LastOfficerUpdate,
    string? PlanReviewUrl = null);

public sealed record CommandDeskDirectionInput(
    ConferenceItemKind Kind,
    string Title,
    string OfficerName,
    string DirectionText,
    string IssuedBy,
    DateOnly IssuedOn,
    bool Answered,
    string Url,
    string? OfficerUserId = null);

public sealed record CommandDeskTaskInput(
    int TaskId,
    string Title,
    string AssigneeName,
    string AssignerName,
    string Priority,
    string Status,
    DateOnly Due,
    string? BlockedReason,
    string? AssignerUserId = null,
    DateTimeOffset? BlockedSinceUtc = null);

public sealed record CommandDeskReturnedInput(int ProjectId, string OfficerUserId, string OfficerName);

public sealed record CommandDeskRegisterInput(string Label, DateOnly? LastOn, int ExpectedDays, string Url);

public static class CommandDeskBuilder
{
    public const int PdcWindowDays = 30;
    public const int TaskDueSoonDays = 7;
    public const int DirectionHighlightDays = 7;
    public const int StaleUpdateDays = 10;
    public const int WinLimit = 5;

    public static int NormaliseWindow(int? days) => days == 15 ? 15 : 30;

    public static CommandDeskVm Build(CommandDeskInput input)
    {
        var today = input.Today;
        var windowDays = NormaliseWindow(input.WindowDays);
        var pdc = BuildPdcWatch(input.Projects, today);
        var directions = BuildDirections(input.Directions, today, input.ViewerUserId);
        var tasks = BuildTasks(input.Tasks, today);
        var decisions = input.Decisions
            .Concat(BlockedForViewer(input.Tasks, input.ViewerUserId, today))
            .OrderBy(item => item.RequestedAtUtc)
            .Select(item => new CommandDeskDecisionVm(
                item.Title,
                item.TypeLabel,
                item.ByName,
                AgeLabel(today.DayNumber - DateOnly.FromDateTime(IstDate(item.RequestedAtUtc)).DayNumber),
                item.ActionLabel,
                item.Url))
            .ToList();
        var quiet = input.Registers
            .Select(register => new
            {
                register,
                days = register.LastOn.HasValue ? today.DayNumber - register.LastOn.Value.DayNumber : (int?)null
            })
            .ToList();
        var quietRegisters = quiet
            .Where(item => item.days is null || item.days > item.register.ExpectedDays)
            .OrderByDescending(item => item.days ?? int.MaxValue)
            .ThenBy(item => item.register.Label, StringComparer.OrdinalIgnoreCase)
            .Select(item => new CommandDeskRegisterVm(item.register.Label, item.register.LastOn, item.days, item.register.ExpectedDays, item.register.Url))
            .ToList();

        return new CommandDeskVm
        {
            Today = today,
            WindowDays = windowDays,
            MovedForward = BuildCounts(input.Progress, today, windowDays),
            Wins = BuildWins(input.Progress, today, windowDays),
            WaitingOnYou = decisions,
            WaitingOnYouTotal = decisions.Count,
            CanOpenDecisionCentre = input.CanOpenDecisionCentre,
            PdcWatch = pdc,
            PdcOverdueCount = pdc.Count(item => item.IsOverdue),
            PdcDueSoonCount = pdc.Count(item => !item.IsOverdue),
            Directions = directions,
            DirectionsOlderThanWeekCount = directions.Count(item => item.IsOlderThanWeek),
            Tasks = tasks,
            TasksBlockedCount = input.Tasks.Count(task => IsOpenForLag(task) && IsBlocked(task)),
            TasksOverdueCount = input.Tasks.Count(task => IsOpenForLag(task) && !IsBlocked(task) && task.Due < today),
            TasksDueSoonCount = input.Tasks.Count(task => IsOpenForLag(task) && !IsBlocked(task) && task.Due >= today && task.Due.DayNumber - today.DayNumber <= TaskDueSoonDays),
            Attention = BuildAttention(input.Projects, input.Returned, today, input.ViewerUserId),
            NothingReturned = input.Returned.Count == 0,
            QuietRegisters = quietRegisters,
            CurrentRegisterCount = quiet.Count - quietRegisters.Count
        };
    }

    // SECTION: What moved forward
    private static IReadOnlyList<CommandDeskCountVm> BuildCounts(
        IReadOnlyList<CommandDeskProgressEvent> progress,
        DateOnly today,
        int windowDays)
    {
        var inWindow = progress.Where(item => InWindow(item.On, today, windowDays)).ToList();
        var counts = new List<CommandDeskCountVm>();
        void Add(CommandDeskProgressKind kind, string singular, string plural, string? url)
        {
            var total = inWindow.Where(item => item.Kind == kind).Sum(item => item.Quantity);
            if (total > 0)
            {
                counts.Add(new CommandDeskCountVm(total, total == 1 ? singular : plural, url));
            }
        }

        Add(CommandDeskProgressKind.StageCompleted, "stage completed", "stages completed", null);
        Add(CommandDeskProgressKind.ProjectCompleted, "project completed", "projects completed", "/Projects?Lifecycle=Completed");
        Add(CommandDeskProgressKind.TotCompleted, "ToT completed", "ToTs completed", CommandDeskRoutes.Tot);
        Add(CommandDeskProgressKind.IprFiled, "IPR filed", "IPRs filed", CommandDeskRoutes.Ipr);
        Add(CommandDeskProgressKind.IprGranted, "IPR granted", "IPRs granted", CommandDeskRoutes.Ipr);
        Add(CommandDeskProgressKind.UnitsProliferated, "unit proliferated", "units proliferated", CommandDeskRoutes.Proliferation);
        Add(CommandDeskProgressKind.FfcDelivered, "FFC delivery", "FFC deliveries", CommandDeskRoutes.Ffc);
        Add(CommandDeskProgressKind.FfcInstalled, "FFC installation", "FFC installations", CommandDeskRoutes.Ffc);
        return counts;
    }

    private static IReadOnlyList<CommandDeskWinVm> BuildWins(
        IReadOnlyList<CommandDeskProgressEvent> progress,
        DateOnly today,
        int windowDays)
        => progress
            .Where(item => !string.IsNullOrWhiteSpace(item.Text) && InWindow(item.On, today, windowDays))
            .OrderByDescending(item => item.On)
            .ThenBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
            .Take(WinLimit)
            .Select(item => new CommandDeskWinVm(
                item.Text!,
                string.IsNullOrWhiteSpace(item.Meta) ? item.On.ToString("dd MMM") : $"{item.Meta} · {item.On:dd MMM}",
                item.On,
                item.Url))
            .ToList();

    // Events dated in the future do not count as progress yet.
    private static bool InWindow(DateOnly on, DateOnly today, int windowDays)
        => on <= today && on > today.AddDays(-windowDays);

    // SECTION: PDC watch
    private static IReadOnlyList<CommandDeskPdcVm> BuildPdcWatch(IReadOnlyList<CommandDeskProjectInput> projects, DateOnly today)
        => projects
            .Where(project => project.Pdc.HasValue && project.Pdc.Value.DayNumber - today.DayNumber <= PdcWindowDays)
            .OrderBy(project => project.Pdc!.Value)
            .ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .Select(project =>
            {
                var days = project.Pdc!.Value.DayNumber - today.DayNumber;
                return new CommandDeskPdcVm(
                    project.ProjectId,
                    project.Name,
                    project.OfficerName,
                    project.StageName,
                    project.Pdc.Value,
                    days,
                    DueLabel(days),
                    days < 0,
                    !project.PlanWithHod ? null
                        : project.PlanReviewUrl is null ? "Revised plan with HoD" : "Revised plan · waiting on you",
                    WorkspaceRouteHelper.ProjectOverview(project.ProjectId),
                    project.PlanWithHod ? project.PlanReviewUrl : null);
            })
            .ToList();

    // SECTION: Directions awaiting progress
    private static IReadOnlyList<CommandDeskDirectionVm> BuildDirections(
        IReadOnlyList<CommandDeskDirectionInput> directions,
        DateOnly today,
        string? viewerUserId = null)
        => directions
            .Where(direction => !direction.Answered)
            .OrderBy(direction => direction.IssuedOn)
            .ThenBy(direction => direction.Title, StringComparer.OrdinalIgnoreCase)
            .Select(direction =>
            {
                var age = Math.Max(0, today.DayNumber - direction.IssuedOn.DayNumber);
                return new CommandDeskDirectionVm(
                    direction.Title,
                    direction.Kind switch
                    {
                        ConferenceItemKind.ProjectIdea => "Idea",
                        ConferenceItemKind.ActionTask => "Task",
                        _ => "Project"
                    },
                    direction.OfficerName,
                    direction.DirectionText,
                    direction.IssuedBy,
                    direction.IssuedOn,
                    age,
                    age == 0 ? "Issued today" : $"{age} {Days(age)}, no reply",
                    age > DirectionHighlightDays,
                    direction.Url,
                    MessageUrl(direction.OfficerUserId, viewerUserId));
            })
            .ToList();

    // SECTION: Tasks
    // Submitted tasks wait on the assigner, not the assignee, so they belong under Waiting on you.
    // Backlog tasks are deferred and unassigned, so an old due date there is not lagging work.
    private static bool IsOpenForLag(CommandDeskTaskInput task)
        => !string.Equals(task.Status, Models.ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(task.Status, Models.ActionTaskStatuses.Backlog, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(task.Status, Models.ActionTaskStatuses.Submitted, StringComparison.OrdinalIgnoreCase);

    private static bool IsBlocked(CommandDeskTaskInput task)
        => string.Equals(task.Status, Models.ActionTaskStatuses.Blocked, StringComparison.OrdinalIgnoreCase);

    // A blocked task usually needs its assigner (the reason often says "escalated"), so a task
    // the viewer assigned that is blocked also waits on the viewer. It stays under Tasks as well,
    // which everyone sees.
    private static IEnumerable<CommandDeskDecisionInput> BlockedForViewer(
        IReadOnlyList<CommandDeskTaskInput> tasks,
        string? viewerUserId,
        DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(viewerUserId))
        {
            return Array.Empty<CommandDeskDecisionInput>();
        }

        return tasks
            .Where(task => IsOpenForLag(task) && IsBlocked(task)
                && string.Equals(task.AssignerUserId, viewerUserId, StringComparison.Ordinal))
            .Select(task => new CommandDeskDecisionInput(
                task.Title,
                string.IsNullOrWhiteSpace(task.BlockedReason) ? "Task blocked" : $"Task blocked: “{Clip(task.BlockedReason!.Trim(), 120)}”",
                task.AssigneeName,
                task.BlockedSinceUtc ?? new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                "Open task",
                CommandDeskRoutes.Task(task.TaskId)));
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    private static IReadOnlyList<CommandDeskTaskVm> BuildTasks(IReadOnlyList<CommandDeskTaskInput> tasks, DateOnly today)
        => tasks
            .Where(IsOpenForLag)
            .Select(task => new { task, days = task.Due.DayNumber - today.DayNumber, blocked = IsBlocked(task) })
            .Where(item => item.blocked || item.days <= TaskDueSoonDays)
            .OrderByDescending(item => item.blocked || item.days < 0)
            .ThenBy(item => item.task.Due)
            .ThenBy(item => item.task.Title, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                var state = item.blocked
                    ? item.days < 0 ? $"blocked · {DueLabel(item.days)}" : "blocked"
                    : DueLabel(item.days);
                var who = $"{item.task.AssigneeName} · assigned by {item.task.AssignerName}";
                if (string.Equals(item.task.Priority, "High", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.task.Priority, "Urgent", StringComparison.OrdinalIgnoreCase))
                {
                    who += $" · {item.task.Priority}";
                }

                return new CommandDeskTaskVm(
                    item.task.Title,
                    who,
                    item.task.Due,
                    state,
                    item.blocked || item.days < 0,
                    item.blocked && !string.IsNullOrWhiteSpace(item.task.BlockedReason) ? item.task.BlockedReason!.Trim() : null,
                    CommandDeskRoutes.Task(item.task.TaskId));
            })
            .ToList();

    // SECTION: Needs attention
    // Each reason lists, by officer, the projects behind the count, so the HoD can open one
    // directly or message the officer, not only read a number.
    private static IReadOnlyList<CommandDeskAttentionVm> BuildAttention(
        IReadOnlyList<CommandDeskProjectInput> projects,
        IReadOnlyList<CommandDeskReturnedInput> returned,
        DateOnly today,
        string? viewerUserId = null)
    {
        var staleBefore = today.AddDays(-StaleUpdateDays);
        var names = projects
            .GroupBy(project => project.ProjectId)
            .ToDictionary(group => group.Key, group => group.First().Name);
        var result = new List<CommandDeskAttentionVm>();

        void Add(string key, string singular, string plural, IEnumerable<(string UserId, string Name, int ProjectId)> items)
        {
            var byOfficer = items
                .GroupBy(item => item.UserId, StringComparer.Ordinal)
                .Select(group => new CommandDeskOfficerCountVm(
                    group.First().Name,
                    group.Count(),
                    string.IsNullOrWhiteSpace(group.Key) ? null : CommandDeskRoutes.Conference(group.Key),
                    group
                        .GroupBy(item => item.ProjectId)
                        .Select(project => new CommandDeskAttentionProjectVm(
                            names.GetValueOrDefault(project.Key) ?? "Project",
                            WorkspaceRouteHelper.ProjectOverview(project.Key)))
                        .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    MessageUrl(group.Key, viewerUserId)))
                .OrderByDescending(officer => officer.Count)
                .ThenBy(officer => officer.OfficerName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var count = byOfficer.Sum(officer => officer.Count);
            if (count > 0)
            {
                result.Add(new CommandDeskAttentionVm(key, count, count == 1 ? singular : plural, byOfficer));
            }
        }

        Add("returned", "item returned by the HoD and not yet corrected", "items returned by the HoD and not yet corrected",
            returned.Select(item => (item.OfficerUserId, item.OfficerName, item.ProjectId)));
        // A plan waiting for approval is the officer's answer to a missing PDC (as on the officer's
        // own desk), so those projects are not counted here; the plan is under Waiting on you.
        Add("no-pdc", "project with no PDC on its current stage", "projects with no PDC on their current stage",
            projects.Where(project => !project.Pdc.HasValue && !project.PlanWithHod)
                .Select(project => (project.OfficerUserId, project.OfficerName, project.ProjectId)));
        Add("no-update", $"project with no update from its officer for {StaleUpdateDays}+ days", $"projects with no update from their officer for {StaleUpdateDays}+ days",
            projects.Where(project => !project.LastOfficerUpdate.HasValue || project.LastOfficerUpdate.Value <= staleBefore)
                .Select(project => (project.OfficerUserId, project.OfficerName, project.ProjectId)));
        return result;
    }

    /// <summary>A chat with the officer; none for the viewer themselves or an unknown officer.</summary>
    private static string? MessageUrl(string? officerUserId, string? viewerUserId)
        => string.IsNullOrWhiteSpace(officerUserId) || string.Equals(officerUserId, viewerUserId, StringComparison.Ordinal)
            ? null
            : CommandDeskRoutes.Message(officerUserId);

    // SECTION: Labels
    public static string DueLabel(int days) => days switch
    {
        < 0 => $"overdue {-days} {Days(-days)}",
        0 => "due today",
        1 => "due tomorrow",
        _ => $"due in {days} days"
    };

    private static string AgeLabel(int days) => days <= 0 ? "today" : $"{days} {Days(days)}";

    private static string Days(int count) => count == 1 ? "day" : "days";

    private static DateTime IstDate(DateTimeOffset utc) => Infrastructure.IstClock.ToIst(utc).DateTime;
}

internal static class CommandDeskRoutes
{
    public const string Desk = "/Workspace/CommandDesk";
    public const string DecisionCentre = "/Approvals/Pending";
    public const string TaskRegister = "/ActionTasks";
    public const string Visits = "/ProjectOfficeReports/Visits";
    public const string Training = "/ProjectOfficeReports/Training";
    public const string Proliferation = "/ProjectOfficeReports/Proliferation";
    public const string SocialMedia = "/ProjectOfficeReports/SocialMedia";
    public const string Activities = "/Activities";
    public const string Ipr = "/ProjectOfficeReports/Ipr";
    public const string Tot = "/ProjectOfficeReports/Tot";
    public const string Ffc = "/ProjectOfficeReports/FFC";

    public static string Task(int taskId) => $"/ActionTasks/Details/{taskId}";

    public static string Conference(string officerUserId) => $"/Workspace/Conference/{Uri.EscapeDataString(officerUserId)}";

    public static string Message(string userId) => $"/Chat?with={Uri.EscapeDataString(userId)}";

    public static string Approval(string type, string requestId)
        => $"/Approvals/Pending/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(requestId)}?returnUrl={Uri.EscapeDataString(Desk)}";
}
