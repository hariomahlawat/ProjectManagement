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

namespace ProjectManagement.Pages.Projects.Meta;

[Authorize(Roles = "Project Officer")]
[AutoValidateAntiforgeryToken]
public sealed class RequestModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ProjectMetaChangeRequestService _service;
    private readonly IUserContext _userContext;

    public RequestModel(ApplicationDbContext db, ProjectMetaChangeRequestService service, IUserContext userContext)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    [BindProperty]
    public RequestInput Input { get; set; } = new();

    public string ProjectName { get; private set; } = string.Empty;

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

        var userId = _userContext.UserId;
        if (!IsLeadProjectOfficer(project, userId))
        {
            return Forbid();
        }

        ProjectName = project.Name;
        Input = new RequestInput
        {
            ProjectId = project.Id,
            Name = project.Name,
            Description = project.Description,
            CaseFileNumber = project.CaseFileNumber,
            CategoryId = project.CategoryId,
            SponsoringUnitId = project.SponsoringUnitId,
            SponsoringLineDirectorateId = project.SponsoringLineDirectorateId
        };

        await LoadCategoryOptionsAsync(project.CategoryId, cancellationToken);
        await LoadLookupOptionsAsync(project.SponsoringUnitId, project.SponsoringLineDirectorateId, cancellationToken);

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

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        ProjectName = project.Name;

        var userId = _userContext.UserId;
        if (!IsLeadProjectOfficer(project, userId))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return Page();
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

        var submission = new ProjectMetaChangeRequestSubmission
        {
            ProjectId = id,
            Name = Input.Name,
            Description = Input.Description,
            CaseFileNumber = Input.CaseFileNumber,
            CategoryId = Input.CategoryId,
            SponsoringUnitId = Input.SponsoringUnitId,
            SponsoringLineDirectorateId = Input.SponsoringLineDirectorateId,
            Reason = Input.Reason
        };

        var result = await _service.SubmitAsync(submission, userId!, cancellationToken);

        if (result.Outcome == ProjectMetaChangeRequestSubmissionOutcome.Success)
        {
            TempData["Flash"] = "Change request sent for HoD approval.";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        if (result.Outcome == ProjectMetaChangeRequestSubmissionOutcome.ProjectNotFound)
        {
            return NotFound();
        }

        if (result.Outcome == ProjectMetaChangeRequestSubmissionOutcome.NotProjectOfficer)
        {
            return Forbid();
        }

        foreach (var kvp in result.Errors)
        {
            var key = string.IsNullOrWhiteSpace(kvp.Key) ? string.Empty : $"Input.{kvp.Key}";
            foreach (var message in kvp.Value)
            {
                ModelState.AddModelError(key, message);
            }
        }

        return Page();
    }

    private static bool IsLeadProjectOfficer(Project project, string? userId)
        => !string.IsNullOrWhiteSpace(userId)
            && string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase);

    private async Task LoadCategoryOptionsAsync(int? selectedCategoryId, CancellationToken cancellationToken)
    {
        var categories = await _db.ProjectCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var children = categories.ToLookup(c => c.ParentId);

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

    private static IReadOnlyList<SelectListItem> BuildLookupOptions(IEnumerable<(int Id, string Name)> items, int? selected)
    {
        var list = new List<SelectListItem>
        {
            new("— (none) —", string.Empty, selected is null)
        };

        var selectedValue = selected?.ToString();
        foreach (var (id, name) in items)
        {
            list.Add(new SelectListItem(name, id.ToString(), selectedValue == id.ToString()));
        }

        return list;
    }

    public sealed class RequestInput
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

        [StringLength(1024)]
        public string? Reason { get; set; }
    }
}
