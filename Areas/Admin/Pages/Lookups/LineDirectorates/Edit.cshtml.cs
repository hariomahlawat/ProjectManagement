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

namespace ProjectManagement.Areas.Admin.Pages.Lookups.LineDirectorates;

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

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var entity = await _db.LineDirectorates.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Id = entity.Id,
            Name = entity.Name,
            SortOrder = entity.SortOrder
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var entity = await _db.LineDirectorates.FindAsync(Input.Id);
        if (entity is null)
        {
            return NotFound();
        }

        var trimmedName = Input.Name.Trim();
        var duplicate = await _db.LineDirectorates
            .AnyAsync(l => l.Id != Input.Id && l.Name == trimmedName);

        if (duplicate)
        {
            ModelState.AddModelError("Input.Name", "A line directorate with this name already exists.");
            return Page();
        }

        var originalName = entity.Name;
        var originalSortOrder = entity.SortOrder;

        entity.Name = trimmedName;
        entity.SortOrder = Input.SortOrder;
        entity.UpdatedUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _audit.LogAsync(
            "Lookups.LineDirectorateUpdated",
            new Dictionary<string, string?>
            {
                ["LineDirectorateId"] = entity.Id.ToString(),
                ["NameBefore"] = originalName,
                ["NameAfter"] = entity.Name,
                ["SortOrderBefore"] = originalSortOrder.ToString(),
                ["SortOrderAfter"] = entity.SortOrder.ToString()
            },
            userId: userId,
            userName: User.Identity?.Name);

        TempData["StatusMessage"] = $"Updated '{entity.Name}'.";
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
