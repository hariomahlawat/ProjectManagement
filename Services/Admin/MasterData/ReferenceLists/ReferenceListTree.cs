namespace ProjectManagement.Services.Admin.MasterData.ReferenceLists;

/// <summary>An item of a tree list in page order, with where it sits.</summary>
public sealed record ReferenceListTreeNode<TKey>(
    ReferenceListItemProjection<TKey> Item,
    int Depth,
    string? ParentPath,
    bool CanMoveUp,
    bool CanMoveDown) where TKey : struct;

/// <summary>
/// Lays a tree list (categories) out in page order: each parent, then its children, siblings by
/// their order and then name. Moving is among siblings. Every item is listed exactly once, even
/// when old data has a cycle (A under B under A) or a parent that no longer exists: those are
/// listed at the top level after the rest.
/// </summary>
public static class ReferenceListTree
{
    public static IReadOnlyList<ReferenceListTreeNode<TKey>> Build<TKey>(IReadOnlyCollection<ReferenceListItemProjection<TKey>> items)
        where TKey : struct
    {
        var ids = items.Select(item => item.Id).ToHashSet();
        var roots = new List<ReferenceListItemProjection<TKey>>();
        var children = new Dictionary<TKey, List<ReferenceListItemProjection<TKey>>>();
        foreach (var item in items)
        {
            if (item.ParentId is TKey parent && ids.Contains(parent))
            {
                if (!children.TryGetValue(parent, out var siblings))
                {
                    children[parent] = siblings = new List<ReferenceListItemProjection<TKey>>();
                }

                siblings.Add(item);
            }
            else
            {
                roots.Add(item);
            }
        }

        static List<ReferenceListItemProjection<TKey>> Ordered(IEnumerable<ReferenceListItemProjection<TKey>> list) =>
            list.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();

        roots = Ordered(roots);
        foreach (var key in children.Keys.ToList())
        {
            children[key] = Ordered(children[key]);
        }

        var nodes = new List<ReferenceListTreeNode<TKey>>(items.Count);
        var listed = new HashSet<TKey>();

        void Visit(ReferenceListItemProjection<TKey> item, int depth, string? parentPath, List<ReferenceListItemProjection<TKey>> siblings)
        {
            if (!listed.Add(item.Id))
            {
                return;
            }

            var index = siblings.FindIndex(candidate => EqualityComparer<TKey>.Default.Equals(candidate.Id, item.Id));
            nodes.Add(new ReferenceListTreeNode<TKey>(item, depth, parentPath, index > 0, index >= 0 && index < siblings.Count - 1));

            if (children.TryGetValue(item.Id, out var own))
            {
                var path = parentPath is null ? item.Name : $"{parentPath} / {item.Name}";
                foreach (var child in own)
                {
                    Visit(child, depth + 1, path, own);
                }
            }
        }

        foreach (var root in roots)
        {
            Visit(root, 0, null, roots);
        }

        // Items only reachable through a cycle never hang from a top-level item.
        foreach (var item in Ordered(items.Where(item => !listed.Contains(item.Id))))
        {
            Visit(item, 0, null, new List<ReferenceListItemProjection<TKey>> { item });
        }

        return nodes;
    }
}
