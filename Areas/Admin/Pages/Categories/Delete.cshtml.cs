using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Admin.Pages.Categories
{
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public DeleteModel(ApplicationDbContext db)
        {
            _db = db;
        }

        [BindProperty]
        public ProjectCategory? Category { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var category = await _db.ProjectCategories
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
            var category = await _db.ProjectCategories.SingleOrDefaultAsync(c => c.Id == id);
            if (category is null)
            {
                return NotFound();
            }

            if (await _db.ProjectCategories.AnyAsync(c => c.ParentId == id))
            {
                await _db.Entry(category).Reference(c => c.Parent).LoadAsync();
                ModelState.AddModelError(string.Empty, "This category has child categories. Reassign or delete them first.");
                Category = category;
                return Page();
            }

            if (await _db.Projects.AnyAsync(p => p.CategoryId == id))
            {
                await _db.Entry(category).Reference(c => c.Parent).LoadAsync();
                ModelState.AddModelError(string.Empty, "Projects are assigned to this category. Reassign them before deleting.");
                Category = category;
                return Page();
            }

            _db.ProjectCategories.Remove(category);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"Deleted '{category.Name}'.";
            return RedirectToPage("Index");
        }
    }
}
