using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Utilities;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

[Authorize(Policy = Policies.Ipr.Edit)]
public sealed class ManageModel : PageModel
{
    private static readonly IReadOnlyDictionary<string, string[]> ValidationErrorFieldMap =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Filed date cannot be in the future."] = new[] { nameof(IndexModel.RecordInput.FiledOn) },
            ["Grant date cannot be in the future."] = new[] { nameof(IndexModel.RecordInput.GrantedOn) },
            ["Filed date is required once the record is not under filing."] = new[] { nameof(IndexModel.RecordInput.FiledOn) },
            ["Grant date is required once the record is granted."] = new[] { nameof(IndexModel.RecordInput.GrantedOn) },
            ["Grant date cannot be provided without a filing date."] = new[] { nameof(IndexModel.RecordInput.FiledOn), nameof(IndexModel.RecordInput.GrantedOn) },
            ["Grant date cannot be earlier than the filing date."] = new[] { nameof(IndexModel.RecordInput.FiledOn), nameof(IndexModel.RecordInput.GrantedOn) },
            ["A patent record with the same filing number and type already exists."] = new[] { nameof(IndexModel.RecordInput.FilingNumber) },
            ["Filing number is required."] = new[] { nameof(IndexModel.RecordInput.FilingNumber) }
        };

    private readonly ApplicationDbContext _db;
    private readonly IIprReadService _readService;
    private readonly IIprWriteService _writeService;
    private readonly UserManager<ApplicationUser> _userManager;

    private string? _query;

    public ManageModel(
        ApplicationDbContext db,
        IIprReadService readService,
        IIprWriteService writeService,
        UserManager<ApplicationUser> userManager)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _writeService = writeService ?? throw new ArgumentNullException(nameof(writeService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    [BindProperty(SupportsGet = true)]
    public string? Query
    {
        get => _query;
        set => _query = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    [BindProperty]
    public IndexModel.RecordInput Input { get; set; } = CreateDefaultInput();

    [BindProperty]
    public IndexModel.UploadAttachmentInput UploadInput { get; set; } = new();

    [BindProperty]
    public IndexModel.RemoveAttachmentInput RemoveAttachment { get; set; } = new();

    public IReadOnlyList<RecordRow> Records { get; private set; } = Array.Empty<RecordRow>();

    public IReadOnlyList<SelectListItem> TypeFormOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> StatusFormOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> ProjectOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<IndexModel.AttachmentViewModel> Attachments { get; private set; } = Array.Empty<IndexModel.AttachmentViewModel>();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Input = CreateDefaultInput();

        if (EditId.HasValue)
        {
            var record = await LoadRecordAsync(EditId.Value, cancellationToken, overwriteInput: true);
            if (record is null)
            {
                TempData["ToastError"] = "The selected patent record could not be found.";
                EditId = null;
            }
        }
        else
        {
            Attachments = Array.Empty<IndexModel.AttachmentViewModel>();
            UploadInput = new IndexModel.UploadAttachmentInput();
        }

        await LoadSelectListsAsync(cancellationToken);
        await LoadRecordsAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        EditId = null;

        if (!ModelState.IsValid)
        {
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            Attachments = Array.Empty<IndexModel.AttachmentViewModel>();
            UploadInput = new IndexModel.UploadAttachmentInput();
            return Page();
        }

        if (Input.Type is null)
        {
            ModelState.AddModelError("Input.Type", "Select a type.");
        }

        if (Input.Status is null)
        {
            ModelState.AddModelError("Input.Status", "Select a status.");
        }

        if (!ModelState.IsValid)
        {
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            if (Input.Id.HasValue)
            {
                await LoadRecordAsync(Input.Id.Value, cancellationToken, overwriteInput: false);
            }
            else
            {
                Attachments = Array.Empty<IndexModel.AttachmentViewModel>();
                UploadInput = new IndexModel.UploadAttachmentInput();
            }
            return Page();
        }

        try
        {
            var entity = ToEntity(Input);
            await _writeService.CreateAsync(entity, cancellationToken);
            TempData["ToastMessage"] = "Patent record created.";
            return RedirectToPage("./Manage", new { Query });
        }
        catch (InvalidOperationException ex)
        {
            if (!TryAddInputValidationErrors(ex.Message))
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            Attachments = Array.Empty<IndexModel.AttachmentViewModel>();
            UploadInput = new IndexModel.UploadAttachmentInput();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync(CancellationToken cancellationToken)
    {
        if (Input.Id.HasValue)
        {
            EditId = Input.Id;
        }

        if (!ModelState.IsValid)
        {
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            if (Input.Id.HasValue)
            {
                await LoadRecordAsync(Input.Id.Value, cancellationToken, overwriteInput: false);
            }
            return Page();
        }

        if (!Input.Id.HasValue)
        {
            ModelState.AddModelError(string.Empty, "The record could not be found.");
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            return Page();
        }

        if (Input.Type is null)
        {
            ModelState.AddModelError("Input.Type", "Select a type.");
        }

        if (Input.Status is null)
        {
            ModelState.AddModelError("Input.Status", "Select a status.");
        }

        var rowVersion = DecodeRowVersion(Input.RowVersion);
        if (rowVersion is null)
        {
            ModelState.AddModelError(string.Empty, "We could not verify your request. Please reload and try again.");
        }

        if (!ModelState.IsValid)
        {
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            if (Input.Id.HasValue)
            {
                await LoadRecordAsync(Input.Id.Value, cancellationToken, overwriteInput: false);
            }
            return Page();
        }

        try
        {
            var entity = ToEntity(Input);
            entity.RowVersion = rowVersion!;

            var updated = await _writeService.UpdateAsync(entity, cancellationToken);
            if (updated is null)
            {
                TempData["ToastError"] = "The selected patent record could not be found.";
                return RedirectToPage("./Manage", new { Query });
            }

            TempData["ToastMessage"] = "Patent record updated.";
            return RedirectToPage("./Manage", new { Query });
        }
        catch (InvalidOperationException ex)
        {
            if (!TryAddInputValidationErrors(ex.Message))
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            if (Input.Id.HasValue)
            {
                await LoadRecordAsync(Input.Id.Value, cancellationToken, overwriteInput: false);
            }
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, string rowVersion, CancellationToken cancellationToken)
    {
        var decoded = DecodeRowVersion(rowVersion);
        if (decoded is null)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage("./Manage", new { Query });
        }

        try
        {
            var deleted = await _writeService.DeleteAsync(id, decoded, cancellationToken);
            if (!deleted)
            {
                TempData["ToastError"] = "The selected patent record could not be found.";
            }
            else
            {
                TempData["ToastMessage"] = "Patent record deleted.";
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage("./Manage", new { Query });
    }

    public async Task<IActionResult> OnPostAttachAsync(CancellationToken cancellationToken)
    {
        if (UploadInput.RecordId.HasValue)
        {
            EditId = UploadInput.RecordId;
        }

        if (!UploadInput.RecordId.HasValue)
        {
            ModelState.AddModelError(string.Empty, "Select a record before uploading attachments.");
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            Attachments = Array.Empty<IndexModel.AttachmentViewModel>();
            UploadInput = new IndexModel.UploadAttachmentInput();
            return Page();
        }

        if (UploadInput.File is null || UploadInput.File.Length == 0)
        {
            ModelState.AddModelError(nameof(UploadInput.File), "Choose a file to upload.");
            await LoadRecordAsync(UploadInput.RecordId.Value, cancellationToken, overwriteInput: true);
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            return Page();
        }

        var userId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        try
        {
            await using var stream = UploadInput.File.OpenReadStream();
            await _writeService.AddAttachmentAsync(
                UploadInput.RecordId.Value,
                stream,
                UploadInput.File.FileName,
                UploadInput.File.ContentType,
                userId,
                cancellationToken);

            TempData["ToastMessage"] = "Attachment uploaded.";
            return RedirectToPage("./Manage", new { Query, editId = UploadInput.RecordId.Value });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadRecordAsync(UploadInput.RecordId.Value, cancellationToken, overwriteInput: true);
            await LoadSelectListsAsync(cancellationToken);
            await LoadRecordsAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveAttachmentAsync(CancellationToken cancellationToken)
    {
        if (RemoveAttachment.RecordId.HasValue)
        {
            EditId = RemoveAttachment.RecordId;
        }

        if (!RemoveAttachment.RecordId.HasValue)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage("./Manage", new { Query });
        }

        var rowVersion = DecodeRowVersion(RemoveAttachment.RowVersion);
        if (rowVersion is null)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage("./Manage", new { Query, editId = RemoveAttachment.RecordId.Value });
        }

        try
        {
            var deleted = await _writeService.DeleteAttachmentAsync(RemoveAttachment.AttachmentId, rowVersion, cancellationToken);
            if (deleted)
            {
                TempData["ToastMessage"] = "Attachment removed.";
            }
            else
            {
                TempData["ToastError"] = "Attachment not found.";
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        return RedirectToPage("./Manage", new { Query, editId = RemoveAttachment.RecordId.Value });
    }

    private async Task LoadRecordsAsync(CancellationToken cancellationToken)
    {
        var query = _db.IprRecords
            .AsNoTracking()
            .Include(r => r.Project)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var trimmed = Query.Trim();
            query = query.Where(r =>
                EF.Functions.ILike(r.IprFilingNumber, $"%{trimmed}%") ||
                (r.Title != null && EF.Functions.ILike(r.Title, $"%{trimmed}%")));
        }

        var items = await query
            .OrderByDescending(r => r.FiledAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(r => r.Id)
            .Select(r => new RecordRow(
                r.Id,
                r.IprFilingNumber,
                r.Title,
                GetTypeLabel(r.Type),
                GetStatusLabel(r.Status),
                r.Project != null ? r.Project.Name : null,
                Convert.ToBase64String(r.RowVersion)))
            .ToListAsync(cancellationToken);

        Records = items;
    }

    private async Task LoadSelectListsAsync(CancellationToken cancellationToken)
    {
        var supportedTypes = new[] { IprType.Patent, IprType.Copyright };

        TypeFormOptions = supportedTypes
            .Select(type => new SelectListItem(GetTypeLabel(type), type.ToString())
            {
                Selected = Input.Type.HasValue && Input.Type.Value == type
            })
            .ToList();

        StatusFormOptions = Enum.GetValues<IprStatus>()
            .Select(status => new SelectListItem(GetStatusLabel(status), status.ToString())
            {
                Selected = Input.Status.HasValue && Input.Status.Value == status
            })
            .ToList();

        var projectItems = await _db.Projects.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString(CultureInfo.InvariantCulture))
            {
                Selected = Input.ProjectId.HasValue && Input.ProjectId.Value == p.Id
            })
            .ToListAsync(cancellationToken);

        var options = new List<SelectListItem>
        {
            new("No project", string.Empty)
            {
                Selected = !Input.ProjectId.HasValue
            }
        };

        options.AddRange(projectItems);
        ProjectOptions = options;
    }

    private static IndexModel.RecordInput CreateDefaultInput()
    {
        return new IndexModel.RecordInput
        {
            Type = IprType.Patent,
            Status = IprStatus.FilingUnderProcess
        };
    }

    private static IndexModel.RecordInput MapToInput(IprRecord record)
    {
        return new IndexModel.RecordInput
        {
            Id = record.Id,
            FilingNumber = record.IprFilingNumber,
            Title = record.Title,
            Notes = record.Notes,
            Type = record.Type,
            Status = record.Status,
            FiledBy = record.FiledBy,
            FiledOn = record.FiledAtUtc.HasValue
                ? DateOnly.FromDateTime(record.FiledAtUtc.Value.UtcDateTime)
                : null,
            GrantedOn = record.GrantedAtUtc.HasValue
                ? DateOnly.FromDateTime(record.GrantedAtUtc.Value.UtcDateTime)
                : null,
            ProjectId = record.ProjectId,
            RowVersion = Convert.ToBase64String(record.RowVersion)
        };
    }

    private async Task<IprRecord?> LoadRecordAsync(int id, CancellationToken cancellationToken, bool overwriteInput)
    {
        var record = await _readService.GetAsync(id, cancellationToken);
        if (record is null)
        {
            Attachments = Array.Empty<IndexModel.AttachmentViewModel>();
            UploadInput = new IndexModel.UploadAttachmentInput();
            return null;
        }

        if (overwriteInput || !Input.Id.HasValue || Input.Id.Value != record.Id)
        {
            Input = MapToInput(record);
        }
        else if (string.IsNullOrWhiteSpace(Input.RowVersion))
        {
            Input.RowVersion = Convert.ToBase64String(record.RowVersion);
        }

        SetAttachmentState(record);

        return record;
    }

    private void SetAttachmentState(IprRecord record)
    {
        UploadInput = new IndexModel.UploadAttachmentInput
        {
            RecordId = record.Id
        };

        Attachments = record.Attachments
            .Where(a => !a.IsArchived)
            .OrderByDescending(a => a.UploadedAtUtc)
            .Select(a => new IndexModel.AttachmentViewModel(
                a.Id,
                a.OriginalFileName,
                a.FileSize,
                FormatUserDisplay(a.UploadedByUser, a.UploadedByUserId),
                a.UploadedAtUtc,
                Convert.ToBase64String(a.RowVersion)))
            .ToList();
    }

    private static IprRecord ToEntity(IndexModel.RecordInput input)
    {
        return new IprRecord
        {
            Id = input.Id ?? 0,
            IprFilingNumber = input.FilingNumber?.Trim() ?? string.Empty,
            Title = string.IsNullOrWhiteSpace(input.Title) ? null : input.Title.Trim(),
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            Type = input.Type ?? IprType.Patent,
            Status = input.Status ?? IprStatus.FilingUnderProcess,
            FiledBy = string.IsNullOrWhiteSpace(input.FiledBy) ? null : input.FiledBy.Trim(),
            FiledAtUtc = input.FiledOn.HasValue
                ? new DateTimeOffset(input.FiledOn.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                : null,
            GrantedAtUtc = input.GrantedOn.HasValue
                ? new DateTimeOffset(input.GrantedOn.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                : null,
            ProjectId = input.ProjectId
        };
    }

    public string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double size = bytes / 1024d;
        string[] units = { "KB", "MB", "GB", "TB", "PB" };
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", size, units[unitIndex]);
    }

    public string FormatAttachmentTimestamp(DateTimeOffset value)
    {
        var istZone = TimeZoneHelper.GetIst();
        var converted = TimeZoneInfo.ConvertTimeFromUtc(value.UtcDateTime, istZone);
        return converted.ToString("dd MMM yyyy 'at' HH:mm 'IST'", CultureInfo.InvariantCulture);
    }

    private static string GetTypeLabel(IprType type)
        => type switch
        {
            IprType.Patent => "Patent",
            IprType.Copyright => "Copyright",
            _ => type.ToString()
        };

    private static string GetStatusLabel(IprStatus status)
        => status switch
        {
            IprStatus.FilingUnderProcess => "Filing under process",
            IprStatus.Filed => "Filed",
            IprStatus.Granted => "Granted",
            IprStatus.Rejected => "Rejected",
            IprStatus.Withdrawn => "Withdrawn",
            _ => status.ToString()
        };

    private bool TryAddInputValidationErrors(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (!ValidationErrorFieldMap.TryGetValue(message, out var fields))
        {
            return false;
        }

        foreach (var field in fields)
        {
            ModelState.AddModelError($"{nameof(Input)}.{field}", message);
        }

        return true;
    }

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        return user?.Id;
    }

    private static string FormatUserDisplay(ApplicationUser? user, string fallback)
    {
        if (user is { FullName: { Length: > 0 } fullName })
        {
            return fullName;
        }

        if (user is { UserName: { Length: > 0 } userName })
        {
            return userName;
        }

        return fallback;
    }

    private static byte[]? DecodeRowVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public sealed record RecordRow(
        int Id,
        string FilingNumber,
        string? Title,
        string TypeLabel,
        string StatusLabel,
        string? ProjectName,
        string RowVersion);
}
