using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Admin.Pages.TechnicalCategories
{
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public EditModel(ApplicationDbContext db)
        {
            _db = db;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public IList<SelectListItem> ParentOptions { get; private set; } = new List<SelectListItem>();

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var category = await _db.TechnicalCategories.FindAsync(id);
            if (category is null)
            {
                return NotFound();
            }

            Input = new InputModel
            {
                Id = category.Id,
                Name = category.Name,
                ParentId = category.ParentId,
                IsActive = category.IsActive
            };

            ParentOptions = await LoadParentOptionsAsync(category.ParentId, category.Id);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            ParentOptions = await LoadParentOptionsAsync(Input.ParentId, Input.Id);

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var category = await _db.TechnicalCategories.FindAsync(Input.Id);
            if (category is null)
            {
                return NotFound();
            }

            if (Input.ParentId == Input.Id)
            {
                ModelState.AddModelError("Input.ParentId", "A category cannot be its own parent.");
                return Page();
            }

            var trimmedName = Input.Name.Trim();
            var duplicateExists = await _db.TechnicalCategories
                .AnyAsync(c => c.ParentId == Input.ParentId && c.Name == trimmedName && c.Id != Input.Id);

            if (duplicateExists)
            {
                ModelState.AddModelError("Input.Name", "A category with this name already exists under the selected parent.");
                return Page();
            }

            category.Name = trimmedName;
            category.ParentId = Input.ParentId;
            category.IsActive = Input.IsActive;

            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"Updated '{category.Name}'.";

            return RedirectToPage("Index");
        }

        private Task<List<SelectListItem>> LoadParentOptionsAsync(int? selectedId, int? excludeId)
        {
            return CategoryHierarchyBuilder.BuildSelectListAsync(
                _db.TechnicalCategories,
                selectedId,
                excludeId,
                c => c.Id,
                c => c.ParentId,
                c => c.SortOrder,
                c => c.Name);
        }

        public class InputModel
        {
            [Required]
            public int Id { get; set; }

            [Required]
            [MaxLength(120)]
            public string Name { get; set; } = string.Empty;

            public int? ParentId { get; set; }

            public bool IsActive { get; set; }
        }
    }
}
