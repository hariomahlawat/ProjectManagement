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
    public IReadOnlyList<SelectListItem> SponsoringUnitOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> LineDirectorateOptions { get; private set; } = Array.Empty<SelectListItem>();

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
        await LoadLookupOptionsAsync(project.SponsoringUnitId, project.SponsoringLineDirectorateId, cancellationToken);

        Input = new MetaEditInput
        {
            ProjectId = project.Id,
            Name = project.Name,
            Description = project.Description,
            CaseFileNumber = project.CaseFileNumber,
            CategoryId = project.CategoryId,
            SponsoringUnitId = project.SponsoringUnitId,
            SponsoringLineDirectorateId = project.SponsoringLineDirectorateId,
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
        await LoadLookupOptionsAsync(Input.SponsoringUnitId, Input.SponsoringLineDirectorateId, cancellationToken);

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

        if (Input.SponsoringUnitId.HasValue)
        {
            var unitActive = await _db.SponsoringUnits
                .AsNoTracking()
                .AnyAsync(u => u.Id == Input.SponsoringUnitId.Value && u.IsActive, cancellationToken);

            if (!unitActive)
            {
                ModelState.AddModelError("Input.SponsoringUnitId", ProjectValidationMessages.InactiveSponsoringUnit);
                return Page();
            }
        }

        if (Input.SponsoringLineDirectorateId.HasValue)
        {
            var lineActive = await _db.LineDirectorates
                .AsNoTracking()
                .AnyAsync(l => l.Id == Input.SponsoringLineDirectorateId.Value && l.IsActive, cancellationToken);

            if (!lineActive)
            {
                ModelState.AddModelError("Input.SponsoringLineDirectorateId", ProjectValidationMessages.InactiveLineDirectorate);
                return Page();
            }
        }

        var previousName = project.Name;
        var previousDescription = project.Description;
        var previousCaseFileNumber = project.CaseFileNumber;
        var previousCategoryId = project.CategoryId;
        var previousSponsoringUnitId = project.SponsoringUnitId;
        var previousSponsoringLineDirectorateId = project.SponsoringLineDirectorateId;

        project.Name = trimmedName;
        project.Description = trimmedDescription;
        project.CaseFileNumber = trimmedCaseFileNumber;
        project.CategoryId = selectedCategoryId;
        project.SponsoringUnitId = Input.SponsoringUnitId;
        project.SponsoringLineDirectorateId = Input.SponsoringLineDirectorateId;

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
            await LoadLookupOptionsAsync(project.SponsoringUnitId, project.SponsoringLineDirectorateId, cancellationToken);

            Input = new MetaEditInput
            {
                ProjectId = project.Id,
                Name = project.Name,
                Description = project.Description,
                CaseFileNumber = project.CaseFileNumber,
                CategoryId = project.CategoryId,
                SponsoringUnitId = project.SponsoringUnitId,
                SponsoringLineDirectorateId = project.SponsoringLineDirectorateId,
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
                ["CategoryIdAfter"] = project.CategoryId?.ToString(),
                ["SponsoringUnitIdBefore"] = previousSponsoringUnitId?.ToString(),
                ["SponsoringUnitIdAfter"] = project.SponsoringUnitId?.ToString(),
                ["SponsoringLineDirectorateIdBefore"] = previousSponsoringLineDirectorateId?.ToString(),
                ["SponsoringLineDirectorateIdAfter"] = project.SponsoringLineDirectorateId?.ToString()
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

        [Display(Name = "Sponsoring Unit")]
        public int? SponsoringUnitId { get; set; }

        [Display(Name = "Sponsoring Line Dte")]
        public int? SponsoringLineDirectorateId { get; set; }

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

    private async Task LoadLookupOptionsAsync(int? sponsoringUnitId, int? sponsoringLineDirectorateId, CancellationToken cancellationToken)
    {
        var units = await _db.SponsoringUnits
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.SortOrder)
            .ThenBy(u => u.Name)
            .Select(u => new { u.Id, u.Name })
            .ToListAsync(cancellationToken);

        var directorates = await _db.LineDirectorates
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name)
            .Select(l => new { l.Id, l.Name })
            .ToListAsync(cancellationToken);

        var unitItems = units.Select(u => (Id: u.Id, Name: u.Name)).ToList();
        if (sponsoringUnitId.HasValue && unitItems.All(u => u.Id != sponsoringUnitId.Value))
        {
            var selectedUnit = await _db.SponsoringUnits
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == sponsoringUnitId.Value, cancellationToken);
            if (selectedUnit is not null)
            {
                unitItems.Add((selectedUnit.Id, $"{selectedUnit.Name} (inactive)"));
            }
        }

        var directorateItems = directorates.Select(l => (Id: l.Id, Name: l.Name)).ToList();
        if (sponsoringLineDirectorateId.HasValue && directorateItems.All(l => l.Id != sponsoringLineDirectorateId.Value))
        {
            var selectedDirectorate = await _db.LineDirectorates
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == sponsoringLineDirectorateId.Value, cancellationToken);
            if (selectedDirectorate is not null)
            {
                directorateItems.Add((selectedDirectorate.Id, $"{selectedDirectorate.Name} (inactive)"));
            }
        }

        SponsoringUnitOptions = BuildLookupOptions(unitItems, sponsoringUnitId);
        LineDirectorateOptions = BuildLookupOptions(directorateItems, sponsoringLineDirectorateId);
    }

    private static IReadOnlyList<SelectListItem> BuildLookupOptions(IEnumerable<(int Id, string Name)> items, int? selectedId)
    {
        var list = new List<SelectListItem>
        {
            new("— (none) —", string.Empty, selectedId is null)
        };

        var selectedValue = selectedId?.ToString();
        foreach (var (id, name) in items)
        {
            list.Add(new SelectListItem(name, id.ToString(), selectedValue == id.ToString()));
        }

        return list;
    }
}
