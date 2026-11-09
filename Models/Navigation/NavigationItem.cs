using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Navigation;

public record class NavigationItem
{
    public required string Text { get; init; }

    /// <summary>
    /// Optional stable identifier. The navigation drawer uses it for its sections
    /// (see <see cref="NavigationSectionKeys"/>) so markup, scripts and tests do not
    /// depend on display text.
    /// </summary>
    public string? Key { get; init; }

    public string? Area { get; init; }

    public string? Page { get; init; }

    /// <summary>
    /// Optional Razor Page route-family prefix used only for active-tab resolution.
    /// The destination still comes from <see cref="Page"/>.
    /// </summary>
    public string? ActivePagePrefix { get; init; }

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

    /// <summary>
    /// Opens the destination in a new browser tab. Use for standalone tools, such as
    /// PDF Tools, that run outside the PRISM page shell.
    /// </summary>
    public bool OpensInNewTab { get; init; }
}

/// <summary>Stable keys of the navigation drawer's sections (docs/ux/navigation-drawer.md).</summary>
public static class NavigationSectionKeys
{
    public const string Main = "main";
    public const string Projects = "projects";
    public const string Trackers = "trackers";
    public const string Tools = "tools";
    public const string Administration = "administration";
}
