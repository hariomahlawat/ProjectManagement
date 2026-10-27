using System.Collections.Generic;

namespace ProjectManagement.ViewModels;

/// <summary>
/// One option in a quick-filter menu (docs/ux/projects-register.md): its value (null = any),
/// label, how many projects it would list under every other filter (null when not counted),
/// and whether it is chosen.
/// </summary>
public sealed record ProjectRegisterFacetOption(string? Value, string Label, int? Count, bool IsSelected);

/// <summary>A quick-filter menu: the parameter it sets, its name and its options.</summary>
public sealed record ProjectRegisterFacet(string Key, string Name, IReadOnlyList<ProjectRegisterFacetOption> Options)
{
    public ProjectRegisterFacetOption? Selected => Options.Count == 0 ? null : FindSelected();

    private ProjectRegisterFacetOption? FindSelected()
    {
        foreach (var option in Options)
        {
            if (option.IsSelected && option.Value is not null)
            {
                return option;
            }
        }

        return null;
    }
}

/// <summary>An applied filter shown as a chip, with the route that removes just that filter.</summary>
public sealed record ProjectRegisterFilterChip(string Label, IDictionary<string, string?> RemoveRoute);
