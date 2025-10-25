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
}
