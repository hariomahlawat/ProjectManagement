using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ProjectManagement.Helpers;

public static class CategoryHierarchyBuilder
{
    public sealed record CategoryNode<TCategory>(TCategory Category, IReadOnlyList<CategoryNode<TCategory>> Children);

    public static async Task<IReadOnlyList<CategoryNode<TCategory>>> LoadHierarchyAsync<TCategory>(
        IQueryable<TCategory> query,
        Func<TCategory, int> idSelector,
        Func<TCategory, int?> parentSelector,
        Func<TCategory, int> sortOrderSelector,
        Func<TCategory, string> nameSelector,
        CancellationToken cancellationToken = default)
        where TCategory : class
    {
        var categories = await query
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return BuildHierarchy(categories, idSelector, parentSelector, sortOrderSelector, nameSelector);
    }

    public static IReadOnlyList<CategoryNode<TCategory>> BuildHierarchy<TCategory>(
        IEnumerable<TCategory> categories,
        Func<TCategory, int> idSelector,
        Func<TCategory, int?> parentSelector,
        Func<TCategory, int> sortOrderSelector,
        Func<TCategory, string> nameSelector)
    {
        var ordered = categories
            .OrderBy(sortOrderSelector)
            .ThenBy(nameSelector)
            .ToList();

        var lookup = ordered.ToLookup(parentSelector);

        List<CategoryNode<TCategory>> Build(int? parentId)
        {
            return lookup[parentId]
                .Select(category => new CategoryNode<TCategory>(category, Build(idSelector(category))))
                .ToList();
        }

        return Build(null);
    }

    public static async Task<List<SelectListItem>> BuildSelectListAsync<TCategory>(
        IQueryable<TCategory> query,
        int? selectedId,
        int? excludeId,
        Func<TCategory, int> idSelector,
        Func<TCategory, int?> parentSelector,
        Func<TCategory, int> sortOrderSelector,
        Func<TCategory, string> nameSelector,
        CancellationToken cancellationToken = default)
        where TCategory : class
    {
        var categories = await query
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return BuildSelectList(categories, selectedId, excludeId, idSelector, parentSelector, sortOrderSelector, nameSelector);
    }

    public static List<SelectListItem> BuildSelectList<TCategory>(
        IEnumerable<TCategory> categories,
        int? selectedId,
        int? excludeId,
        Func<TCategory, int> idSelector,
        Func<TCategory, int?> parentSelector,
        Func<TCategory, int> sortOrderSelector,
        Func<TCategory, string> nameSelector)
    {
        var ordered = categories
            .OrderBy(sortOrderSelector)
            .ThenBy(nameSelector)
            .ToList();

        var lookup = ordered.ToLookup(parentSelector);
        var visited = new HashSet<int>();
        var items = new List<SelectListItem>
        {
            new()
            {
                Text = "(None)",
                Value = string.Empty,
                Selected = selectedId is null
            }
        };

        void Build(int? parentId, string prefix)
        {
            foreach (var category in lookup[parentId])
            {
                var categoryId = idSelector(category);
                if (excludeId.HasValue && categoryId == excludeId.Value)
                {
                    continue;
                }

                if (!visited.Add(categoryId))
                {
                    continue;
                }

                var categoryName = nameSelector(category);
                var text = string.IsNullOrEmpty(prefix)
                    ? categoryName
                    : $"{prefix} › {categoryName}";

                items.Add(new SelectListItem
                {
                    Text = text,
                    Value = categoryId.ToString(),
                    Selected = selectedId == categoryId
                });

                Build(categoryId, text);
            }
        }

        Build(null, string.Empty);
        return items;
    }
}
