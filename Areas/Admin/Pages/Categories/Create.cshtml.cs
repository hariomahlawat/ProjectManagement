using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.Admin.Pages.Categories
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
            ParentOptions = await CategorySelectListBuilder.BuildAsync(_db, parentId);
        }

        public async Task<IActionResult> OnPostAsync()
        {
            ParentOptions = await CategorySelectListBuilder.BuildAsync(_db, Input.ParentId);

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var category = new ProjectCategory
            {
                Name = Input.Name.Trim(),
                ParentId = Input.ParentId,
                IsActive = Input.IsActive
            };

            var nextSort = await _db.ProjectCategories
                .Where(c => c.ParentId == Input.ParentId)
                .Select(c => c.SortOrder)
                .DefaultIfEmpty(-1)
                .MaxAsync();

            category.SortOrder = nextSort + 1;

            _db.ProjectCategories.Add(category);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"Created '{category.Name}'.";

            return RedirectToPage("Index");
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
