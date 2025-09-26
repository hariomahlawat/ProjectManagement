using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Admin.Pages.Categories
{
    internal static class CategorySelectListBuilder
    {
        public static async Task<List<SelectListItem>> BuildAsync(ApplicationDbContext db, int? selectedId = null, int? excludeId = null)
        {
            var categories = await db.ProjectCategories
                .AsNoTracking()
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            var lookup = categories.ToLookup(c => c.ParentId);
            var visited = new HashSet<int>();
            var items = new List<SelectListItem>
            {
                new SelectListItem
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
                    if (excludeId.HasValue && category.Id == excludeId.Value)
                    {
                        continue;
                    }

                    if (!visited.Add(category.Id))
                    {
                        continue;
                    }

                    var text = string.IsNullOrEmpty(prefix)
                        ? category.Name
                        : $"{prefix} › {category.Name}";

                    items.Add(new SelectListItem
                    {
                        Text = text,
                        Value = category.Id.ToString(),
                        Selected = selectedId == category.Id
                    });

                    Build(category.Id, text);
                }
            }

            Build(null, string.Empty);
            return items;
        }
    }
}
