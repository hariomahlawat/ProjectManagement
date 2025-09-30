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
        var exists = await _db.LineDirectorates.AnyAsync(l => l.Name == trimmedName);

        if (exists)
        {
            ModelState.AddModelError("Input.Name", "A line directorate with this name already exists.");
            return Page();
        }

        var now = DateTime.UtcNow;
        var entity = new ProjectManagement.Models.LineDirectorate
        {
            Name = trimmedName,
            SortOrder = Input.SortOrder,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        _db.LineDirectorates.Add(entity);
        await _db.SaveChangesAsync();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _audit.LogAsync(
            "Lookups.LineDirectorateCreated",
            userId: userId,
            userName: User.Identity?.Name,
            data: new Dictionary<string, string?>
            {
                ["LineDirectorateId"] = entity.Id.ToString(),
                ["Name"] = entity.Name,
                ["SortOrder"] = entity.SortOrder.ToString()
            });

        TempData["StatusMessage"] = $"Created '{entity.Name}'.";
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
