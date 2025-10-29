using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records.Projects;

[Authorize]
public class ManageModel(ApplicationDbContext db, IAuditService audit, ILogger<ManageModel> logger) : PageModel
{
    private readonly ApplicationDbContext _db = db;
    private readonly IAuditService _audit = audit;
    private readonly ILogger<ManageModel> _logger = logger;

    [FromQuery] public long RecordId { get; set; }
    public FfcRecord Record { get; private set; } = default!;
    public IList<FfcProject> Items { get; private set; } = [];
    public SelectList LinkedProjects { get; private set; } = default!;
    private bool CanManageProjects => User.IsInRole("Admin") || User.IsInRole("HoD");

    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public long? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Remarks { get; set; }
        public int? LinkedProjectId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(long recordId, long? id)
    {
        if (!await TryLoadRecordAsync(recordId))
        {
            return NotFound();
        }

        if (id.HasValue)
        {
            var project = await _db.FfcProjects
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id.Value && x.FfcRecordId == RecordId);
            if (project is null)
            {
                return NotFound();
            }

            Input = new()
            {
                Id = project.Id,
                Name = project.Name,
                Remarks = project.Remarks,
                LinkedProjectId = project.LinkedProjectId
            };
        }
        else
        {
            Input = new();
        }

        await LoadPageDataAsync(recordId, Input.LinkedProjectId);

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(long recordId)
    {
        if (!CanManageProjects) return Forbid();

        if (!await TryLoadRecordAsync(recordId))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Input.Name))
            ModelState.AddModelError(nameof(Input.Name), "Name is required.");

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync(recordId, Input.LinkedProjectId);
            return Page();
        }

        var entity = new FfcProject
        {
            FfcRecordId = recordId,
            Name = Input.Name.Trim(),
            Remarks = string.IsNullOrWhiteSpace(Input.Remarks) ? null : Input.Remarks.Trim(),
            LinkedProjectId = Input.LinkedProjectId
        };
        _db.FfcProjects.Add(entity);
        await _db.SaveChangesAsync();

        await TryLogAsync("ProjectOfficeReports.FFC.RecordProjectCreated", BuildProjectData(entity, "After"));

        TempData["StatusMessage"] = "Project added.";
        return RedirectToPage(new { recordId });
    }

    public async Task<IActionResult> OnPostUpdateAsync(long recordId)
    {
        if (!CanManageProjects) return Forbid();

        RecordId = recordId;
        if (Input.Id is null) return BadRequest();

        if (!await TryLoadRecordAsync(recordId))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Input.Name))
            ModelState.AddModelError(nameof(Input.Name), "Name is required.");

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync(recordId, Input.LinkedProjectId);
            return Page();
        }

        var p = await _db.FfcProjects.FirstOrDefaultAsync(x => x.Id == Input.Id && x.FfcRecordId == recordId);
        if (p is null) return NotFound();

        var before = BuildProjectData(p, "Before");

        p.Name = Input.Name.Trim();
        p.Remarks = string.IsNullOrWhiteSpace(Input.Remarks) ? null : Input.Remarks.Trim();
        p.LinkedProjectId = Input.LinkedProjectId;

        await _db.SaveChangesAsync();

        var data = new Dictionary<string, string?>(before);
        foreach (var kvp in BuildProjectData(p, "After"))
        {
            data[kvp.Key] = kvp.Value;
        }

        await TryLogAsync("ProjectOfficeReports.FFC.RecordProjectUpdated", data);
        TempData["StatusMessage"] = "Project updated.";
        return RedirectToPage(new { recordId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(long recordId, long id)
    {
        if (!CanManageProjects) return Forbid();

        if (!await TryLoadRecordAsync(recordId))
        {
            return NotFound();
        }

        var p = await _db.FfcProjects.FirstOrDefaultAsync(x => x.Id == id && x.FfcRecordId == recordId);
        if (p is null) return NotFound();

        var auditData = BuildProjectData(p, "Before");

        _db.FfcProjects.Remove(p);
        await _db.SaveChangesAsync();

        await TryLogAsync("ProjectOfficeReports.FFC.RecordProjectDeleted", auditData);

        TempData["StatusMessage"] = "Project removed.";
        return RedirectToPage(new { recordId });
    }

    private async Task TryLogAsync(string action, IDictionary<string, string?> data)
    {
        try
        {
            await _audit.LogAsync(
                action,
                userId: User.FindFirstValue(ClaimTypes.NameIdentifier),
                userName: User.Identity?.Name,
                data: data,
                http: HttpContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for action {Action}.", action);
        }
    }

    private static Dictionary<string, string?> BuildProjectData(FfcProject project, string prefix)
    {
        return new Dictionary<string, string?>
        {
            [$"{prefix}.ProjectId"] = project.Id.ToString(),
            [$"{prefix}.RecordId"] = project.FfcRecordId.ToString(),
            [$"{prefix}.Name"] = project.Name,
            [$"{prefix}.Remarks"] = project.Remarks,
            [$"{prefix}.LinkedProjectId"] = project.LinkedProjectId?.ToString()
        };
    }

    private async Task<bool> TryLoadRecordAsync(long recordId)
    {
        RecordId = recordId;

        var record = await _db.FfcRecords
            .Include(r => r.Country)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recordId);

        if (record is null)
        {
            return false;
        }

        Record = record;
        return true;
    }

    private async Task LoadPageDataAsync(long recordId, int? selectedLinkedProjectId)
    {
        Items = await _db.FfcProjects
            .Where(p => p.FfcRecordId == recordId)
            .Include(p => p.LinkedProject)
            .OrderBy(p => p.Name)
            .AsNoTracking()
            .ToListAsync();

        var linkedProjects = await _db.Projects
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync();

        LinkedProjects = new SelectList(linkedProjects, "Id", "Name", selectedLinkedProjectId);
    }
}
