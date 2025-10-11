using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Admin.Pages.TechnicalCategories
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public IndexModel(ApplicationDbContext db)
        {
            _db = db;
        }

        [TempData]
        public string? StatusMessage { get; set; }

        public IReadOnlyList<CategoryNode> Nodes { get; private set; } = Array.Empty<CategoryNode>();

        public async Task OnGetAsync()
        {
            Nodes = await LoadTreeAsync();
        }

        public async Task<IActionResult> OnPostToggleAsync(int id)
        {
            var category = await _db.TechnicalCategories.SingleOrDefaultAsync(c => c.Id == id);
            if (category is null)
            {
                return NotFound();
            }

            category.IsActive = !category.IsActive;
            await _db.SaveChangesAsync();

            StatusMessage = category.IsActive
                ? $"Activated '{category.Name}'."
                : $"Deactivated '{category.Name}'.";

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostMoveAsync(int id, int offset)
        {
            if (offset == 0)
            {
                return RedirectToPage();
            }

            var category = await _db.TechnicalCategories.SingleOrDefaultAsync(c => c.Id == id);
            if (category is null)
            {
                return NotFound();
            }

            var siblings = await _db.TechnicalCategories
                .Where(c => c.ParentId == category.ParentId)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            var index = siblings.FindIndex(c => c.Id == id);
            if (index < 0)
            {
                return RedirectToPage();
            }

            var targetIndex = Math.Clamp(index + offset, 0, siblings.Count - 1);
            if (targetIndex == index)
            {
                return RedirectToPage();
            }

            var moving = siblings[index];
            siblings.RemoveAt(index);
            siblings.Insert(targetIndex, moving);

            for (var i = 0; i < siblings.Count; i++)
            {
                siblings[i].SortOrder = i;
            }

            await _db.SaveChangesAsync();

            StatusMessage = $"Reordered siblings for '{category.Name}'.";

            return RedirectToPage();
        }

        private async Task<IReadOnlyList<CategoryNode>> LoadTreeAsync()
        {
            var nodes = await CategoryHierarchyBuilder.LoadHierarchyAsync(
                _db.TechnicalCategories,
                c => c.Id,
                c => c.ParentId,
                c => c.SortOrder,
                c => c.Name);

            var usageCounts = await _db.Projects
                .Where(p => p.TechnicalCategoryId != null)
                .GroupBy(p => p.TechnicalCategoryId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count);

            CategoryNode Map(CategoryHierarchyBuilder.CategoryNode<TechnicalCategory> node)
            {
                var count = usageCounts.TryGetValue(node.Category.Id, out var value) ? value : 0;
                return new CategoryNode(node.Category, count, node.Children.Select(Map).ToList());
            }

            return nodes.Select(Map).ToList();
        }

        public sealed record CategoryNode(TechnicalCategory Category, int ProjectCount, IReadOnlyList<CategoryNode> Children);
    }
}
