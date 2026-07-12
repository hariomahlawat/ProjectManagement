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

            if (Input.ParentId.HasValue)
            {
                var descendantIds = await GetDescendantIdsAsync(Input.Id);
                if (descendantIds.Contains(Input.ParentId.Value))
                {
                    ModelState.AddModelError("Input.ParentId", "A technical category cannot move under one of its descendants.");
                    return Page();
                }
            }

            var trimmedName = Input.Name.Trim();
            var duplicateExists = await _db.TechnicalCategories
                .AnyAsync(c => c.ParentId == Input.ParentId && c.Name == trimmedName && c.Id != Input.Id);

            if (duplicateExists)
            {
                ModelState.AddModelError("Input.Name", "A category with this name already exists under the selected parent.");
                return Page();
            }

            var parentChanged = category.ParentId != Input.ParentId;

            category.Name = trimmedName;
            category.ParentId = Input.ParentId;
            category.IsActive = Input.IsActive;

            if (parentChanged)
            {
                var nextSortOrder = await _db.TechnicalCategories
                    .Where(c => c.ParentId == Input.ParentId && c.Id != category.Id)
                    .Select(c => c.SortOrder)
                    .DefaultIfEmpty(-1)
                    .MaxAsync();

                category.SortOrder = nextSortOrder + 1;
            }

            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"Updated '{category.Name}'.";

            return RedirectToPage("Index");
        }


        private async Task<HashSet<int>> GetDescendantIdsAsync(int categoryId)
        {
            var relationships = await _db.TechnicalCategories
                .AsNoTracking()
                .Select(category => new { category.Id, category.ParentId })
                .ToListAsync();

            var childrenByParent = relationships.ToLookup(category => category.ParentId);
            var descendantIds = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(categoryId);

            while (pending.Count > 0)
            {
                var parentId = pending.Pop();
                foreach (var child in childrenByParent[parentId])
                {
                    if (descendantIds.Add(child.Id))
                    {
                        pending.Push(child.Id);
                    }
                }
            }

            return descendantIds;
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
