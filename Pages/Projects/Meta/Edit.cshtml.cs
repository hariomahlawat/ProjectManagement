using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects.Meta;

[Authorize(Roles = "Admin,HoD")]
[AutoValidateAntiforgeryToken]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IAuditService _audit;

    public EditModel(ApplicationDbContext db, IUserContext userContext, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    [BindProperty]
    public MetaEditInput Input { get; set; } = new();

    public IReadOnlyList<SelectListItem> CategoryOptions { get; private set; } = Array.Empty<SelectListItem>();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        await LoadCategoryOptionsAsync(project.CategoryId, cancellationToken);

        Input = new MetaEditInput
        {
            ProjectId = project.Id,
            Name = project.Name,
            Description = project.Description,
            CaseFileNumber = project.CaseFileNumber,
            CategoryId = project.CategoryId,
            RowVersion = Convert.ToBase64String(project.RowVersion)
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            return BadRequest();
        }

        await LoadCategoryOptionsAsync(Input.CategoryId, cancellationToken);

        byte[] rowVersionBytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(Input.RowVersion))
        {
            ModelState.AddModelError(string.Empty, "The form has expired. Please reload and try again.");
        }
        else
        {
            try
            {
                rowVersionBytes = Convert.FromBase64String(Input.RowVersion);
            }
            catch (FormatException)
            {
                ModelState.AddModelError(string.Empty, "The form has expired. Please reload and try again.");
            }
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

        var trimmedName = Input.Name.Trim();
        var trimmedDescription = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();
        var trimmedCaseFileNumber = string.IsNullOrWhiteSpace(Input.CaseFileNumber)
            ? null
            : Input.CaseFileNumber.Trim();
        var selectedCategoryId = Input.CategoryId;

        if (selectedCategoryId.HasValue)
        {
            var categoryExists = await _db.ProjectCategories
                .AsNoTracking()
                .AnyAsync(c => c.Id == selectedCategoryId.Value && c.IsActive, cancellationToken);

            if (!categoryExists)
            {
                ModelState.AddModelError("Input.CategoryId", ProjectValidationMessages.InactiveCategory);
                return Page();
            }
        }

        if (!string.IsNullOrEmpty(trimmedCaseFileNumber))
        {
            var duplicate = await _db.Projects
                .AsNoTracking()
                .AnyAsync(
                    p => p.Id != id
                        && p.CaseFileNumber != null
                        && p.CaseFileNumber == trimmedCaseFileNumber,
                    cancellationToken);

            if (duplicate)
            {
                ModelState.AddModelError("Input.CaseFileNumber", ProjectValidationMessages.DuplicateCaseFileNumber);
                return Page();
            }
        }

        var previousName = project.Name;
        var previousDescription = project.Description;
        var previousCaseFileNumber = project.CaseFileNumber;
        var previousCategoryId = project.CategoryId;

        project.Name = trimmedName;
        project.Description = trimmedDescription;
        project.CaseFileNumber = trimmedCaseFileNumber;
        project.CategoryId = selectedCategoryId;

        _db.Entry(project).Property(p => p.RowVersion).OriginalValue = rowVersionBytes;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            ModelState.AddModelError(string.Empty, "The project was modified by someone else. Please reload and try again.");
            await _db.Entry(project).ReloadAsync(cancellationToken);
            await LoadCategoryOptionsAsync(project.CategoryId, cancellationToken);

            Input = new MetaEditInput
            {
                ProjectId = project.Id,
                Name = project.Name,
                Description = project.Description,
                CaseFileNumber = project.CaseFileNumber,
                CategoryId = project.CategoryId,
                RowVersion = Convert.ToBase64String(project.RowVersion)
            };

            return Page();
        }

        await _audit.LogAsync(
            "Projects.MetaChangedDirect",
            data: new Dictionary<string, string?>
            {
                ["ProjectId"] = project.Id.ToString(),
                ["NameBefore"] = previousName,
                ["NameAfter"] = project.Name,
                ["DescriptionBefore"] = previousDescription,
                ["DescriptionAfter"] = project.Description,
                ["CaseFileNumberBefore"] = previousCaseFileNumber,
                ["CaseFileNumberAfter"] = project.CaseFileNumber,
                ["CategoryIdBefore"] = previousCategoryId?.ToString(),
                ["CategoryIdAfter"] = project.CategoryId?.ToString()
            },
            userId: userId,
            userName: User.Identity?.Name);

        TempData["Flash"] = "Project details updated.";

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

        public int? CategoryId { get; set; }

        public string RowVersion { get; set; } = string.Empty;
    }

    private async Task LoadCategoryOptionsAsync(int? selectedCategoryId, CancellationToken cancellationToken)
    {
        var categories = await _db.ProjectCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var children = categories
            .ToLookup(c => c.ParentId);

        var options = new List<SelectListItem>
        {
            new("— (none) —", string.Empty, selectedCategoryId is null)
        };

        void AddOptions(int? parentId, string prefix)
        {
            foreach (var category in children[parentId])
            {
                var text = string.IsNullOrEmpty(prefix) ? category.Name : $"{prefix}{category.Name}";
                options.Add(new SelectListItem(text, category.Id.ToString(), category.Id == selectedCategoryId));
                AddOptions(category.Id, string.Concat(prefix, "— "));
            }
        }

        AddOptions(null, string.Empty);
        CategoryOptions = options;
    }
}
