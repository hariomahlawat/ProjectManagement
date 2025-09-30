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

namespace ProjectManagement.Areas.Admin.Pages.Lookups.SponsoringUnits;

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
        var unit = await _db.SponsoringUnits.FindAsync(id);
        if (unit is null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Id = unit.Id,
            Name = unit.Name,
            SortOrder = unit.SortOrder
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var unit = await _db.SponsoringUnits.FindAsync(Input.Id);
        if (unit is null)
        {
            return NotFound();
        }

        var trimmedName = Input.Name.Trim();

        var duplicate = await _db.SponsoringUnits
            .AnyAsync(u => u.Id != Input.Id && u.Name == trimmedName);

        if (duplicate)
        {
            ModelState.AddModelError("Input.Name", "A sponsoring unit with this name already exists.");
            return Page();
        }

        var originalName = unit.Name;
        var originalSort = unit.SortOrder;

        unit.Name = trimmedName;
        unit.SortOrder = Input.SortOrder;
        unit.UpdatedUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _audit.LogAsync(
            "Lookups.SponsoringUnitUpdated",
            new Dictionary<string, string?>
            {
                ["SponsoringUnitId"] = unit.Id.ToString(),
                ["NameBefore"] = originalName,
                ["NameAfter"] = unit.Name,
                ["SortOrderBefore"] = originalSort.ToString(),
                ["SortOrderAfter"] = unit.SortOrder.ToString()
            },
            userId: userId,
            userName: User.Identity?.Name);

        TempData["StatusMessage"] = $"Updated '{unit.Name}'.";
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
