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
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public CreateModel(ApplicationDbContext db)
        {
            _db = db;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public IList<SelectListItem> ParentOptions { get; private set; } = new List<SelectListItem>();

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task OnGetAsync(int? parentId)
        {
            Input.ParentId = parentId;
            ParentOptions = await LoadParentOptionsAsync(parentId, null);
        }

        public async Task<IActionResult> OnPostAsync()
        {
            ParentOptions = await LoadParentOptionsAsync(Input.ParentId, null);

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var trimmedName = Input.Name.Trim();
            var duplicateExists = await _db.TechnicalCategories
                .AnyAsync(c => c.ParentId == Input.ParentId && c.Name == trimmedName);

            if (duplicateExists)
            {
                ModelState.AddModelError("Input.Name", "A category with this name already exists under the selected parent.");
                return Page();
            }

            var category = new TechnicalCategory
            {
                Name = trimmedName,
                ParentId = Input.ParentId,
                IsActive = Input.IsActive
            };

            var maxSortOrder = await _db.TechnicalCategories
                .Where(c => c.ParentId == Input.ParentId)
                .MaxAsync(c => (int?)c.SortOrder);

            category.SortOrder = (maxSortOrder ?? -1) + 1;

            _db.TechnicalCategories.Add(category);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"Created '{category.Name}'.";

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
            [MaxLength(120)]
            public string Name { get; set; } = string.Empty;

            public int? ParentId { get; set; }

            public bool IsActive { get; set; } = true;
        }
    }
}
