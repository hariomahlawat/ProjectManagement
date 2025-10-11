using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Admin.Pages.TechnicalCategories
{
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public DeleteModel(ApplicationDbContext db)
        {
            _db = db;
        }

        [BindProperty]
        public TechnicalCategory? Category { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var category = await _db.TechnicalCategories
                .Include(c => c.Parent)
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == id);

            if (category is null)
            {
                return NotFound();
            }

            Category = category;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            var category = await _db.TechnicalCategories.SingleOrDefaultAsync(c => c.Id == id);
            if (category is null)
            {
                return NotFound();
            }

            var childCount = await _db.TechnicalCategories.CountAsync(c => c.ParentId == id);
            if (childCount > 0)
            {
                await _db.Entry(category).Reference(c => c.Parent).LoadAsync();
                var childLabel = childCount == 1 ? "child category" : "child categories";
                ModelState.AddModelError(string.Empty, $"This technical category has {childCount} {childLabel}. Reassign or delete them first.");
                Category = category;
                return Page();
            }

            var projectCount = await _db.Projects.CountAsync(p => p.TechnicalCategoryId == id);
            if (projectCount > 0)
            {
                await _db.Entry(category).Reference(c => c.Parent).LoadAsync();
                var projectLabel = projectCount == 1 ? "project" : "projects";
                ModelState.AddModelError(string.Empty, $"Cannot delete yet. {projectCount} {projectLabel} currently use this technical category. Reassign them before deleting.");
                Category = category;
                return Page();
            }

            _db.TechnicalCategories.Remove(category);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"Deleted '{category.Name}'.";
            return RedirectToPage("Index");
        }
    }
}
