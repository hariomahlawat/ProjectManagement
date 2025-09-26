using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Admin.Pages.Categories
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
            var category = await _db.ProjectCategories.SingleOrDefaultAsync(c => c.Id == id);
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

            var category = await _db.ProjectCategories.SingleOrDefaultAsync(c => c.Id == id);
            if (category is null)
            {
                return NotFound();
            }

            var siblings = await _db.ProjectCategories
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
            var categories = await _db.ProjectCategories
                .AsNoTracking()
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            var lookup = categories.ToLookup(c => c.ParentId);

            List<CategoryNode> Build(int? parentId)
            {
                return lookup[parentId]
                    .Select(c => new CategoryNode(c, Build(c.Id)))
                    .ToList();
            }

            return Build(null);
        }

        public sealed record CategoryNode(ProjectCategory Category, IReadOnlyList<CategoryNode> Children);
    }
}
