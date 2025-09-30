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
            CategoryId = project.CategoryId
        };

        await LoadCategoryOptionsAsync(project.CategoryId, cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            return BadRequest();
        }

        await LoadCategoryOptionsAsync(Input.CategoryId, cancellationToken);

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

        var submission = new ProjectMetaChangeRequestSubmission
        {
            ProjectId = id,
            Name = Input.Name,
            Description = Input.Description,
            CaseFileNumber = Input.CaseFileNumber,
            CategoryId = Input.CategoryId,
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

        [StringLength(1024)]
        public string? Reason { get; set; }
    }
}
