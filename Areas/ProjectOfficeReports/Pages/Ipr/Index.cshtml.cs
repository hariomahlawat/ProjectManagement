using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.ViewModels;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;
using ProjectManagement.Utilities;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

[Authorize(Policy = Policies.Ipr.View)]
public sealed class IndexModel : PageModel
{
    private static readonly IReadOnlyDictionary<IprValidationCode, string[]> ValidationErrorFieldMap =
        new Dictionary<IprValidationCode, string[]>
        {
            [IprValidationCode.FilingNumberRequired] = new[] { nameof(RecordInput.FilingNumber) },
            [IprValidationCode.TitleRequired] = new[] { nameof(RecordInput.Title) },
            [IprValidationCode.DuplicateFilingNumber] = new[] { nameof(RecordInput.FilingNumber) },
            [IprValidationCode.FiledDateRequired] = new[] { nameof(RecordInput.FiledOn) },
            [IprValidationCode.FiledDateInFuture] = new[] { nameof(RecordInput.FiledOn) },
            [IprValidationCode.GrantDateRequired] = new[] { nameof(RecordInput.GrantedOn) },
            [IprValidationCode.GrantDateInFuture] = new[] { nameof(RecordInput.GrantedOn) },
            [IprValidationCode.GrantDateWithoutFilingDate] = new[] { nameof(RecordInput.FiledOn), nameof(RecordInput.GrantedOn) },
            [IprValidationCode.GrantDateBeforeFilingDate] = new[] { nameof(RecordInput.FiledOn), nameof(RecordInput.GrantedOn) }
        };

    private readonly ApplicationDbContext _db;
    private readonly IIprReadService _readService;
    private readonly IIprWriteService _writeService;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIprExportService _exportService;
    private readonly IprAttachmentOptions _attachmentOptions;

    private string? _query;
    private string? _mode;

