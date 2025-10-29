using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Ffc;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC.Records.Attachments;

[Authorize]
public class UploadModel(
    ApplicationDbContext db,
    IFfcAttachmentStorage storage,
    IOptions<FfcAttachmentOptions> options,
    IAuditService audit,
    ILogger<UploadModel> logger) : PageModel
{
    private readonly ApplicationDbContext _db = db;
    private readonly IFfcAttachmentStorage _storage = storage;
    private readonly FfcAttachmentOptions _options = options.Value;
    private readonly IAuditService _audit = audit;
    private readonly ILogger<UploadModel> _logger = logger;

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
        if (!await TryLoadRecordAsync(recordId))
        {
            return NotFound();
        }

        await LoadAttachmentItemsAsync(recordId);

        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(long recordId)
    {
        if (!CanManageAttachments) return Forbid();

        if (!await TryLoadRecordAsync(recordId))
        {
            return NotFound();
        }

        if (UploadFile is null || UploadFile.Length == 0)
        {
            ModelState.AddModelError(nameof(UploadFile), "Select a file.");
            await LoadAttachmentItemsAsync(recordId);
            return Page();
        }

        var result = await _storage.SaveAsync(recordId, UploadFile, Kind, Caption);
        if (!result.Success)
        {
            ModelState.AddModelError(nameof(UploadFile), result.ErrorMessage ?? "Upload failed.");
            await LoadAttachmentItemsAsync(recordId);
            return Page();
        }

        if (result.Attachment is { } attachment)
        {
            await TryLogAsync("ProjectOfficeReports.FFC.AttachmentUploaded", new Dictionary<string, string?>
            {
                ["AttachmentId"] = attachment.Id.ToString(),
                ["RecordId"] = attachment.FfcRecordId.ToString(),
                ["Kind"] = attachment.Kind.ToString(),
                ["Caption"] = attachment.Caption,
                ["ContentType"] = attachment.ContentType,
                ["SizeBytes"] = attachment.SizeBytes.ToString(CultureInfo.InvariantCulture),
                ["OriginalFileName"] = UploadFile.FileName
            });
        }

        TempData["StatusMessage"] = "File uploaded.";
        return RedirectToPage(new { recordId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(long recordId, long id)
    {
        if (!CanManageAttachments) return Forbid();

        if (!await TryLoadRecordAsync(recordId))
        {
            return NotFound();
        }

        var a = await _db.FfcAttachments.FirstOrDefaultAsync(x => x.Id == id && x.FfcRecordId == recordId);
        if (a is null) return NotFound();

        var data = new Dictionary<string, string?>
        {
            ["AttachmentId"] = a.Id.ToString(),
            ["RecordId"] = a.FfcRecordId.ToString(),
            ["Kind"] = a.Kind.ToString(),
            ["Caption"] = a.Caption,
            ["ContentType"] = a.ContentType,
            ["SizeBytes"] = a.SizeBytes.ToString(CultureInfo.InvariantCulture)
        };

        await _storage.DeleteAsync(a);

        await TryLogAsync("ProjectOfficeReports.FFC.AttachmentDeleted", data);
        TempData["StatusMessage"] = "Attachment removed.";
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

    private async Task LoadAttachmentItemsAsync(long recordId)
    {
        Items = await _db.FfcAttachments
            .Where(a => a.FfcRecordId == recordId)
            .OrderByDescending(a => a.UploadedAt)
            .AsNoTracking()
            .ToListAsync();
    }
}
