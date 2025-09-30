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

namespace ProjectManagement.Areas.Admin.Pages.Lookups.LineDirectorates;

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
        var entity = await LoadAsync();
        if (entity is null)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var entity = await _db.LineDirectorates.FirstOrDefaultAsync(l => l.Id == Id);
        if (entity is null)
        {
            return NotFound();
        }

        ProjectCount = await _db.Projects.CountAsync(p => p.SponsoringLineDirectorateId == Id);

        if (!Restore && ProjectCount > 0)
        {
            Name = entity.Name;
            IsActive = entity.IsActive;
            ModelState.AddModelError(string.Empty, "Cannot deactivate while the line directorate is assigned to existing projects.");
            return Page();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Restore)
        {
            if (!entity.IsActive)
            {
                entity.IsActive = true;
                entity.UpdatedUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    "Lookups.LineDirectorateReactivated",
                    userId: userId,
                    userName: User.Identity?.Name,
                    data: new Dictionary<string, string?>
                    {
                        ["LineDirectorateId"] = entity.Id.ToString(),
                        ["Name"] = entity.Name
                    });
            }

            TempData["StatusMessage"] = $"Reactivated '{entity.Name}'.";
        }
        else
        {
            if (entity.IsActive)
            {
                entity.IsActive = false;
                entity.UpdatedUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    "Lookups.LineDirectorateDeactivated",
                    userId: userId,
                    userName: User.Identity?.Name,
                    data: new Dictionary<string, string?>
                    {
                        ["LineDirectorateId"] = entity.Id.ToString(),
                        ["Name"] = entity.Name
                    });
            }

            TempData["StatusMessage"] = $"Deactivated '{entity.Name}'.";
        }

        return RedirectToPage("./Index");
    }

    private async Task<ProjectManagement.Models.LineDirectorate?> LoadAsync()
    {
        var entity = await _db.LineDirectorates
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == Id);

        if (entity is null)
        {
            return null;
        }

        Name = entity.Name;
        IsActive = entity.IsActive;
        ProjectCount = await _db.Projects.CountAsync(p => p.SponsoringLineDirectorateId == Id);
        return entity;
    }
}
