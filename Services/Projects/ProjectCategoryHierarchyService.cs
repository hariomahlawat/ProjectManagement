using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectCategoryHierarchyService
{
    private readonly ApplicationDbContext _db;

    public ProjectCategoryHierarchyService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<int>> GetCategoryAndDescendantIdsAsync(
        int categoryId,
        CancellationToken cancellationToken = default)
    {
        var categories = await _db.ProjectCategories
            .AsNoTracking()
            .Select(c => new { c.Id, c.ParentId })
            .ToListAsync(cancellationToken);

        var childrenLookup = categories
            .GroupBy(c => c.ParentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var resolved = new HashSet<int> { categoryId };
        var stack = new Stack<int>();
        stack.Push(categoryId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!childrenLookup.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (resolved.Add(child))
                {
                    stack.Push(child);
                }
            }
        }

        return resolved.ToList();
    }
}
