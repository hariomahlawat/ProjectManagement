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

namespace ProjectManagement.Areas.Admin.Pages.Lookups.ProjectTypes;

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
        // SECTION: Lookup detail
        var unit = await LoadAsync();
        if (unit is null)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // SECTION: Lookup resolution
        var projectType = await _db.ProjectTypes
            .FirstOrDefaultAsync(u => u.Id == Id);

        if (projectType is null)
        {
            return NotFound();
        }

        // SECTION: Assignment guard
        ProjectCount = await _db.Projects.CountAsync(p => p.ProjectTypeId == Id);

        if (!Restore && ProjectCount > 0)
        {
            IsActive = projectType.IsActive;
            Name = projectType.Name;
            ModelState.AddModelError(string.Empty, "Cannot deactivate while the project type is assigned to existing projects.");
            return Page();
        }

        // SECTION: Audit log
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Restore)
        {
            if (!projectType.IsActive)
            {
                projectType.IsActive = true;
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    "Lookups.ProjectTypeReactivated",
                    userId: userId,
                    userName: User.Identity?.Name,
                    data: new Dictionary<string, string?>
                    {
                        ["ProjectTypeId"] = projectType.Id.ToString(),
                        ["Name"] = projectType.Name
                    });
            }

            TempData["StatusMessage"] = $"Reactivated '{projectType.Name}'.";
        }
        else
        {
            if (projectType.IsActive)
            {
                projectType.IsActive = false;
                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    "Lookups.ProjectTypeDeactivated",
                    userId: userId,
                    userName: User.Identity?.Name,
                    data: new Dictionary<string, string?>
                    {
                        ["ProjectTypeId"] = projectType.Id.ToString(),
                        ["Name"] = projectType.Name
                    });
            }

            TempData["StatusMessage"] = $"Deactivated '{projectType.Name}'.";
        }

        return RedirectToPage("./Index");
    }

    private async Task<ProjectManagement.Models.ProjectType?> LoadAsync()
    {
        var projectType = await _db.ProjectTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == Id);

        if (projectType is null)
        {
            return null;
        }

        Name = projectType.Name;
        IsActive = projectType.IsActive;
        ProjectCount = await _db.Projects.CountAsync(p => p.ProjectTypeId == Id);

        return projectType;
    }
}
