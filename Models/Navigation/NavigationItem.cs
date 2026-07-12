using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Navigation;

public record class NavigationItem
{
    public required string Text { get; init; }

    public string? Area { get; init; }

    public string? Page { get; init; }

    public string? Controller { get; init; }

    public string? Action { get; init; }

    public IReadOnlyDictionary<string, object?>? RouteValues { get; init; }

    public IReadOnlyList<NavigationItem> Children { get; init; } = Array.Empty<NavigationItem>();

    public string? AuthorizationPolicy { get; init; }

    public IReadOnlyList<string>? RequiredRoles { get; init; }

    public string? BadgeViewComponentName { get; init; }

    public object? BadgeViewComponentParameters { get; init; }

    public string? Icon { get; init; }

    public string? Accent { get; init; }

    /// <summary>
    /// Renders the item as a module-level command instead of a navigation tab.
    /// Use this for actions such as creating a new project so navigation and
    /// commands remain visually and semantically distinct.
    /// </summary>
    public bool IsAction { get; init; }
}
