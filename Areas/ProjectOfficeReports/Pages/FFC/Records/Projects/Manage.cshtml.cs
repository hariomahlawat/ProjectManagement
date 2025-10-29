using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records.Projects;

[Authorize]
public class ManageModel(AppDbContext db) : PageModel
{
    private readonly AppDbContext _db = db;

    [FromQuery] public long RecordId { get; set; }
    public FfcRecord Record { get; private set; } = default!;
    public IList<FfcProject> Items { get; private set; } = [];
    public SelectList LinkedProjects { get; private set; } = default!;

    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public long? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Remarks { get; set; }
        public long? LinkedProjectId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(long recordId, long? id)
    {
        RecordId = recordId;
        Record = await _db.FfcRecords.Include(r => r.Country).FirstOrDefaultAsync(r => r.Id == RecordId)
                  ?? throw new Exception("Record not found.");

        Items = await _db.FfcProjects.Where(p => p.FfcRecordId == RecordId)
                    .OrderBy(p => p.Name).AsNoTracking().ToListAsync();

        LinkedProjects = new SelectList(await _db.Projects
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(), "Id", "Name");

        if (id.HasValue)
        {
            var p = await _db.FfcProjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value && x.FfcRecordId == RecordId);
            if (p is null) return NotFound();
            Input = new() { Id = p.Id, Name = p.Name, Remarks = p.Remarks, LinkedProjectId = p.LinkedProjectId };
        }
        return Page();
    }

    [Authorize(Roles = "Admin,HoD")]
    public async Task<IActionResult> OnPostCreateAsync(long recordId)
    {
        RecordId = recordId;
        if (string.IsNullOrWhiteSpace(Input.Name))
            ModelState.AddModelError(nameof(Input.Name), "Name is required.");

        if (!ModelState.IsValid) return await OnGetAsync(recordId, null);

        var entity = new FfcProject
        {
            FfcRecordId = recordId,
            Name = Input.Name.Trim(),
            Remarks = string.IsNullOrWhiteSpace(Input.Remarks) ? null : Input.Remarks.Trim(),
            LinkedProjectId = Input.LinkedProjectId
        };
        _db.FfcProjects.Add(entity);
        await _db.SaveChangesAsync();

        TempData["StatusMessage"] = "Project added.";
        return RedirectToPage(new { recordId });
    }

    [Authorize(Roles = "Admin,HoD")]
    public async Task<IActionResult> OnPostUpdateAsync(long recordId)
    {
        RecordId = recordId;
        if (Input.Id is null) return BadRequest();
        if (string.IsNullOrWhiteSpace(Input.Name))
            ModelState.AddModelError(nameof(Input.Name), "Name is required.");

        if (!ModelState.IsValid) return await OnGetAsync(recordId, Input.Id);

        var p = await _db.FfcProjects.FirstOrDefaultAsync(x => x.Id == Input.Id && x.FfcRecordId == recordId);
        if (p is null) return NotFound();

        p.Name = Input.Name.Trim();
        p.Remarks = string.IsNullOrWhiteSpace(Input.Remarks) ? null : Input.Remarks.Trim();
        p.LinkedProjectId = Input.LinkedProjectId;

        await _db.SaveChangesAsync();
        TempData["StatusMessage"] = "Project updated.";
        return RedirectToPage(new { recordId });
    }

    [Authorize(Roles = "Admin,HoD")]
    public async Task<IActionResult> OnPostDeleteAsync(long recordId, long id)
    {
        var p = await _db.FfcProjects.FirstOrDefaultAsync(x => x.Id == id && x.FfcRecordId == recordId);
        if (p is null) return NotFound();
        _db.FfcProjects.Remove(p);
        await _db.SaveChangesAsync();

        TempData["StatusMessage"] = "Project removed.";
        return RedirectToPage(new { recordId });
    }
}
