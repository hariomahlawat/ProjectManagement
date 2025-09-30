using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Pages.Projects.Meta;

[Authorize(Roles = "Admin,HoD")]
[AutoValidateAntiforgeryToken]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;

    public EditModel(ApplicationDbContext db, IUserContext userContext)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    [BindProperty]
    public MetaEditInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        Input = new MetaEditInput
        {
            ProjectId = project.Id,
            Name = project.Name,
            Description = project.Description,
            CaseFileNumber = project.CaseFileNumber
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Forbid();
        }

        var principal = _userContext.User;
        var isAdmin = principal.IsInRole("Admin");
        var isHoD = principal.IsInRole("HoD");

        var project = await _db.Projects
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        if (isHoD && !isAdmin &&
            !string.Equals(project.HodUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var trimmedCaseFileNumber = string.IsNullOrWhiteSpace(Input.CaseFileNumber)
            ? null
            : Input.CaseFileNumber.Trim();

        if (!string.IsNullOrEmpty(trimmedCaseFileNumber))
        {
            var exists = await _db.Projects
                .AnyAsync(
                    p => p.CaseFileNumber == trimmedCaseFileNumber && p.Id != project.Id,
                    cancellationToken);

            if (exists)
            {
                ModelState.AddModelError("Input.CaseFileNumber", "Case file number already exists.");
                return Page();
            }
        }

        project.Name = Input.Name.Trim();
        project.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();
        project.CaseFileNumber = trimmedCaseFileNumber;

        await _db.SaveChangesAsync(cancellationToken);

        return RedirectToPage("/Projects/Overview", new { id });
    }

    public sealed class MetaEditInput
    {
        public int ProjectId { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        [StringLength(64)]
        public string? CaseFileNumber { get; set; }
    }
}
