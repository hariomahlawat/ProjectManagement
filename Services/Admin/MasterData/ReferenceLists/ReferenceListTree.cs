namespace ProjectManagement.Services.Admin.MasterData.ReferenceLists;

/// <summary>An item of a tree list in page order, with where it sits.</summary>
public sealed record ReferenceListTreeNode(
    ReferenceListItemProjection Item,
    int Depth,
    string? ParentPath,
    bool CanMoveUp,
    bool CanMoveDown);

/// <summary>
/// Lays a tree list (categories) out in page order: each parent, then its children, siblings by
/// their order and then name. Moving is among siblings. Every item is listed exactly once, even
/// when old data has a cycle (A under B under A) or a parent that no longer exists: those are
/// listed at the top level after the rest.
/// </summary>
public static class ReferenceListTree
{
    public static IReadOnlyList<ReferenceListTreeNode> Build(IReadOnlyCollection<ReferenceListItemProjection> items)
    {
        var ids = items.Select(item => item.Id).ToHashSet();
        var children = items
            .GroupBy(item => item.ParentId is int parent && ids.Contains(parent) ? parent : (int?)null)
            .ToDictionary(
                group => group.Key ?? 0,
                group => group.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList());

        var nodes = new List<ReferenceListTreeNode>(items.Count);
        var listed = new HashSet<int>();

        void Visit(ReferenceListItemProjection item, int depth, string? parentPath, List<ReferenceListItemProjection> siblings)
        {
            if (!listed.Add(item.Id))
            {
                return;
            }

            var index = siblings.FindIndex(candidate => candidate.Id == item.Id);
            nodes.Add(new ReferenceListTreeNode(item, depth, parentPath, index > 0, index >= 0 && index < siblings.Count - 1));

            if (children.TryGetValue(item.Id, out var own))
            {
                var path = parentPath is null ? item.Name : $"{parentPath} / {item.Name}";
                foreach (var child in own)
                {
                    Visit(child, depth + 1, path, own);
                }
            }
        }

        if (children.TryGetValue(0, out var roots))
        {
            foreach (var root in roots)
            {
                Visit(root, 0, null, roots);
            }
        }

        // Items only reachable through a cycle never hang from a top-level item.
        var unreached = items.Where(item => !listed.Contains(item.Id))
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var item in unreached)
        {
            Visit(item, 0, null, new List<ReferenceListItemProjection> { item });
        }

        return nodes;
    }
}