    public IndexModel(
        ApplicationDbContext db,
        IIprReadService readService,
        IIprWriteService writeService,
        IAuthorizationService authorizationService,
        UserManager<ApplicationUser> userManager,
        IIprExportService exportService,
        IOptions<IprAttachmentOptions> attachmentOptions)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _writeService = writeService ?? throw new ArgumentNullException(nameof(writeService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _attachmentOptions = attachmentOptions?.Value ?? throw new ArgumentNullException(nameof(attachmentOptions));
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

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "records";

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

    private static readonly TimeZoneInfo IstTimeZone = TimeZoneHelper.GetIst();

    public IReadOnlyList<IprRecordRowViewModel> Records { get; private set; } = Array.Empty<IprRecordRowViewModel>();

    public IprKpis Kpis { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public sealed class YearlyRow
    {
        public int Year { get; set; }
        public int Filed { get; set; }
        public int Granted { get; set; }
    }

    public List<YearlyRow> YearlyStats { get; set; } = new();

    public sealed record TypeBreakdownRow(string Type, int Filed, int Granted, int AwaitingGrant);

    public sealed record ProjectIprLinkRow(
        int Id,
        int? ProjectId,
        string ProjectName,
        string Title,
        string Type,
        string Position,
        DateTime? FiledOn,
        DateTime? GrantedOn,
        int ProjectIprCount);

    public sealed record AwaitingGrantRow(
        int Id,
        int? ProjectId,
        string ProjectName,
        string Title,
        string Type,
        DateTime? FiledOn,
        int WaitingDays);

    public IReadOnlyList<TypeBreakdownRow> TypeBreakdown { get; private set; } = Array.Empty<TypeBreakdownRow>();

    public IReadOnlyList<ProjectIprLinkRow> ProjectIprLinks { get; private set; } = Array.Empty<ProjectIprLinkRow>();

    public IReadOnlyList<AwaitingGrantRow> OldestAwaitingGrant { get; private set; } = Array.Empty<AwaitingGrantRow>();

    public int ProjectsWithIpr { get; private set; }

    public IReadOnlyList<AttachmentViewModel> Attachments { get; private set; } = Array.Empty<AttachmentViewModel>();

    public string? EditingProjectName { get; private set; }

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    public bool CanEdit { get; private set; }

    public string AttachmentUploadHint
        => $"PDF only · Maximum {FormatFileSize(_attachmentOptions.MaxFileSizeBytes)}";

    public bool HasAnyFilter
        => !string.IsNullOrWhiteSpace(Query)
            || Types.Count > 0
            || Statuses.Count > 0
            || ProjectId.HasValue
            || Year.HasValue;

    public IReadOnlyList<string> ActiveFilterChips { get; private set; } = Array.Empty<string>();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        await EvaluateAuthorizationAsync();
        NormalizeMode();
        await LoadPageAsync(cancellationToken, loadRecordInput: true);
        return Page();
    }

    public async Task<IActionResult> OnGetSummaryAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        var filter = BuildFilter();
        var kpis = await _readService.GetKpisAsync(filter, cancellationToken);
        return new JsonResult(new { filed = kpis.Total, granted = kpis.Granted, awaitingGrant = kpis.Filed });
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        var filter = BuildFilter();
        var file = await _exportService.ExportAsync(filter, cancellationToken);

        return File(file.Content, file.ContentType, file.FileName);
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
            ModelState.AddModelError($"{nameof(Input)}.{nameof(RecordInput.Type)}", "Select a type.");
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
        }

        if (Input.Status is null)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(RecordInput.Status)}", "Select a status.");
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
        catch (IprValidationException ex)
        {
            AddInputValidationErrors(ex);
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
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
        catch (IprValidationException ex)
        {
            AddInputValidationErrors(ex);
            await LoadPageAsync(cancellationToken, loadRecordInput: false);
            return Page();
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
            ModelState.AddModelError($"{nameof(UploadInput)}.{nameof(UploadAttachmentInput.File)}", "Choose a file to upload.");
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

        if (!string.IsNullOrWhiteSpace(Tab))
        {
            values["tab"] = Tab;
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

    private IprRecordRowViewModel CreateRowViewModel(IprListRowDto dto)
    {
        if (dto is null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        var title = string.IsNullOrWhiteSpace(dto.Title) ? "Untitled record" : dto.Title!;
        var project = string.IsNullOrWhiteSpace(dto.ProjectName) ? "Unassigned project" : dto.ProjectName!;
        var applicationNumber = string.IsNullOrWhiteSpace(dto.FilingNumber) ? "—" : dto.FilingNumber;
        var attachments = dto.Attachments
            .Select(CreateAttachmentViewModel)
            .ToList();

        return new IprRecordRowViewModel(
            dto.Id,
            dto.ProjectId,
            title,
            project,
            GetTypeLabel(dto.Type),
            applicationNumber,
            GetStatusLabel(dto.Status),
            GetStatusChipClass(dto.Status),
            string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes,
            ConvertToIstDate(dto.FiledAtUtc),
            ConvertToIstDate(dto.GrantedAtUtc),
            attachments,
            dto.AttachmentCount);
    }

    private IprRecordAttachmentViewModel CreateAttachmentViewModel(IprListAttachmentDto attachment)
    {
        var uploadedAt = FormatAttachmentTimestamp(attachment.UploadedAtUtc);
        return new IprRecordAttachmentViewModel(
            attachment.Id,
            attachment.FileName,
            FormatFileSize(attachment.FileSize),
            attachment.UploadedBy,
            uploadedAt);
    }

    private static DateTime? ConvertToIstDate(DateTimeOffset? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var converted = TimeZoneInfo.ConvertTimeFromUtc(value.Value.UtcDateTime, IstTimeZone);
        return converted;
    }

    public string FormatAttachmentTimestamp(DateTimeOffset value)
    {
        var converted = TimeZoneInfo.ConvertTimeFromUtc(value.UtcDateTime, IstTimeZone);
        return converted.ToString("dd MMM yyyy 'at' hh:mm tt", CultureInfo.InvariantCulture);
    }

    private static string GetStatusChipClass(IprStatus status)
        => status switch
        {
            IprStatus.Granted => "text-success border-success",
            IprStatus.Rejected => "text-danger border-danger",
            IprStatus.FilingUnderProcess => "text-primary border-primary",
            IprStatus.Withdrawn => "border-secondary text-secondary",
            _ => string.Empty
        };

    private IReadOnlyList<string> BuildActiveFilterChips()
    {
        if (!HasAnyFilter)
        {
            return Array.Empty<string>();
        }

        var chips = new List<string>();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            chips.Add($"Search: \"{Query}\"");
        }

        foreach (var type in Types)
        {
            chips.Add($"Type: {GetTypeLabel(type)}");
        }

        foreach (var status in Statuses)
        {
            chips.Add($"Status: {GetStatusLabel(status)}");
        }

        if (ProjectId.HasValue)
        {
            var projectValue = ProjectId.Value.ToString(CultureInfo.InvariantCulture);
            var projectLabel = ProjectOptions.FirstOrDefault(option => option.Value == projectValue)?.Text;
            if (!string.IsNullOrWhiteSpace(projectLabel) && !string.Equals(projectLabel, "All projects", StringComparison.Ordinal))
            {
                chips.Add($"Project: {projectLabel}");
            }
        }

        if (Year.HasValue)
        {
            chips.Add($"Filed year: {Year.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return chips;
    }

    private async Task LoadPageAsync(CancellationToken cancellationToken, bool loadRecordInput)
    {
        NormalizePaging();
        await LoadRegisterOverviewAsync(cancellationToken);

        switch (Tab)
        {
            case "project":
                await LoadProjectLinksAsync(cancellationToken);
                break;
            case "analytics":
                await LoadAnalyticsAsync(cancellationToken);
                break;
            default:
                await LoadRecordsAsync(cancellationToken);
                break;
        }

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
                Input = new RecordInput
                {
                    Type = IprType.Patent,
                    Status = IprStatus.Filed
                };
            }
        }

        await PopulateSelectListsAsync(cancellationToken);
        ActiveFilterChips = BuildActiveFilterChips();
    }

    private async Task LoadRecordsAsync(CancellationToken cancellationToken)
    {
        var result = await _readService.SearchAsync(BuildFilter(), cancellationToken);
        Records = result.Items.Select(CreateRowViewModel).ToList();
        TotalCount = result.Total;
        PageNumber = result.Page;
        PageSize = result.PageSize;
        TotalPages = PageSize > 0
            ? (int)Math.Ceiling(result.Total / (double)PageSize)
            : 0;
    }

    private async Task LoadRegisterOverviewAsync(CancellationToken cancellationToken)
    {
        var query = BuildPublicRegisterQuery();

        var grouped = await query
            .GroupBy(record => new
            {
                record.Type,
                Status = record.Status == IprStatus.FilingUnderProcess
                    ? IprStatus.Filed
                    : record.Status
            })
            .Select(group => new
            {
                group.Key.Type,
                group.Key.Status,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var granted = grouped
            .Where(item => item.Status == IprStatus.Granted)
            .Sum(item => item.Count);
        var awaiting = grouped
            .Where(item => item.Status == IprStatus.Filed)
            .Sum(item => item.Count);
        Kpis = new IprKpis(awaiting + granted, 0, awaiting, granted, 0, 0);

        ProjectsWithIpr = await query
            .Where(record => record.ProjectId.HasValue)
            .Select(record => record.ProjectId!.Value)
            .Distinct()
            .CountAsync(cancellationToken);

        TypeBreakdown = new[] { IprType.Patent, IprType.Copyright }
            .Select(type =>
            {
                var filed = grouped
                    .Where(item => item.Type == type)
                    .Sum(item => item.Count);
                var grantedByType = grouped
                    .Where(item => item.Type == type && item.Status == IprStatus.Granted)
                    .Sum(item => item.Count);

                return new TypeBreakdownRow(
                    GetTypeLabel(type),
                    filed,
                    grantedByType,
                    filed - grantedByType);
            })
            .ToList();
    }

    private async Task LoadProjectLinksAsync(CancellationToken cancellationToken)
    {
        var snapshot = await BuildPublicRegisterQuery()
            .Select(record => new
            {
                record.Id,
                record.Title,
                record.Type,
                Status = record.Status == IprStatus.FilingUnderProcess
                    ? IprStatus.Filed
                    : record.Status,
                record.FiledAtUtc,
                record.GrantedAtUtc,
                record.ProjectId,
                ProjectName = record.Project != null
                    ? record.Project.Name
                    : "Unassigned project"
            })
            .ToListAsync(cancellationToken);

        var projectCounts = snapshot
            .GroupBy(item => item.ProjectId ?? 0)
            .ToDictionary(group => group.Key, group => group.Count());

        ProjectIprLinks = snapshot
            .OrderBy(item => item.ProjectName)
            .ThenBy(item => item.Type)
            .ThenBy(item => item.Title)
            .Select(item => new ProjectIprLinkRow(
                item.Id,
                item.ProjectId,
                item.ProjectName,
                string.IsNullOrWhiteSpace(item.Title) ? "Untitled IPR record" : item.Title!,
                GetTypeLabel(item.Type),
                item.Status == IprStatus.Granted ? "Granted" : "Awaiting grant",
                ConvertToIstDate(item.FiledAtUtc),
                ConvertToIstDate(item.GrantedAtUtc),
                projectCounts.TryGetValue(item.ProjectId ?? 0, out var count) ? count : 1))
            .ToList();
    }

    private async Task LoadAnalyticsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await BuildPublicRegisterQuery()
            .Select(record => new
            {
                record.Id,
                record.Title,
                record.Type,
                Status = record.Status == IprStatus.FilingUnderProcess
                    ? IprStatus.Filed
                    : record.Status,
                record.FiledAtUtc,
                record.GrantedAtUtc,
                FiledYear = record.FiledAtUtc.HasValue
                    ? (int?)record.FiledAtUtc.Value.Year
                    : null,
                GrantedYear = record.GrantedAtUtc.HasValue
                    ? (int?)record.GrantedAtUtc.Value.Year
                    : null,
                record.ProjectId,
                ProjectName = record.Project != null
                    ? record.Project.Name
                    : "Unassigned project"
            })
            .ToListAsync(cancellationToken);

        var todayIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IstTimeZone).Date;
        OldestAwaitingGrant = snapshot
            .Where(item => item.Status == IprStatus.Filed)
            .OrderBy(item => item.FiledAtUtc ?? DateTimeOffset.MaxValue)
            .Take(5)
            .Select(item =>
            {
                var filedOn = ConvertToIstDate(item.FiledAtUtc);
                var waitingDays = filedOn.HasValue
                    ? Math.Max(0, (todayIst - filedOn.Value.Date).Days)
                    : 0;

                return new AwaitingGrantRow(
                    item.Id,
                    item.ProjectId,
                    item.ProjectName,
                    string.IsNullOrWhiteSpace(item.Title) ? "Untitled IPR record" : item.Title!,
                    GetTypeLabel(item.Type),
                    filedOn,
                    waitingDays);
            })
            .ToList();

        var years = snapshot
            .SelectMany(item => new[] { item.FiledYear, item.GrantedYear })
            .Where(year => year.HasValue)
            .Select(year => year!.Value)
            .Distinct()
            .OrderBy(year => year)
            .ToList();

        YearlyStats = years
            .Select(year => new YearlyRow
            {
                Year = year,
                Filed = snapshot.Count(item => item.FiledYear == year),
                Granted = snapshot.Count(item =>
                    item.Status == IprStatus.Granted &&
                    item.GrantedYear == year)
            })
            .ToList();
    }

    private IQueryable<IprRecord> BuildPublicRegisterQuery()
    {
        return _db.IprRecords
            .AsNoTracking()
            .Where(record =>
                record.Status == IprStatus.FilingUnderProcess ||
                record.Status == IprStatus.Filed ||
                record.Status == IprStatus.Granted);
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

        TypeFormOptions = supportedTypes
            .Select(type => new SelectListItem(GetTypeLabel(type), type.ToString())
            {
                Selected = Input.Type.HasValue && Input.Type.Value == type
            })
            .ToList();

        var supportedStatuses = new[] { IprStatus.Filed, IprStatus.Granted };

        StatusOptions = supportedStatuses
            .Select(status => new SelectListItem(GetStatusLabel(status), status.ToString())
            {
                Selected = Statuses.Contains(status)
            })
            .ToList();

        StatusFormOptions = supportedStatuses
            .Select(status => new SelectListItem(GetStatusLabel(status), status.ToString())
            {
                Selected = Input.Status.HasValue && Input.Status.Value == status
            })
            .ToList();

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
            new("10", "10") { Selected = PageSize == 10 },
            new("25", "25") { Selected = PageSize == 25 },
            new("50", "50") { Selected = PageSize == 50 }
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
        Statuses = Statuses
            .Select(status => status == IprStatus.FilingUnderProcess ? IprStatus.Filed : status)
            .Where(status => status is IprStatus.Filed or IprStatus.Granted)
            .Distinct()
            .ToList();

        Tab = Tab?.Trim().ToLowerInvariant() switch
        {
            "project" => "project",
            "analytics" => "analytics",
            _ => "records"
        };

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

        if (PageSize is not (10 or 25 or 50))
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
                Status = record.Status == IprStatus.FilingUnderProcess ? IprStatus.Filed : record.Status,
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
            Title = input.Title?.Trim(),
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            Type = input.Type ?? IprType.Patent,
            Status = input.Status == IprStatus.Granted ? IprStatus.Granted : IprStatus.Filed,
            FiledBy = string.IsNullOrWhiteSpace(input.FiledBy) ? null : input.FiledBy.Trim(),
            FiledAtUtc = input.FiledOn.HasValue
                ? new DateTimeOffset(input.FiledOn.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                : null,
            GrantedAtUtc = input.Status == IprStatus.Granted && input.GrantedOn.HasValue
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
            IprStatus.FilingUnderProcess => "Awaiting grant",
            IprStatus.Filed => "Awaiting grant",
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
        [Required]
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
        [Required]
        [DataType(DataType.Date)]
        public DateOnly? FiledOn { get; set; }

        [Display(Name = "Granted on")]
        [DataType(DataType.Date)]
        public DateOnly? GrantedOn { get; set; }

        [Display(Name = "Project")]
        public int? ProjectId { get; set; }

        public string? RowVersion { get; set; }
    }

    private void AddInputValidationErrors(IprValidationException exception)
    {
        if (!ValidationErrorFieldMap.TryGetValue(exception.Code, out var fields))
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return;
        }

        foreach (var field in fields)
        {
            ModelState.AddModelError($"{nameof(Input)}.{field}", exception.Message);
        }
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
