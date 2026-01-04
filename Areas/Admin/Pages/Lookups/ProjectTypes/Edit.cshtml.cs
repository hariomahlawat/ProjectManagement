using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.Admin.Pages.Lookups.ProjectTypes;

[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public EditModel(ApplicationDbContext db, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var projectType = await _db.ProjectTypes.FindAsync(id);
        if (projectType is null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Id = projectType.Id,
            Name = projectType.Name,
            SortOrder = projectType.SortOrder
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var projectType = await _db.ProjectTypes.FindAsync(Input.Id);
        if (projectType is null)
        {
            return NotFound();
        }

        var trimmedName = Input.Name.Trim();

        // SECTION: Duplicate guard
        var duplicate = await _db.ProjectTypes
            .AnyAsync(u => u.Id != Input.Id && u.Name == trimmedName);

        if (duplicate)
        {
            ModelState.AddModelError("Input.Name", "A project type with this name already exists.");
            return Page();
        }

        // SECTION: Persist lookup update
        var originalName = projectType.Name;
        var originalSort = projectType.SortOrder;

        projectType.Name = trimmedName;
        projectType.SortOrder = Input.SortOrder;

        await _db.SaveChangesAsync();

        // SECTION: Audit log
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _audit.LogAsync(
            "Lookups.ProjectTypeUpdated",
            userId: userId,
            userName: User.Identity?.Name,
            data: new Dictionary<string, string?>
            {
                ["ProjectTypeId"] = projectType.Id.ToString(),
                ["NameBefore"] = originalName,
                ["NameAfter"] = projectType.Name,
                ["SortOrderBefore"] = originalSort.ToString(),
                ["SortOrderAfter"] = projectType.SortOrder.ToString()
            });

        TempData["StatusMessage"] = $"Updated '{projectType.Name}'.";
        return RedirectToPage("./Index");
    }

    public sealed class InputModel
    {
        [HiddenInput]
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [Range(0, 1000)]
        public int SortOrder { get; set; }
    }
}
