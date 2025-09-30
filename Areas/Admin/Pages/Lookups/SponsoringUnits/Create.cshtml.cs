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
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public CreateModel(ApplicationDbContext db, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var trimmedName = Input.Name.Trim();

        var exists = await _db.SponsoringUnits
            .AnyAsync(u => u.Name == trimmedName);

        if (exists)
        {
            ModelState.AddModelError("Input.Name", "A sponsoring unit with this name already exists.");
            return Page();
        }

        var now = DateTime.UtcNow;
        var unit = new ProjectManagement.Models.SponsoringUnit
        {
            Name = trimmedName,
            SortOrder = Input.SortOrder,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        _db.SponsoringUnits.Add(unit);
        await _db.SaveChangesAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _audit.LogAsync(
            "Lookups.SponsoringUnitCreated",
            new Dictionary<string, string?>
            {
                ["SponsoringUnitId"] = unit.Id.ToString(),
                ["Name"] = unit.Name,
                ["SortOrder"] = unit.SortOrder.ToString()
            },
            userId: userId,
            userName: User.Identity?.Name);

        TempData["StatusMessage"] = $"Created '{unit.Name}'.";
        return RedirectToPage("./Index");
    }

    public sealed class InputModel
    {
        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [Range(0, 1000)]
        public int SortOrder { get; set; }
    }
}
