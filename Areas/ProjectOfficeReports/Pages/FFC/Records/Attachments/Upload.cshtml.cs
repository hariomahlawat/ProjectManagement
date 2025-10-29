using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Ffc;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Configuration;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records.Attachments;

[Authorize]
public class UploadModel(
    ApplicationDbContext db,
    IFfcAttachmentStorage storage,
    IOptions<FfcAttachmentOptions> options) : PageModel
{
    private readonly ApplicationDbContext _db = db;
    private readonly IFfcAttachmentStorage _storage = storage;
    private readonly FfcAttachmentOptions _options = options.Value;

    [FromQuery] public long RecordId { get; set; }
    public FfcRecord Record { get; private set; } = default!;
    public IList<FfcAttachment> Items { get; private set; } = [];
    private bool CanManageAttachments => User.IsInRole("Admin") || User.IsInRole("HoD");

    [BindProperty] public IFormFile? UploadFile { get; set; }
    [BindProperty] public FfcAttachmentKind Kind { get; set; } = FfcAttachmentKind.Pdf;
    [BindProperty] public string? Caption { get; set; }
    public long MaxFileSizeBytes => _options.MaxFileSizeBytes;

    public async Task<IActionResult> OnGetAsync(long recordId)
    {
        RecordId = recordId;
        Record = await _db.FfcRecords.Include(r => r.Country).FirstOrDefaultAsync(r => r.Id == recordId)
                  ?? throw new Exception("Record not found.");

        Items = await _db.FfcAttachments.Where(a => a.FfcRecordId == recordId)
                    .OrderByDescending(a => a.UploadedAt).AsNoTracking().ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(long recordId)
    {
        if (!CanManageAttachments) return Forbid();

        if (UploadFile is null || UploadFile.Length == 0)
        {
            ModelState.AddModelError(nameof(UploadFile), "Select a file.");
            return await OnGetAsync(recordId);
        }

        var result = await _storage.SaveAsync(recordId, UploadFile, Kind, Caption);
        if (!result.Success)
        {
            ModelState.AddModelError(nameof(UploadFile), result.ErrorMessage ?? "Upload failed.");
            return await OnGetAsync(recordId);
        }

        TempData["StatusMessage"] = "File uploaded.";
        return RedirectToPage(new { recordId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(long recordId, long id)
    {
        if (!CanManageAttachments) return Forbid();

        var a = await _db.FfcAttachments.FirstOrDefaultAsync(x => x.Id == id && x.FfcRecordId == recordId);
        if (a is null) return NotFound();

        await _storage.DeleteAsync(a);
        TempData["StatusMessage"] = "Attachment removed.";
        return RedirectToPage(new { recordId });
    }
}
