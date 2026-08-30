using System;
using System.Collections.Generic;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// Canonical root-category resolver for project portfolio grouping.
/// Supports arbitrary hierarchy depth and safely terminates on malformed cycles.
/// </summary>
public static class ProjectCategoryHierarchyResolver
{
    public static ProjectCategoryHierarchyNode? ResolveRoot(
        int? categoryId,
        IReadOnlyDictionary<int, ProjectCategoryHierarchyNode> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        if (!categoryId.HasValue || !categories.TryGetValue(categoryId.Value, out var current))
        {
            return null;
        }

        var visited = new HashSet<int>();
        while (current.ParentId.HasValue)
        {
            if (!visited.Add(current.Id))
            {
                break;
            }

            if (!categories.TryGetValue(current.ParentId.Value, out var parent))
            {
                break;
            }

            current = parent;
        }

        return current;
    }
}

public readonly record struct ProjectCategoryHierarchyNode(
    int Id,
    string Name,
    int? ParentId);
