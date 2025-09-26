using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Admin.Pages.Categories
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
            var category = await _db.ProjectCategories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
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

            ParentOptions = await CategorySelectListBuilder.BuildAsync(_db, category.ParentId, category.Id);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            ParentOptions = await CategorySelectListBuilder.BuildAsync(_db, Input.ParentId, Input.Id);

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var category = await _db.ProjectCategories.SingleOrDefaultAsync(c => c.Id == Input.Id);
            if (category is null)
            {
                return NotFound();
            }

            if (Input.ParentId == category.Id)
            {
                ModelState.AddModelError("Input.ParentId", "A category cannot be its own parent.");
                return Page();
            }

            if (Input.ParentId.HasValue)
            {
                var descendants = await GetDescendantIdsAsync(category.Id);
                if (descendants.Contains(Input.ParentId.Value))
                {
                    ModelState.AddModelError("Input.ParentId", "A category cannot move under one of its descendants.");
                    return Page();
                }
            }

            var parentChanged = category.ParentId != Input.ParentId;

            category.Name = Input.Name.Trim();
            category.IsActive = Input.IsActive;
            category.ParentId = Input.ParentId;

            if (parentChanged)
            {
                var nextSort = await _db.ProjectCategories
                    .Where(c => c.ParentId == Input.ParentId && c.Id != category.Id)
                    .Select(c => c.SortOrder)
                    .DefaultIfEmpty(-1)
                    .MaxAsync();

                category.SortOrder = nextSort + 1;
            }

            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"Updated '{category.Name}'.";

            return RedirectToPage("Index");
        }

        private async Task<HashSet<int>> GetDescendantIdsAsync(int categoryId)
        {
            var relationships = await _db.ProjectCategories
                .AsNoTracking()
                .Select(c => new { c.Id, c.ParentId })
                .ToListAsync();

            var lookup = relationships.ToLookup(x => x.ParentId);
            var results = new HashSet<int>();

            void Visit(int parentId)
            {
                foreach (var child in lookup[parentId])
                {
                    if (results.Add(child.Id))
                    {
                        Visit(child.Id);
                    }
                }
            }

            Visit(categoryId);
            return results;
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
