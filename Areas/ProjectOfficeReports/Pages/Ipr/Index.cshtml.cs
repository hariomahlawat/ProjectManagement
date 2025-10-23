using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Utilities;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

[Authorize(Policy = Policies.Ipr.View)]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IIprReadService _readService;
    private readonly IIprWriteService _writeService;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserManager<ApplicationUser> _userManager;

    private string? _query;
    private string? _mode;

    public IndexModel(
        ApplicationDbContext db,
        IIprReadService readService,
        IIprWriteService writeService,
        IAuthorizationService authorizationService,
        UserManager<ApplicationUser> userManager)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _writeService = writeService ?? throw new ArgumentNullException(nameof(writeService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    [BindProperty(SupportsGet = true)]
    public string? Query
    {
        get => _query;
        set => _query = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [BindProperty(SupportsGet = true)]
    public List<IprType> Types { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public List<IprStatus> Statuses { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? ProjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(Name = "page", SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    [BindProperty(SupportsGet = true)]
    public string? Mode
    {
        get => _mode;
        set => _mode = value;
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty]
    public RecordInput Input { get; set; } = new();

    [BindProperty]
    public DeleteInput DeleteRequest { get; set; } = new();

    [BindProperty]
    public UploadAttachmentInput UploadInput { get; set; } = new();

    [BindProperty]
    public RemoveAttachmentInput RemoveAttachment { get; set; } = new();

    public IReadOnlyList<SelectListItem> TypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> TypeFormOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> StatusOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> StatusFormOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> ProjectOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> ProjectFormOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> YearOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> PageSizeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<IprListRowDto> Records { get; private set; } = Array.Empty<IprListRowDto>();

    public IprKpis Kpis { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public IReadOnlyList<AttachmentViewModel> Attachments { get; private set; } = Array.Empty<AttachmentViewModel>();

    public string? EditingProjectName { get; private set; }

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    public bool CanEdit { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        await EvaluateAuthorizationAsync();
        NormalizeMode();
        await LoadPageAsync(cancellationToken, loadRecordInput: true);
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        var filter = BuildFilter();
        var records = await _readService.GetExportAsync(filter, cancellationToken);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("IPR Records");

        var headers = new[]
        {
            "Ser",
            "Name of the product",
            "IPR filing no",
            "Status",
            "Filed by",
            "Filing date",
            "Grant date",
            "Project",
            "Remarks"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            var cell = worksheet.Cell(1, column + 1);
            cell.Value = headers[column];
            cell.Style.Font.Bold = true;
        }

        var istZone = TimeZoneHelper.GetIst();

        for (var index = 0; index < records.Count; index++)
        {
            var rowNumber = index + 2;
            var record = records[index];

            worksheet.Cell(rowNumber, 1).Value = index + 1;
            worksheet.Cell(rowNumber, 2).Value = record.Title ?? string.Empty;
            worksheet.Cell(rowNumber, 3).Value = record.FilingNumber;
            worksheet.Cell(rowNumber, 4).Value = GetStatusLabel(record.Status);
            worksheet.Cell(rowNumber, 5).Value = record.FiledBy ?? string.Empty;

            if (record.FiledAtUtc.HasValue)
            {
                var filedAtIst = TimeZoneInfo.ConvertTimeFromUtc(record.FiledAtUtc.Value.UtcDateTime, istZone);
                var filedDate = filedAtIst.Date;
                var filedCell = worksheet.Cell(rowNumber, 6);
                filedCell.Value = filedDate;
                filedCell.Style.DateFormat.Format = "dd-mmm-yyyy";
            }

            if (record.GrantedAtUtc.HasValue)
            {
                var grantedAtIst = TimeZoneInfo.ConvertTimeFromUtc(record.GrantedAtUtc.Value.UtcDateTime, istZone);
                var grantedCell = worksheet.Cell(rowNumber, 7);
                grantedCell.Value = grantedAtIst.Date;
                grantedCell.Style.DateFormat.Format = "dd-mmm-yyyy";
            }

            worksheet.Cell(rowNumber, 8).Value = record.ProjectName ?? string.Empty;
            worksheet.Cell(rowNumber, 9).Value = record.Remarks ?? string.Empty;
        }

        worksheet.Column(3).Style.NumberFormat.Format = "@";
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"ipr-records-{timestamp}.xlsx";

        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        Mode = "create";

        var authResult = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        await EvaluateAuthorizationAsync();
        NormalizeFilters();

        if (!ModelState.IsValid)
        {
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        if (Input.Type is null)
        {
            ModelState.AddModelError(nameof(Input.Type), "Select a type.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        if (Input.Status is null)
        {
            ModelState.AddModelError(nameof(Input.Status), "Select a status.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        try
        {
            var entity = ToEntity(Input);
            var created = await _writeService.CreateAsync(entity, cancellationToken);
            TempData["ToastMessage"] = "IPR record created.";
            return RedirectToPage(null, GetRouteValues(new { mode = "edit", id = created.Id }, includePage: true, includeModeAndId: false));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostEditAsync(CancellationToken cancellationToken)
    {
        Mode = "edit";
        if (Input.Id.HasValue)
        {
            Id = Input.Id;
        }

        var authResult = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        await EvaluateAuthorizationAsync();
        NormalizeFilters();

        if (!ModelState.IsValid)
        {
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        if (!Input.Id.HasValue)
        {
            ModelState.AddModelError(string.Empty, "The record could not be found.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        var rowVersion = DecodeRowVersion(Input.RowVersion);
        if (rowVersion is null)
        {
            ModelState.AddModelError(string.Empty, "We could not verify your request. Please reload and try again.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        try
        {
            var entity = ToEntity(Input);
            entity.RowVersion = rowVersion;
            var updated = await _writeService.UpdateAsync(entity, cancellationToken);
            if (updated is null)
            {
                ModelState.AddModelError(string.Empty, "The record could not be found.");
                await LoadPageAsync(cancellationToken, loadRecordInput: false);
                return Page();
            }

            TempData["ToastMessage"] = "IPR record updated.";
            return RedirectToPage(null, GetRouteValues(new { mode = "edit", id = updated.Id }, includePage: true, includeModeAndId: false));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var authResult = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        NormalizeFilters();
        await EvaluateAuthorizationAsync();

        var rowVersion = DecodeRowVersion(DeleteRequest.RowVersion);
        if (rowVersion is null)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage(null, GetRouteValues(includePage: true, includeModeAndId: false));
        }

        try
        {
            var deleted = await _writeService.DeleteAsync(DeleteRequest.Id, rowVersion, cancellationToken);
            if (deleted)
            {
                TempData["ToastMessage"] = "IPR record deleted.";
            }
            else
            {
                TempData["ToastError"] = "The record could not be found.";
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData["ToastError"] = ex.Message;
        }

        Mode = null;
        Id = null;
        return RedirectToPage(null, GetRouteValues(includePage: true, includeModeAndId: false));
    }

    public async Task<IActionResult> OnPostAttachAsync(CancellationToken cancellationToken)
    {
        Mode = "edit";
        if (UploadInput.RecordId.HasValue)
        {
            Id = UploadInput.RecordId;
        }

        var authResult = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        await EvaluateAuthorizationAsync();
        NormalizeFilters();

        if (!UploadInput.RecordId.HasValue)
        {
            ModelState.AddModelError(string.Empty, "Select a record before uploading attachments.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        if (UploadInput.File is null || UploadInput.File.Length == 0)
        {
            ModelState.AddModelError(nameof(UploadInput.File), "Choose a file to upload.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
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
            return RedirectToPage(null, GetRouteValues(new { mode = "edit", id = UploadInput.RecordId.Value }, includePage: true, includeModeAndId: false));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveAttachmentAsync(CancellationToken cancellationToken)
    {
        Mode = "edit";
        if (RemoveAttachment.RecordId.HasValue)
        {
            Id = RemoveAttachment.RecordId;
        }

        var authResult = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        await EvaluateAuthorizationAsync();
        NormalizeFilters();

        if (!RemoveAttachment.RecordId.HasValue)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage(null, GetRouteValues(includePage: true, includeModeAndId: false));
        }

        var rowVersion = DecodeRowVersion(RemoveAttachment.RowVersion);
        if (rowVersion is null)
        {
            TempData["ToastError"] = "We could not verify your request. Please reload and try again.";
            return RedirectToPage(null, GetRouteValues(new { mode = "edit", id = RemoveAttachment.RecordId.Value }, includePage: true, includeModeAndId: false));
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

        return RedirectToPage(null, GetRouteValues(new { mode = "edit", id = RemoveAttachment.RecordId.Value }, includePage: true, includeModeAndId: false));
    }

    public RouteValueDictionary GetRouteValues(object? additionalValues = null, bool includePage = true, bool includeModeAndId = true)
    {
        var values = new RouteValueDictionary();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            values["query"] = Query;
        }

        if (Types.Count > 0)
        {
            values["types"] = Types.Select(x => x.ToString()).ToArray();
        }

        if (Statuses.Count > 0)
        {
            values["statuses"] = Statuses.Select(x => x.ToString()).ToArray();
        }

        if (ProjectId.HasValue)
        {
            values["projectId"] = ProjectId.Value;
        }

        if (Year.HasValue)
        {
            values["year"] = Year.Value;
        }

        if (includePage)
        {
            values["page"] = PageNumber;
            values["pageSize"] = PageSize;
        }

        if (includeModeAndId)
        {
            if (!string.IsNullOrEmpty(Mode))
            {
                values["mode"] = Mode;
            }

            if (Id.HasValue)
            {
                values["id"] = Id.Value;
            }
        }

        if (additionalValues is not null)
        {
            foreach (var kvp in new RouteValueDictionary(additionalValues))
            {
                values[kvp.Key] = kvp.Value;
            }
        }

        return values;
    }

    public IDictionary<string, string?> GetRouteValuesForLinks(
        object? additionalValues = null,
        bool includePage = true,
        bool includeModeAndId = true)
    {
        var values = GetRouteValues(additionalValues, includePage, includeModeAndId);
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in values)
        {
            switch (value)
            {
                case null:
                    result[key] = null;
                    break;
                case string str:
                    result[key] = str;
                    break;
                case string[] array:
                    AddIndexedValues(result, key, array);
                    break;
                case IEnumerable<string> enumerable:
                    AddIndexedValues(result, key, enumerable);
                    break;
                default:
                    result[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
                    break;
            }
        }

        return result;
    }

    private static void AddIndexedValues(IDictionary<string, string?> destination, string key, IEnumerable<string> values)
    {
        var index = 0;
        foreach (var item in values)
        {
            destination[$"{key}[{index}]"] = item;
            index++;
        }

        if (index == 0)
        {
            return;
        }
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

    private async Task LoadPageAsync(CancellationToken cancellationToken, bool loadRecordInput)
    {
        NormalizePaging();

        var filter = BuildFilter();
        var result = await _readService.SearchAsync(filter, cancellationToken);
        Records = result.Items;
        TotalCount = result.Total;
        PageNumber = result.Page;
        PageSize = result.PageSize;
        TotalPages = PageSize > 0 ? (int)Math.Ceiling(result.Total / (double)PageSize) : 0;

        Kpis = await _readService.GetKpisAsync(filter, cancellationToken);

        if (string.Equals(Mode, "edit", StringComparison.OrdinalIgnoreCase) && Id.HasValue && CanEdit)
        {
            var record = await LoadRecordAsync(Id.Value, cancellationToken, loadRecordInput);
            if (record is null)
            {
                TempData["ToastError"] = "The selected IPR record could not be found.";
                Mode = null;
                Id = null;
                Attachments = Array.Empty<AttachmentViewModel>();
            }
        }
        else
        {
            Attachments = Array.Empty<AttachmentViewModel>();
            if (string.Equals(Mode, "create", StringComparison.OrdinalIgnoreCase))
            {
                Input = new RecordInput();
            }
        }

        await PopulateSelectListsAsync(cancellationToken);
    }

    private async Task PopulateSelectListsAsync(CancellationToken cancellationToken)
    {
        var supportedTypes = new[] { IprType.Patent, IprType.Copyright };

        TypeOptions = supportedTypes
            .Select(type => new SelectListItem(GetTypeLabel(type), type.ToString())
            {
                Selected = Types.Contains(type)
            })
            .ToList();

        var typeFormOptions = new List<SelectListItem>
        {
            new("Select type", string.Empty)
            {
                Selected = !Input.Type.HasValue
            }
        };

        foreach (var type in supportedTypes)
        {
            typeFormOptions.Add(new SelectListItem(GetTypeLabel(type), type.ToString())
            {
                Selected = Input.Type.HasValue && Input.Type.Value == type
            });
        }

        TypeFormOptions = typeFormOptions;

        StatusOptions = Enum.GetValues<IprStatus>()
            .Select(status => new SelectListItem(GetStatusLabel(status), status.ToString())
            {
                Selected = Statuses.Contains(status)
            })
            .ToList();

        var statusFormOptions = new List<SelectListItem>
        {
            new("Select status", string.Empty)
            {
                Selected = !Input.Status.HasValue
            }
        };

        foreach (var status in Enum.GetValues<IprStatus>())
        {
            statusFormOptions.Add(new SelectListItem(GetStatusLabel(status), status.ToString())
            {
                Selected = Input.Status.HasValue && Input.Status.Value == status
            });
        }

        StatusFormOptions = statusFormOptions;

        var projectItems = await _db.Projects.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new SelectListItem(p.Name, p.Id.ToString(CultureInfo.InvariantCulture))
            {
                Selected = ProjectId.HasValue && ProjectId.Value == p.Id
            })
            .ToListAsync(cancellationToken);

        var projectOptions = new List<SelectListItem>
        {
            new("All projects", string.Empty)
            {
                Selected = !ProjectId.HasValue
            }
        };
        projectOptions.AddRange(projectItems);
        ProjectOptions = projectOptions;

        var projectFormOptions = new List<SelectListItem>
        {
            new("No project", string.Empty)
            {
                Selected = !Input.ProjectId.HasValue
            }
        };

        foreach (var option in projectItems)
        {
            var isSelected = Input.ProjectId.HasValue &&
                option.Value == Input.ProjectId.Value.ToString(CultureInfo.InvariantCulture);

            projectFormOptions.Add(new SelectListItem(option.Text, option.Value)
            {
                Selected = isSelected
            });
        }

        ProjectFormOptions = projectFormOptions;

        var years = await _db.IprRecords.AsNoTracking()
            .Where(r => r.FiledAtUtc != null)
            .Select(r => r.FiledAtUtc!.Value.Year)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(cancellationToken);

        var yearOptions = new List<SelectListItem>
        {
            new("All years", string.Empty)
            {
                Selected = !Year.HasValue
            }
        };

        foreach (var year in years)
        {
            yearOptions.Add(new SelectListItem(year.ToString(CultureInfo.InvariantCulture), year.ToString(CultureInfo.InvariantCulture))
            {
                Selected = Year.HasValue && Year.Value == year
            });
        }

        YearOptions = yearOptions;

        PageSizeOptions = new List<SelectListItem>
        {
            new("25", "25") { Selected = PageSize == 25 },
            new("50", "50") { Selected = PageSize == 50 },
            new("100", "100") { Selected = PageSize == 100 }
        };
    }

    private async Task EvaluateAuthorizationAsync()
    {
        var result = await _authorizationService.AuthorizeAsync(User, null, Policies.Ipr.Edit);
        CanEdit = result.Succeeded;
    }

    private void NormalizeFilters()
    {
        Types = Types.Distinct().ToList();
        Statuses = Statuses.Distinct().ToList();

        if (ProjectId.HasValue && ProjectId.Value <= 0)
        {
            ProjectId = null;
        }

        if (Year.HasValue && Year.Value <= 0)
        {
            Year = null;
        }

        if (Id.HasValue && Id.Value <= 0)
        {
            Id = null;
        }
    }

    private void NormalizeMode()
    {
        if (string.Equals(_mode, "create", StringComparison.OrdinalIgnoreCase))
        {
            _mode = CanEdit ? "create" : null;
        }
        else if (string.Equals(_mode, "edit", StringComparison.OrdinalIgnoreCase))
        {
            _mode = CanEdit ? "edit" : null;
        }
        else
        {
            _mode = null;
        }
    }

    private void NormalizePaging()
    {
        if (PageNumber < 1)
        {
            PageNumber = 1;
        }

        if (PageSize is not (25 or 50 or 100))
        {
            PageSize = 25;
        }
    }

    private IprFilter BuildFilter()
    {
        var filter = new IprFilter
        {
            Query = Query,
            Types = Types.Count > 0 ? Types.ToArray() : null,
            Statuses = Statuses.Count > 0 ? Statuses.ToArray() : null,
            ProjectId = ProjectId,
            FiledFrom = Year.HasValue ? new DateOnly(Year.Value, 1, 1) : null,
            FiledTo = Year.HasValue ? new DateOnly(Year.Value, 12, 31) : null
        };

        filter.Page = PageNumber;
        filter.PageSize = PageSize;
        PageNumber = filter.Page;
        PageSize = filter.PageSize;

        return filter;
    }

    private async Task<IprRecord?> LoadRecordAsync(int id, CancellationToken cancellationToken, bool overwriteInput)
    {
        var record = await _readService.GetAsync(id, cancellationToken);
        if (record is null)
        {
            return null;
        }

        EditingProjectName = record.Project?.Name;

        var rowVersion = Convert.ToBase64String(record.RowVersion);

        if (overwriteInput || !Input.Id.HasValue || Input.Id.Value != record.Id)
        {
            Input = new RecordInput
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
                RowVersion = rowVersion
            };
        }
        else if (string.IsNullOrWhiteSpace(Input.RowVersion))
        {
            Input.RowVersion = rowVersion;
        }

        DeleteRequest = new DeleteInput
        {
            Id = record.Id,
            RowVersion = rowVersion
        };

        UploadInput = new UploadAttachmentInput
        {
            RecordId = record.Id
        };

        Attachments = record.Attachments
            .Where(a => !a.IsArchived)
            .OrderByDescending(a => a.UploadedAtUtc)
            .Select(a => new AttachmentViewModel(
                a.Id,
                a.OriginalFileName,
                a.FileSize,
                FormatUserDisplay(a.UploadedByUser, a.UploadedByUserId),
                a.UploadedAtUtc,
                Convert.ToBase64String(a.RowVersion)))
            .ToList();

        return record;
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

    private static IprRecord ToEntity(RecordInput input)
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

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        return user?.Id;
    }

    public sealed class RecordInput
    {
        [HiddenInput]
        public int? Id { get; set; }

        [Display(Name = "Filing number")]
        [Required]
        [StringLength(128)]
        public string? FilingNumber { get; set; }

        [Display(Name = "Title")]
        [StringLength(256)]
        public string? Title { get; set; }

        [Display(Name = "Notes")]
        [StringLength(2000)]
        public string? Notes { get; set; }

        [Display(Name = "Type")]
        [Required]
        public IprType? Type { get; set; }

        [Display(Name = "Status")]
        [Required]
        public IprStatus? Status { get; set; }

        [Display(Name = "Filed by")]
        [StringLength(128)]
        public string? FiledBy { get; set; }

        [Display(Name = "Filed on")]
        [DataType(DataType.Date)]
        public DateOnly? FiledOn { get; set; }

        [Display(Name = "Granted on")]
        [DataType(DataType.Date)]
        public DateOnly? GrantedOn { get; set; }

        [Display(Name = "Project")]
        public int? ProjectId { get; set; }

        public string? RowVersion { get; set; }
    }

    public sealed class DeleteInput
    {
        [HiddenInput]
        public int Id { get; set; }

        [HiddenInput]
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class UploadAttachmentInput
    {
        [HiddenInput]
        public int? RecordId { get; set; }

        [Display(Name = "Attachment")]
        public IFormFile? File { get; set; }
    }

    public sealed class RemoveAttachmentInput
    {
        [HiddenInput]
        public int AttachmentId { get; set; }

        [HiddenInput]
        public int? RecordId { get; set; }

        [HiddenInput]
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed record AttachmentViewModel(
        int Id,
        string FileName,
        long FileSize,
        string UploadedBy,
        DateTimeOffset UploadedAtUtc,
        string RowVersion);
}
