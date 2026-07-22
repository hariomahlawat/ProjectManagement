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
public sealed partial class IndexModel : PageModel
{
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
    public int PageSize { get; set; } = 15;

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

    public IReadOnlyList<ProjectPickerOption> ProjectPickerOptions { get; private set; } = Array.Empty<ProjectPickerOption>();

    public IReadOnlyList<SelectListItem> YearOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> PageSizeOptions { get; private set; } = Array.Empty<SelectListItem>();

    private static readonly TimeZoneInfo IstTimeZone = TimeZoneHelper.GetIst();

    public IReadOnlyList<IprRecordRowViewModel> Records { get; private set; } = Array.Empty<IprRecordRowViewModel>();

    public IprKpis Kpis { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public IReadOnlyList<TypeBreakdownRow> TypeBreakdown { get; private set; } = Array.Empty<TypeBreakdownRow>();

    public IReadOnlyList<ProjectIprGroup> ProjectIprGroups { get; private set; } = Array.Empty<ProjectIprGroup>();

    public IReadOnlyList<AttentionGroup> AttentionGroups { get; private set; } = Array.Empty<AttentionGroup>();

    public IReadOnlyList<AwaitingGrantRow> OldestAwaitingGrant { get; private set; } = Array.Empty<AwaitingGrantRow>();

    public IReadOnlyList<AgeBandRow> AwaitingAgeBands { get; private set; } = Array.Empty<AgeBandRow>();

    public AnalyticsSummaryModel AnalyticsSummary { get; private set; } = new(0, null, null, 0, 0, 0);

    public int ProjectsWithIpr { get; private set; }

    public int AttentionRecordCount { get; private set; }

    public int OverdueAttentionCount { get; private set; }

    public int UnassignedCount { get; private set; }

    public int MissingAttachmentCount { get; private set; }

    public int GrantRatePercent { get; private set; }

    public IReadOnlyList<AttachmentViewModel> Attachments { get; private set; } = Array.Empty<AttachmentViewModel>();

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

    public static string FormatAge(int days)
    {
        if (days <= 0)
        {
            return "<1d";
        }

        if (days < 30)
        {
            return $"{days}d";
        }

        if (days < 365)
        {
            return $"{Math.Max(1, days / 30)}m";
        }

        var years = days / 365;
        var remainingMonths = (days % 365) / 30;
        return remainingMonths == 0
            ? $"{years}y"
            : $"{years}y {remainingMonths}m";
    }

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

}
