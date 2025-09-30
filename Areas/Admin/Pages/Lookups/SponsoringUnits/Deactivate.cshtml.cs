using System;
using System.Collections.Generic;
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
public class DeactivateModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;

    public DeactivateModel(ApplicationDbContext db, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Restore { get; set; }

    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int ProjectCount { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var unit = await LoadAsync();
        if (unit is null)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var unit = await _db.SponsoringUnits
            .FirstOrDefaultAsync(u => u.Id == Id);

        if (unit is null)
        {
            return NotFound();
        }

        ProjectCount = await _db.Projects.CountAsync(p => p.SponsoringUnitId == Id);

        if (!Restore && ProjectCount > 0)
        {
            IsActive = unit.IsActive;
            Name = unit.Name;
            ModelState.AddModelError(string.Empty, "Cannot deactivate while the unit is assigned to existing projects.");
            return Page();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Restore)
        {
            if (!unit.IsActive)
            {
                unit.IsActive = true;
                unit.UpdatedUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    "Lookups.SponsoringUnitReactivated",
                    new Dictionary<string, string?>
                    {
                        ["SponsoringUnitId"] = unit.Id.ToString(),
                        ["Name"] = unit.Name
                    },
                    userId: userId,
                    userName: User.Identity?.Name);
            }

            TempData["StatusMessage"] = $"Reactivated '{unit.Name}'.";
        }
        else
        {
            if (unit.IsActive)
            {
                unit.IsActive = false;
                unit.UpdatedUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    "Lookups.SponsoringUnitDeactivated",
                    new Dictionary<string, string?>
                    {
                        ["SponsoringUnitId"] = unit.Id.ToString(),
                        ["Name"] = unit.Name
                    },
                    userId: userId,
                    userName: User.Identity?.Name);
            }

            TempData["StatusMessage"] = $"Deactivated '{unit.Name}'.";
        }

        return RedirectToPage("./Index");
    }

    private async Task<ProjectManagement.Models.SponsoringUnit?> LoadAsync()
    {
        var unit = await _db.SponsoringUnits
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == Id);

        if (unit is null)
        {
            return null;
        }

        Name = unit.Name;
        IsActive = unit.IsActive;
        ProjectCount = await _db.Projects.CountAsync(p => p.SponsoringUnitId == Id);

        return unit;
    }
}
