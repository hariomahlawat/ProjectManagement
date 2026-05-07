using System;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Models;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskRouteStateHelper
{
    // SECTION: Shared task redirect route preserves workspace, selection, and relevant list state from inspector actions.
    public RouteValueDictionary BuildTaskWorkspaceRouteValues(ActionTaskRouteState state, int? taskId, int? selectedSprintId)
    {
        var routeValues = new RouteValueDictionary
        {
            [nameof(ActionTaskRouteState.ViewMode)] = state.ViewMode,
            [nameof(ActionTaskRouteState.TaskId)] = taskId,
            [nameof(ActionTaskRouteState.SelectedSprintId)] = selectedSprintId
        };

        if (state.IsPlanningView && !string.Equals(state.PlanningTab, state.DefaultPlanningTab, StringComparison.OrdinalIgnoreCase))
        {
            routeValues[nameof(ActionTaskRouteState.PlanningTab)] = state.PlanningTab;
        }

        if (state.IsPlanningView && !string.Equals(state.PlanningView, "Default", StringComparison.OrdinalIgnoreCase))
        {
            routeValues[nameof(ActionTaskRouteState.PlanningView)] = state.PlanningView;
        }

        if (state.ShouldPreserveTaskFilters)
        {
            AddTaskFilterRouteValues(routeValues, state);
        }

        return routeValues;
    }

    // SECTION: Canonical route state composition keeps query-string preservation outside the page model.
    public ActionTaskRouteState BuildRouteState(ActionTaskRouteInputs inputs, int? taskId, int? selectedSprintId, ActionSprint? selectedSprint, ActionSprint? activeSprint)
    {
        var viewMode = ResolveViewMode(inputs.ViewMode);
        var planningView = ResolvePlanningView(inputs.ViewMode, inputs.PlanningView);
        var defaultPlanningTab = GetDefaultPlanningTab(selectedSprintId, selectedSprint, activeSprint);
        var planningTab = ResolvePlanningTab(inputs.PlanningTab, planningView, defaultPlanningTab);
        var isPlanningView = string.Equals(viewMode, "Planning", StringComparison.OrdinalIgnoreCase);
        var isBacklogView = IsBacklogView(inputs, viewMode, planningView);
        var shouldPreserveTaskFilters = string.Equals(viewMode, "Register", StringComparison.OrdinalIgnoreCase) || isBacklogView;

        return new ActionTaskRouteState(
            viewMode,
            planningTab,
            planningView,
            defaultPlanningTab,
            isPlanningView,
            shouldPreserveTaskFilters,
            taskId,
            selectedSprintId,
            inputs.FilterStatus,
            inputs.FilterPriority,
            inputs.FilterAssigneeUserId,
            inputs.FilterDueDate,
            inputs.FilterSearch,
            inputs.SortBy,
            inputs.SortDir);
    }

    // SECTION: View-mode checks centralize canonical and legacy workspace aliases for page projections.
    public bool IsBacklogView(ActionTaskRouteInputs inputs, string resolvedViewMode, string resolvedPlanningView)
        => IsLegacyViewMode(inputs.ViewMode, "Backlog")
            || (string.Equals(resolvedViewMode, "Planning", StringComparison.OrdinalIgnoreCase)
                && string.Equals(resolvedPlanningView, "Default", StringComparison.OrdinalIgnoreCase)
                && HasTaskFilterRouteState(inputs));

    // SECTION: Task-list filter detection covers GET bookmarks and required postback route preservation.
    public bool HasTaskFilterRouteState(ActionTaskRouteInputs inputs)
        => !string.IsNullOrWhiteSpace(inputs.FilterStatus)
            || !string.IsNullOrWhiteSpace(inputs.FilterPriority)
            || !string.IsNullOrWhiteSpace(inputs.FilterAssigneeUserId)
            || inputs.FilterDueDate.HasValue
            || !string.IsNullOrWhiteSpace(inputs.FilterSearch)
            || !string.IsNullOrWhiteSpace(inputs.SortBy)
            || !string.IsNullOrWhiteSpace(inputs.SortDir);

    // SECTION: Register and legacy backlog filters auto-apply without moving query logic into the page model.
    public bool HasActiveTaskFilters(ActionTaskRouteInputs inputs, string resolvedViewMode, bool isBacklogView)
        => (string.Equals(resolvedViewMode, "Register", StringComparison.OrdinalIgnoreCase) || isBacklogView)
            && HasTaskFilterRouteState(inputs with { SortBy = null, SortDir = null });

    // SECTION: Reports filter-state detection preserves auto-apply query-string behavior.
    public bool HasReportFilters(ActionTaskRouteInputs inputs, string resolvedViewMode)
        => string.Equals(resolvedViewMode, "Reports", StringComparison.OrdinalIgnoreCase)
            && (inputs.ReportSprintId.HasValue
                || !string.IsNullOrWhiteSpace(inputs.ReportAssigneeUserId)
                || inputs.ReportFromDate.HasValue
                || inputs.ReportToDate.HasValue
                || !string.IsNullOrWhiteSpace(inputs.ReportStatus)
                || !string.IsNullOrWhiteSpace(inputs.ReportPriority));

    // SECTION: View-mode normalization keeps old bookmarks rendering modern workspaces without GET redirects.
    public string ResolveViewMode(string? viewMode)
    {
        var normalized = (viewMode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "CommandCentre";
        }

        return normalized switch
        {
            _ when IsViewModeAlias(normalized, "Dashboard") => "CommandCentre",
            _ when IsViewModeAlias(normalized, "Sprints") => "Planning",
            _ when IsViewModeAlias(normalized, "Backlog") => "Planning",
            _ when IsViewModeAlias(normalized, "Sprint") => "Planning",
            _ when IsViewModeAlias(normalized, "Due Board") => "Planning",
            _ when IsViewModeAlias(normalized, "SprintBoard") => "Planning",
            _ when IsViewModeAlias(normalized, "Sprint Board") => "Planning",
            _ when IsViewModeAlias(normalized, "Kanban") => "Planning",
            _ when IsViewModeAlias(normalized, "MyTasks") => "MyWork",
            _ when IsViewModeAlias(normalized, "My Tasks") => "MyWork",
            _ when IsViewModeAlias(normalized, "TaskList") => "Register",
            _ when IsViewModeAlias(normalized, "CommandCentre") => "CommandCentre",
            _ when IsViewModeAlias(normalized, "Planning") => "Planning",
            _ when IsViewModeAlias(normalized, "MyWork") => "MyWork",
            _ when IsViewModeAlias(normalized, "Register") => "Register",
            _ when IsViewModeAlias(normalized, "Reports") => "Reports",
            _ => "CommandCentre"
        };
    }

    // SECTION: Sprint Board sub-view normalization keeps secondary view state safe across links and postbacks.
    public string ResolvePlanningView(string? viewMode, string? planningView)
    {
        var normalized = (planningView ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized switch
            {
                _ when string.Equals(normalized, "DueExceptions", StringComparison.OrdinalIgnoreCase) => "DueExceptions",
                _ when string.Equals(normalized, "Due / exceptions", StringComparison.OrdinalIgnoreCase) => "DueExceptions",
                _ when string.Equals(normalized, "Due Board", StringComparison.OrdinalIgnoreCase) => "DueExceptions",
                _ when string.Equals(normalized, "Sprint Board", StringComparison.OrdinalIgnoreCase) => "DueExceptions",
                _ when string.Equals(normalized, "Kanban", StringComparison.OrdinalIgnoreCase) => "Kanban",
                _ => "Default"
            };
        }

        return (viewMode ?? string.Empty).Trim() switch
        {
            var legacyView when string.Equals(legacyView, "Kanban", StringComparison.OrdinalIgnoreCase) => "Kanban",
            var legacyView when string.Equals(legacyView, "Due Board", StringComparison.OrdinalIgnoreCase) => "DueExceptions",
            var legacyView when string.Equals(legacyView, "SprintBoard", StringComparison.OrdinalIgnoreCase) => "DueExceptions",
            var legacyView when string.Equals(legacyView, "Sprint Board", StringComparison.OrdinalIgnoreCase) => "DueExceptions",
            _ => "Default"
        };
    }

    // SECTION: Sprint Board internal tab normalization keeps planning, execution, closure, and alternate views separated.
    public string ResolvePlanningTab(string? planningTab, string resolvedPlanningView, string defaultPlanningTab)
    {
        var normalized = (planningTab ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized switch
            {
                _ when string.Equals(normalized, "Plan", StringComparison.OrdinalIgnoreCase) => "Plan",
                _ when string.Equals(normalized, "Planning", StringComparison.OrdinalIgnoreCase) => "Plan",
                _ when string.Equals(normalized, "Backlog", StringComparison.OrdinalIgnoreCase) => "Plan",
                _ when string.Equals(normalized, "Execute", StringComparison.OrdinalIgnoreCase) => "Execute",
                _ when string.Equals(normalized, "Execution", StringComparison.OrdinalIgnoreCase) => "Execute",
                _ when string.Equals(normalized, "Close", StringComparison.OrdinalIgnoreCase) => "Close",
                _ when string.Equals(normalized, "Closure", StringComparison.OrdinalIgnoreCase) => "Close",
                _ when string.Equals(normalized, "Views", StringComparison.OrdinalIgnoreCase) => "Views",
                _ when string.Equals(normalized, "View", StringComparison.OrdinalIgnoreCase) => "Views",
                _ => defaultPlanningTab
            };
        }

        return !string.Equals(resolvedPlanningView, "Default", StringComparison.OrdinalIgnoreCase)
            ? "Views"
            : defaultPlanningTab;
    }

    // SECTION: Planning tab default follows selected sprint availability.
    public string GetDefaultPlanningTab(int? selectedSprintId, ActionSprint? selectedSprint, ActionSprint? activeSprint)
        => selectedSprintId.HasValue || selectedSprint is not null || activeSprint is not null
            ? "Execute"
            : "Plan";

    // SECTION: Legacy view-state matching isolates backward-compatible aliases from canonical workspace names.
    public bool IsLegacyViewMode(string? viewMode, string legacyViewMode)
        => string.Equals((viewMode ?? string.Empty).Trim(), legacyViewMode, StringComparison.OrdinalIgnoreCase);

    // SECTION: Filter route values are added only when populated so canonical links stay compact.
    private static void AddTaskFilterRouteValues(RouteValueDictionary routeValues, ActionTaskRouteState state)
    {
        AddRouteValueIfPresent(routeValues, nameof(ActionTaskRouteState.FilterStatus), state.FilterStatus);
        AddRouteValueIfPresent(routeValues, nameof(ActionTaskRouteState.FilterPriority), state.FilterPriority);
        AddRouteValueIfPresent(routeValues, nameof(ActionTaskRouteState.FilterAssigneeUserId), state.FilterAssigneeUserId);
        if (state.FilterDueDate.HasValue)
        {
            routeValues[nameof(ActionTaskRouteState.FilterDueDate)] = state.FilterDueDate.Value.ToString("yyyy-MM-dd");
        }

        AddRouteValueIfPresent(routeValues, nameof(ActionTaskRouteState.FilterSearch), state.FilterSearch);
        AddRouteValueIfPresent(routeValues, nameof(ActionTaskRouteState.SortBy), state.SortBy);
        AddRouteValueIfPresent(routeValues, nameof(ActionTaskRouteState.SortDir), state.SortDir);
    }

    // SECTION: Route value helper avoids emitting empty query-string keys for offline-safe generated URLs.
    private static void AddRouteValueIfPresent(RouteValueDictionary routeValues, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            routeValues[key] = value.Trim();
        }
    }

    // SECTION: Alias comparison centralizes case-insensitive matching for old and canonical view modes.
    private static bool IsViewModeAlias(string normalizedViewMode, string candidate)
        => string.Equals(normalizedViewMode, candidate, StringComparison.OrdinalIgnoreCase);
}

public sealed record ActionTaskRouteInputs(
    string? ViewMode,
    string? PlanningView,
    string? PlanningTab,
    string? FilterStatus,
    string? FilterPriority,
    string? FilterAssigneeUserId,
    DateTime? FilterDueDate,
    string? FilterSearch,
    string? SortBy,
    string? SortDir,
    int? ReportSprintId,
    string? ReportAssigneeUserId,
    DateTime? ReportFromDate,
    DateTime? ReportToDate,
    string? ReportStatus,
    string? ReportPriority);

public sealed record ActionTaskRouteState(
    string ViewMode,
    string PlanningTab,
    string PlanningView,
    string DefaultPlanningTab,
    bool IsPlanningView,
    bool ShouldPreserveTaskFilters,
    int? TaskId,
    int? SelectedSprintId,
    string? FilterStatus,
    string? FilterPriority,
    string? FilterAssigneeUserId,
    DateTime? FilterDueDate,
    string? FilterSearch,
    string? SortBy,
    string? SortDir);
