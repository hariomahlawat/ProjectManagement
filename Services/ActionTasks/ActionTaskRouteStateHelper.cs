using System;
using Microsoft.AspNetCore.Routing;

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
}

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
