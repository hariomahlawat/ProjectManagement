using System;
using Microsoft.AspNetCore.Routing;

namespace ProjectManagement.Services.ActionTasks;

public sealed class ActionTaskRouteStateHelper
{

    // SECTION: Canonical top-level workspace normalization keeps legacy bookmarks working without redirects.
    public string ResolveViewMode(string? viewMode)
    {
        var normalized = (viewMode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "CommandCentre";
        }

        return normalized switch
        {
            _ when IsAlias(normalized, "Dashboard") => "CommandCentre",
            _ when IsAlias(normalized, "Sprints") => "Planning",
            _ when IsAlias(normalized, "Backlog") => "Planning",
            _ when IsAlias(normalized, "Sprint") => "Planning",
            _ when IsAlias(normalized, "Due Board") => "Planning",
            _ when IsAlias(normalized, "SprintBoard") => "Planning",
            _ when IsAlias(normalized, "Sprint Board") => "Planning",
            _ when IsAlias(normalized, "Kanban") => "Planning",
            _ when IsAlias(normalized, "MyTasks") => "MyWork",
            _ when IsAlias(normalized, "My Tasks") => "MyWork",
            _ when IsAlias(normalized, "TaskList") => "Register",
            _ when IsAlias(normalized, "CommandCentre") => "CommandCentre",
            _ when IsAlias(normalized, "Planning") => "Planning",
            _ when IsAlias(normalized, "MyWork") => "MyWork",
            _ when IsAlias(normalized, "Register") => "Register",
            _ when IsAlias(normalized, "Reports") => "Reports",
            _ => "CommandCentre"
        };
    }

    // SECTION: Sprint Board sub-view normalization keeps secondary view state safe across links and postbacks.
    public string ResolvePlanningView(string? planningView, string? viewMode)
    {
        var normalized = (planningView ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized switch
            {
                _ when IsAlias(normalized, "DueExceptions") => "DueExceptions",
                _ when IsAlias(normalized, "Due / exceptions") => "DueExceptions",
                _ when IsAlias(normalized, "Due Board") => "DueExceptions",
                _ when IsAlias(normalized, "Sprint Board") => "DueExceptions",
                _ when IsAlias(normalized, "Kanban") => "Default",
                _ => "Default"
            };
        }

        return (viewMode ?? string.Empty).Trim() switch
        {
            var legacyView when IsAlias(legacyView, "Kanban") => "Default",
            var legacyView when IsAlias(legacyView, "Due Board") => "DueExceptions",
            var legacyView when IsAlias(legacyView, "SprintBoard") => "DueExceptions",
            var legacyView when IsAlias(legacyView, "Sprint Board") => "DueExceptions",
            _ => "Default"
        };
    }

    // SECTION: Sprint Board internal tab normalization separates planning, execution, closure, and alternate views.
    public string ResolvePlanningTab(string? planningTab, string resolvedPlanningView, string defaultPlanningTab)
    {
        var normalized = (planningTab ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized switch
            {
                _ when IsAlias(normalized, "Plan") => "Plan",
                _ when IsAlias(normalized, "Planning") => "Plan",
                _ when IsAlias(normalized, "Backlog") => "Plan",
                _ when IsAlias(normalized, "Execute") => "Execute",
                _ when IsAlias(normalized, "Execution") => "Execute",
                _ when IsAlias(normalized, "Close") => "Close",
                _ when IsAlias(normalized, "Closure") => "Close",
                _ when IsAlias(normalized, "Views") => "Execute",
                _ when IsAlias(normalized, "View") => "Execute",
                _ => defaultPlanningTab
            };
        }

        return string.Equals(resolvedPlanningView, "Default", StringComparison.OrdinalIgnoreCase)
            ? defaultPlanningTab
            : "Execute";
    }


    // SECTION: Planning tab default follows selected or active sprint availability without page-model branching.
    public string ResolveDefaultPlanningTab(int? selectedSprintId, bool hasSelectedSprint, bool hasActiveSprint)
        => selectedSprintId.HasValue || hasSelectedSprint || hasActiveSprint ? "Execute" : "Plan";

