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
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Text;
using ProjectManagement.Models.Projects;

namespace ProjectManagement.Pages.Projects.Meta;

[Authorize(Roles = "Admin,HoD")]
[AutoValidateAntiforgeryToken]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IAuditService _audit;
    private readonly IMarkdownRenderer _markdownRenderer;

    public EditModel(ApplicationDbContext db, IUserContext userContext, IAuditService audit, IMarkdownRenderer markdownRenderer)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _markdownRenderer = markdownRenderer ?? throw new ArgumentNullException(nameof(markdownRenderer));
    }

    [BindProperty]
    public MetaEditInput Input { get; set; } = new();

    public IReadOnlyList<SelectListItem> CategoryOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> TechnicalCategoryOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> ProjectTypeOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> SponsoringUnitOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> LineDirectorateOptions { get; private set; } = Array.Empty<SelectListItem>();
    public bool IsLegacyProject { get; private set; }

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
        await LoadTechnicalCategoryOptionsAsync(project.TechnicalCategoryId, cancellationToken);
        await LoadProjectTypeOptionsAsync(project.ProjectTypeId, cancellationToken);
        await LoadLookupOptionsAsync(project.SponsoringUnitId, project.SponsoringLineDirectorateId, cancellationToken);
        IsLegacyProject = project.IsLegacy;

        var approxProductionCost = await LoadApproxProductionCostAsync(project.Id, cancellationToken);

        Input = new MetaEditInput
        {
            ProjectId = project.Id,
            Name = project.Name,
            Description = project.Description,
            CaseFileNumber = project.CaseFileNumber,
            CategoryId = project.CategoryId,
            TechnicalCategoryId = project.TechnicalCategoryId,
            ProjectTypeId = project.ProjectTypeId,
            IsBuild = project.IsBuild,
            SponsoringUnitId = project.SponsoringUnitId,
            SponsoringLineDirectorateId = project.SponsoringLineDirectorateId,
            RdCostLakhs = project.CostLakhs,
            ApproxProductionCost = approxProductionCost,
            RowVersion = Convert.ToBase64String(project.RowVersion)
        };

        return Page();
    }


    // SECTION: Description markdown preview
    public IActionResult OnPostPreview([FromForm] string? description)
    {
        var html = _markdownRenderer.ToSafeHtml(description);
        return new JsonResult(new { html });
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            return BadRequest();
        }

        await LoadCategoryOptionsAsync(Input.CategoryId, cancellationToken);
        await LoadTechnicalCategoryOptionsAsync(Input.TechnicalCategoryId, cancellationToken);
        await LoadProjectTypeOptionsAsync(Input.ProjectTypeId, cancellationToken);
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

        IsLegacyProject = project.IsLegacy;

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
        var selectedTechnicalCategoryId = Input.TechnicalCategoryId;
        var selectedProjectTypeId = Input.ProjectTypeId;
        var previousProductionCost = project.IsLegacy
            ? await LoadApproxProductionCostAsync(project.Id, cancellationToken)
            : null;
        ProjectProductionCostFact? productionFact = null;

        // SECTION: Legacy cost validation
        if (project.IsLegacy)
        {
            productionFact = await _db.ProjectProductionCostFacts
                .SingleOrDefaultAsync(f => f.ProjectId == project.Id, cancellationToken);

            if (Input.RdCostLakhs is < 0)
            {
                ModelState.AddModelError("Input.RdCostLakhs", "R&D / L1 cost cannot be negative.");
            }

            if (Input.ApproxProductionCost is < 0)
            {
                ModelState.AddModelError("Input.ApproxProductionCost", "Approx Prod cost cannot be negative.");
            }
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

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

        if (selectedTechnicalCategoryId.HasValue)
        {
            var technicalCategoryExists = await _db.TechnicalCategories
                .AsNoTracking()
                .AnyAsync(c => c.Id == selectedTechnicalCategoryId.Value && c.IsActive, cancellationToken);

            if (!technicalCategoryExists)
            {
                ModelState.AddModelError("Input.TechnicalCategoryId", ProjectValidationMessages.InactiveTechnicalCategory);
                return Page();
            }
        }

        if (selectedProjectTypeId.HasValue)
        {
            var projectTypeExists = await _db.ProjectTypes
                .AsNoTracking()
                .AnyAsync(p => p.Id == selectedProjectTypeId.Value && p.IsActive, cancellationToken);

            if (!projectTypeExists)
            {
                ModelState.AddModelError("Input.ProjectTypeId", ProjectValidationMessages.InactiveProjectType);
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
        var previousTechnicalCategoryId = project.TechnicalCategoryId;
        var previousProjectTypeId = project.ProjectTypeId;
        var previousIsBuild = project.IsBuild;
        var previousSponsoringUnitId = project.SponsoringUnitId;
        var previousSponsoringLineDirectorateId = project.SponsoringLineDirectorateId;
        var previousRdCostLakhs = project.CostLakhs;

        project.Name = trimmedName;
        project.Description = trimmedDescription;
        project.CaseFileNumber = trimmedCaseFileNumber;
        project.CategoryId = selectedCategoryId;
        project.TechnicalCategoryId = selectedTechnicalCategoryId;
        project.ProjectTypeId = selectedProjectTypeId;
        project.IsBuild = Input.IsBuild;
        project.SponsoringUnitId = Input.SponsoringUnitId;
        project.SponsoringLineDirectorateId = Input.SponsoringLineDirectorateId;

        // SECTION: Legacy cost persistence
        if (project.IsLegacy)
        {
            project.CostLakhs = Input.RdCostLakhs;

            productionFact ??= new ProjectProductionCostFact
            {
                ProjectId = project.Id
            };

            if (_db.Entry(productionFact).State == EntityState.Detached)
            {
                await _db.ProjectProductionCostFacts.AddAsync(productionFact, cancellationToken);
            }

            productionFact.ApproxProductionCost = Input.ApproxProductionCost;
            productionFact.UpdatedAtUtc = DateTimeOffset.UtcNow;
            productionFact.UpdatedByUserId = userId;
        }

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
            await LoadTechnicalCategoryOptionsAsync(project.TechnicalCategoryId, cancellationToken);
            await LoadProjectTypeOptionsAsync(project.ProjectTypeId, cancellationToken);
            await LoadLookupOptionsAsync(project.SponsoringUnitId, project.SponsoringLineDirectorateId, cancellationToken);
            IsLegacyProject = project.IsLegacy;

            var updatedProductionCost = project.IsLegacy
                ? await LoadApproxProductionCostAsync(project.Id, cancellationToken)
                : null;

            Input = new MetaEditInput
            {
                ProjectId = project.Id,
                Name = project.Name,
                Description = project.Description,
                CaseFileNumber = project.CaseFileNumber,
                CategoryId = project.CategoryId,
                TechnicalCategoryId = project.TechnicalCategoryId,
                ProjectTypeId = project.ProjectTypeId,
                IsBuild = project.IsBuild,
                SponsoringUnitId = project.SponsoringUnitId,
                SponsoringLineDirectorateId = project.SponsoringLineDirectorateId,
                RdCostLakhs = project.CostLakhs,
                ApproxProductionCost = updatedProductionCost,
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
                ["TechnicalCategoryIdBefore"] = previousTechnicalCategoryId?.ToString(),
                ["TechnicalCategoryIdAfter"] = project.TechnicalCategoryId?.ToString(),
                ["ProjectTypeIdBefore"] = previousProjectTypeId?.ToString(),
                ["ProjectTypeIdAfter"] = project.ProjectTypeId?.ToString(),
                ["IsBuildBefore"] = previousIsBuild.ToString(),
                ["IsBuildAfter"] = project.IsBuild.ToString(),
                ["SponsoringUnitIdBefore"] = previousSponsoringUnitId?.ToString(),
                ["SponsoringUnitIdAfter"] = project.SponsoringUnitId?.ToString(),
                ["SponsoringLineDirectorateIdBefore"] = previousSponsoringLineDirectorateId?.ToString(),
                ["SponsoringLineDirectorateIdAfter"] = project.SponsoringLineDirectorateId?.ToString(),
                ["RdCostLakhsBefore"] = previousRdCostLakhs?.ToString(),
                ["RdCostLakhsAfter"] = project.CostLakhs?.ToString(),
                ["ApproxProductionCostBefore"] = previousProductionCost?.ToString(),
                ["ApproxProductionCostAfter"] = productionFact?.ApproxProductionCost?.ToString()
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

        [StringLength(ProjectFieldLimits.DescriptionMaxLength)]
        public string? Description { get; set; }

        [StringLength(64)]
        public string? CaseFileNumber { get; set; }

        public int? CategoryId { get; set; }

        [Display(Name = "Technical Category")]
        public int? TechnicalCategoryId { get; set; }

        // SECTION: Project type and build flag
        [Display(Name = "Project type")]
        public int? ProjectTypeId { get; set; }

        [Display(Name = "Build (repeat / re-manufacture)")]
        public bool IsBuild { get; set; }

        [Display(Name = "Sponsoring Unit")]
        public int? SponsoringUnitId { get; set; }

        [Display(Name = "Sponsoring Line Dte")]
        public int? SponsoringLineDirectorateId { get; set; }

        public decimal? RdCostLakhs { get; set; }

        public decimal? ApproxProductionCost { get; set; }

        public string RowVersion { get; set; } = string.Empty;
    }

    private Task<decimal?> LoadApproxProductionCostAsync(int projectId, CancellationToken cancellationToken)
    {
        return _db.ProjectProductionCostFacts
            .AsNoTracking()
            .Where(f => f.ProjectId == projectId)
            .Select(f => f.ApproxProductionCost)
            .SingleOrDefaultAsync(cancellationToken);
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

    private async Task LoadTechnicalCategoryOptionsAsync(int? selectedTechnicalCategoryId, CancellationToken cancellationToken)
    {
        var categories = await _db.TechnicalCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var children = categories.ToLookup(c => c.ParentId);

        var options = new List<SelectListItem>
        {
            new("— (none) —", string.Empty, selectedTechnicalCategoryId is null)
        };

        void AddOptions(int? parentId, string prefix)
        {
            foreach (var category in children[parentId])
            {
                var text = string.IsNullOrEmpty(prefix) ? category.Name : $"{prefix}{category.Name}";
                options.Add(new SelectListItem(text, category.Id.ToString(), category.Id == selectedTechnicalCategoryId));
                AddOptions(category.Id, string.Concat(prefix, "— "));
            }
        }

        AddOptions(null, string.Empty);
        if (selectedTechnicalCategoryId.HasValue)
        {
            var selectedValue = selectedTechnicalCategoryId.Value.ToString();
            if (options.All(option => !string.Equals(option.Value, selectedValue, StringComparison.Ordinal)))
            {
                var selected = await _db.TechnicalCategories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == selectedTechnicalCategoryId.Value, cancellationToken);

                if (selected is not null)
                {
                    options.Add(new SelectListItem($"{selected.Name} (inactive)", selected.Id.ToString(), true));
                }
            }
        }
        TechnicalCategoryOptions = options;
    }

    // SECTION: Project type options
    private async Task LoadProjectTypeOptionsAsync(int? selectedProjectTypeId, CancellationToken cancellationToken)
    {
        var types = await _db.ProjectTypes
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);

        var items = types.Select(p => (Id: p.Id, Name: p.Name)).ToList();
        if (selectedProjectTypeId.HasValue && items.All(p => p.Id != selectedProjectTypeId.Value))
        {
            var selectedType = await _db.ProjectTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == selectedProjectTypeId.Value, cancellationToken);
            if (selectedType is not null)
            {
                items.Add((selectedType.Id, $"{selectedType.Name} (inactive)"));
            }
        }

        ProjectTypeOptions = BuildLookupOptions(items, selectedProjectTypeId);
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
