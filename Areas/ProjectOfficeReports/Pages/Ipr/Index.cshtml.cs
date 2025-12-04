using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.ViewModels;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Utilities;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

[Authorize(Policy = Policies.Ipr.View)]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IIprReadService _readService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IIprExportService _exportService;

    private string? _query;

    public IndexModel(
        ApplicationDbContext db,
        IIprReadService readService,
        IAuthorizationService authorizationService,
        IIprExportService exportService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
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

    public IReadOnlyList<SelectListItem> TypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> StatusOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> ProjectOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> YearOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> PageSizeOptions { get; private set; } = Array.Empty<SelectListItem>();

    private static readonly TimeZoneInfo IstTimeZone = TimeZoneHelper.GetIst();

    public IReadOnlyList<IprRecordRowViewModel> Records { get; private set; } = Array.Empty<IprRecordRowViewModel>();

    public IprKpis Kpis { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public sealed class PatentTotals
    {
        public int Filing { get; set; }

        public int Filed { get; set; }

        public int Granted { get; set; }

        public int Rejected { get; set; }

        public int Withdrawn { get; set; }

        public int Total => Filing + Filed + Granted + Rejected + Withdrawn;
    }

    public sealed class YearlyRow
    {
        public int Year { get; set; }

        public int Filing { get; set; }

        public int Filed { get; set; }

        public int Granted { get; set; }

        public int Rejected { get; set; }

        public int Withdrawn { get; set; }

        public int Total => Filing + Filed + Granted + Rejected + Withdrawn;
    }

    public List<YearlyRow> YearlyStats { get; set; } = new();

    public PatentTotals OverallTotals { get; set; } = new();

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    public bool CanEdit { get; private set; }

    public bool HasAnyFilter
        => !string.IsNullOrWhiteSpace(Query)
            || Types.Count > 0
            || Statuses.Count > 0
            || ProjectId.HasValue
            || Year.HasValue;

    public IReadOnlyList<string> ActiveFilterChips { get; private set; } = Array.Empty<string>();

    public sealed record IprSummaryDto(int Filing, int Filed, int Granted, int Rejected, int Withdrawn);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        await EvaluateAuthorizationAsync();
        await LoadPageAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetSummaryAsync(CancellationToken cancellationToken)
    {
        var query = _db.IprRecords.AsNoTracking();

        var dto = new IprSummaryDto(
            Filing: await query.CountAsync(r => r.Status == IprStatus.FilingUnderProcess, cancellationToken),
            Filed: await query.CountAsync(r => r.Status == IprStatus.Filed, cancellationToken),
            Granted: await query.CountAsync(r => r.Status == IprStatus.Granted, cancellationToken),
            Rejected: await query.CountAsync(r => r.Status == IprStatus.Rejected, cancellationToken),
            Withdrawn: await query.CountAsync(r => r.Status == IprStatus.Withdrawn, cancellationToken));

        return new JsonResult(dto);
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        var filter = BuildFilter();
        var file = await _exportService.ExportAsync(filter, cancellationToken);

        return File(file.Content, file.ContentType, file.FileName);
    }

    public RouteValueDictionary GetRouteValues(object? additionalValues = null, bool includePage = true)
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
        bool includePage = true)
    {
        var values = GetRouteValues(additionalValues, includePage);
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
            IprStatus.FilingUnderProcess => "border-warning text-warning",
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

    private async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        NormalizePaging();

        var filter = BuildFilter();
        var result = await _readService.SearchAsync(filter, cancellationToken);
        Records = result.Items.Select(CreateRowViewModel).ToList();
        TotalCount = result.Total;
        PageNumber = result.Page;
        PageSize = result.PageSize;
        TotalPages = PageSize > 0 ? (int)Math.Ceiling(result.Total / (double)PageSize) : 0;

        Kpis = await _readService.GetKpisAsync(filter, cancellationToken);

        await PopulateSelectListsAsync(cancellationToken);

        await LoadYearlyStatsAsync(cancellationToken);

        ActiveFilterChips = BuildActiveFilterChips();
    }

    // SECTION: Yearly stats aggregation
    private async Task LoadYearlyStatsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _db.IprRecords
            .AsNoTracking()
            .Select(r => new
            {
                FiledYear = r.FiledAtUtc.HasValue ? (int?)r.FiledAtUtc.Value.Year : null,
                GrantedYear = r.GrantedAtUtc.HasValue ? (int?)r.GrantedAtUtc.Value.Year : null,
                r.Status
            })
            .ToListAsync(cancellationToken);

        var currentYear = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IstTimeZone).Year;

        var yearlyRows = snapshot
            .Select(item =>
            {
                int year;
                switch (item.Status)
                {
                    case IprStatus.Granted:
                    case IprStatus.Rejected:
                    case IprStatus.Withdrawn:
                        year = item.GrantedYear
                            ?? item.FiledYear
                            ?? currentYear;
                        break;

                    case IprStatus.FilingUnderProcess:
                    case IprStatus.Filed:
                    default:
                        year = item.FiledYear
                            ?? item.GrantedYear
                            ?? currentYear;
                        break;
                }

                return new
                {
                    Year = year,
                    item.Status
                };
            })
            .GroupBy(x => x.Year)
            .OrderBy(group => group.Key)
            .Select(group => new YearlyRow
            {
                Year = group.Key,
                Filing = group.Count(x => x.Status == IprStatus.FilingUnderProcess),
                Filed = group.Count(x => x.Status == IprStatus.Filed),
                Granted = group.Count(x => x.Status == IprStatus.Granted),
                Rejected = group.Count(x => x.Status == IprStatus.Rejected),
                Withdrawn = group.Count(x => x.Status == IprStatus.Withdrawn)
            })
            .Where(row => row.Total > 0)
            .ToList();

        YearlyStats = yearlyRows;

        OverallTotals = new PatentTotals
        {
            Filing = snapshot.Count(static item => item.Status == IprStatus.FilingUnderProcess),
            Filed = snapshot.Count(static item => item.Status == IprStatus.Filed),
            Granted = snapshot.Count(static item => item.Status == IprStatus.Granted),
            Rejected = snapshot.Count(static item => item.Status == IprStatus.Rejected),
            Withdrawn = snapshot.Count(static item => item.Status == IprStatus.Withdrawn)
        };
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

        StatusOptions = Enum.GetValues<IprStatus>()
            .Select(status => new SelectListItem(GetStatusLabel(status), status.ToString())
            {
                Selected = Statuses.Contains(status)
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
}