    // SECTION: Page route-state assembly centralizes postback query preservation in the route helper.
    public ActionTaskRouteState BuildRouteState(ActionTaskRouteStateRequest request)
    {
        var viewMode = ResolveViewMode(request.ViewMode);
        var planningView = ResolvePlanningView(request.PlanningView, request.ViewMode);
        var defaultPlanningTab = ResolveDefaultPlanningTab(request.SelectedSprintId, request.HasSelectedSprint, request.HasActiveSprint);
        var isLegacyKanbanRoute = IsAlias(request.PlanningView ?? string.Empty, "Kanban") || IsAlias(request.ViewMode ?? string.Empty, "Kanban");
        var planningTab = string.IsNullOrWhiteSpace(request.PlanningTab) && isLegacyKanbanRoute
            ? "Execute"
            : ResolvePlanningTab(request.PlanningTab, planningView, defaultPlanningTab);
        var isPlanningView = string.Equals(viewMode, "Planning", StringComparison.OrdinalIgnoreCase);
        var shouldPreserveTaskFilters = string.Equals(viewMode, "Register", StringComparison.OrdinalIgnoreCase)
            || IsLegacyViewMode(request.ViewMode, "Backlog")
            || IsPlanningBacklogFilterContext(viewMode, planningView, request.FilterState);

        return new ActionTaskRouteState(
            viewMode,
            planningTab,
            planningView,
            defaultPlanningTab,
            isPlanningView,
            shouldPreserveTaskFilters,
            request.TaskId,
            request.SelectedSprintId,
            request.FilterState.FilterStatus,
            request.FilterState.FilterPriority,
            request.FilterState.FilterAssigneeUserId,
            request.FilterState.FilterDueDate,
            request.FilterState.FilterSearch,
            request.FilterState.SortBy,
            request.FilterState.SortDir,
            request.FilterState.FilterBucket);
    }

    // SECTION: Shared filter-state detection covers GET bookmarks and postback route preservation.
    public bool HasTaskFilterRouteState(ActionTaskFilterRouteState state)
        => !string.IsNullOrWhiteSpace(state.FilterStatus)
            || !string.IsNullOrWhiteSpace(state.FilterBucket)
            || !string.IsNullOrWhiteSpace(state.FilterPriority)
            || !string.IsNullOrWhiteSpace(state.FilterAssigneeUserId)
            || state.FilterDueDate.HasValue
            || !string.IsNullOrWhiteSpace(state.FilterSearch)
            || !string.IsNullOrWhiteSpace(state.SortBy)
            || !string.IsNullOrWhiteSpace(state.SortDir);


    // SECTION: Planning backlog filter detection keeps canonical Planning routes compatible with legacy Backlog filtered views.
    public bool IsPlanningBacklogFilterContext(string resolvedViewMode, string resolvedPlanningView, ActionTaskFilterRouteState filterState)
        => string.Equals(resolvedViewMode, "Planning", StringComparison.OrdinalIgnoreCase)
            && string.Equals(resolvedPlanningView, "Default", StringComparison.OrdinalIgnoreCase)
            && HasTaskFilterRouteState(filterState);

    // SECTION: Shared report filter-state detection covers auto-applied report GET filters.
    public bool HasReportFilterRouteState(ActionTaskReportFilterRouteState state)
        => !string.IsNullOrWhiteSpace(state.ReportBucket)
            || state.ReportSprintId.HasValue
            || !string.IsNullOrWhiteSpace(state.ReportAssigneeUserId)
            || state.ReportFromDate.HasValue
            || state.ReportToDate.HasValue
            || !string.IsNullOrWhiteSpace(state.ReportStatus)
            || !string.IsNullOrWhiteSpace(state.ReportPriority);

    public bool IsLegacyViewMode(string? viewMode, string legacyViewMode)
        => string.Equals((viewMode ?? string.Empty).Trim(), legacyViewMode, StringComparison.OrdinalIgnoreCase);

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

    // SECTION: Filter route values are added only when populated so canonical links stay compact.
    private static void AddTaskFilterRouteValues(RouteValueDictionary routeValues, ActionTaskRouteState state)
    {
        AddRouteValueIfPresent(routeValues, nameof(ActionTaskRouteState.FilterStatus), state.FilterStatus);
        AddRouteValueIfPresent(routeValues, nameof(ActionTaskRouteState.FilterBucket), state.FilterBucket);
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
    private static bool IsAlias(string normalizedValue, string candidate)
        => string.Equals(normalizedValue, candidate, StringComparison.OrdinalIgnoreCase);

    private static void AddRouteValueIfPresent(RouteValueDictionary routeValues, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            routeValues[key] = value.Trim();
        }
    }
}


public sealed record ActionTaskRouteStateRequest(
    string? ViewMode,
    string? PlanningTab,
    string? PlanningView,
    int? TaskId,
    int? SelectedSprintId,
    bool HasSelectedSprint,
    bool HasActiveSprint,
    ActionTaskFilterRouteState FilterState);

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
    string? SortDir,
    string? FilterBucket = null);

public sealed record ActionTaskFilterRouteState(
    string? FilterStatus,
    string? FilterPriority,
    string? FilterAssigneeUserId,
    DateTime? FilterDueDate,
    string? FilterSearch,
    string? SortBy,
    string? SortDir,
    string? FilterBucket = null);

public sealed record ActionTaskReportFilterRouteState(
    string? ReportBucket,
    int? ReportSprintId,
    string? ReportAssigneeUserId,
    DateTime? ReportFromDate,
    DateTime? ReportToDate,
    string? ReportStatus,
    string? ReportPriority);
