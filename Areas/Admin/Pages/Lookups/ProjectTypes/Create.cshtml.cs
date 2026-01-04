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

        // SECTION: Duplicate guard
        var exists = await _db.ProjectTypes
            .AnyAsync(u => u.Name == trimmedName);

        if (exists)
        {
            ModelState.AddModelError("Input.Name", "A project type with this name already exists.");
            return Page();
        }

        // SECTION: Persist lookup
        var projectType = new ProjectManagement.Models.ProjectType
        {
            Name = trimmedName,
            SortOrder = Input.SortOrder,
            IsActive = true
        };

        _db.ProjectTypes.Add(projectType);
        await _db.SaveChangesAsync();

        // SECTION: Audit log
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _audit.LogAsync(
            "Lookups.ProjectTypeCreated",
            userId: userId,
            userName: User.Identity?.Name,
            data: new Dictionary<string, string?>
            {
                ["ProjectTypeId"] = projectType.Id.ToString(),
                ["Name"] = projectType.Name,
                ["SortOrder"] = projectType.SortOrder.ToString()
            });

        TempData["StatusMessage"] = $"Created '{projectType.Name}'.";
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
