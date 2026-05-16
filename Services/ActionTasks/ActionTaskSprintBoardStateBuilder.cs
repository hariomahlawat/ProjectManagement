using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskSprintBoardStateBuilder
{
    private readonly ActionSprintService _sprintService;
    private readonly ActionTaskUserLookupService _userLookup;
    private readonly IActionTrackerClock _clock;

    public ActionTaskSprintBoardStateBuilder(ActionSprintService sprintService, ActionTaskUserLookupService userLookup, IActionTrackerClock clock)
    {
        _sprintService = sprintService;
        _userLookup = userLookup;
        _clock = clock;
    }

    // SECTION: Sprint Board state composition centralizes panel defaults and selected sprint audit preparation.
    public async Task<ActionTaskSprintBoardState> BuildAsync(ActionTaskSprintBoardStateRequest request)
    {
        var auditHistory = await LoadAuditHistoryAsync(request.SelectedSprint);
        var actorNames = auditHistory.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await _userLookup.LoadSprintActorNamesAsync(auditHistory);

        return new ActionTaskSprintBoardState(
            request.ShowCreateSprintPanel ? null : CreateSprintDefaults.FromDate(_clock.IstToday),
            request.SelectedSprint is not null && !request.ShowEditSprintPanel ? UpdateSprintDefaults.FromSprint(request.SelectedSprint) : null,
            request.SelectedSprint is not null && !request.ShowCreateNextSprintPanel ? CreateNextSprintDefaults.FromSourceSprint(request.SelectedSprint) : null,
            auditHistory,
            actorNames);
    }

    // SECTION: Selected sprint audit loading preserves existing empty-state behaviour when no sprint is selected.
    private async Task<IReadOnlyList<ActionSprintAuditLog>> LoadAuditHistoryAsync(ActionSprint? selectedSprint)
        => selectedSprint is null ? Array.Empty<ActionSprintAuditLog>() : await _sprintService.GetSprintAuditHistoryAsync(selectedSprint.Id);
}

public sealed record ActionTaskSprintBoardStateRequest(
    ActionSprint? SelectedSprint,
    bool ShowCreateSprintPanel,
    bool ShowEditSprintPanel,
    bool ShowCreateNextSprintPanel);

public sealed record ActionTaskSprintBoardState(
    CreateSprintDefaults? CreateDefaults,
    UpdateSprintDefaults? EditDefaults,
    CreateNextSprintDefaults? NextDefaults,
    IReadOnlyList<ActionSprintAuditLog> AuditHistory,
    IReadOnlyDictionary<string, string> ActorNames);

public sealed record CreateSprintDefaults(string Name, string? Goal, DateTime StartDate, DateTime EndDate)
{
    public static CreateSprintDefaults FromDate(DateTime today)
    {
        // SECTION: Default to editable half-month command sprints.
        var start = today.Day <= 15 ? new DateTime(today.Year, today.Month, 1) : new DateTime(today.Year, today.Month, 16);
        var end = today.Day <= 15 ? new DateTime(today.Year, today.Month, 15) : new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        return new CreateSprintDefaults($"Sprint {start:dd MMM} - {end:dd MMM}", null, start, end);
    }
}

public sealed record UpdateSprintDefaults(int SprintId, string RowVersion, string Name, string? Goal, DateTime StartDate, DateTime EndDate)
{
    public static UpdateSprintDefaults FromSprint(ActionSprint sprint)
        => new(sprint.Id, Convert.ToBase64String(sprint.RowVersion), sprint.Name, sprint.Goal, sprint.StartDate, sprint.EndDate);
}

public sealed record CreateNextSprintDefaults(int SourceSprintId, string Name, string? Goal, DateTime StartDate, DateTime EndDate)
{
    public static CreateNextSprintDefaults FromSourceSprint(ActionSprint sprint)
    {
        // SECTION: Closure-review next sprint defaults immediately follow source sprint end.
        var start = sprint.EndDate.Date.AddDays(1);
        var end = start.AddDays(14);
        return new CreateNextSprintDefaults(sprint.Id, $"Sprint {start:dd MMM} - {end:dd MMM}", sprint.Goal, start, end);
    }
}
