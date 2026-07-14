using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Infrastructure;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Usage;

namespace ProjectManagement.Pages.Usage;

[Authorize(Policy = Policies.Usage.View)]
public sealed class IndexModel : PageModel
{
    private readonly IErpUsageQueryService _usage;
    private readonly IAdminTimeService _time;
    private readonly IOptions<ErpUsageOptions> _options;
    private readonly IErpUsageModuleCatalog _moduleCatalog;

    public IndexModel(
        IErpUsageQueryService usage,
        IAdminTimeService time,
        IOptions<ErpUsageOptions> options,
        IErpUsageModuleCatalog moduleCatalog)
    {
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _moduleCatalog = moduleCatalog ?? throw new ArgumentNullException(nameof(moduleCatalog));
    }

    [BindProperty(SupportsGet = true)] public int Days { get; set; } = 30;
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }
    [BindProperty(SupportsGet = true)] public string? Module { get; set; }
    [BindProperty(SupportsGet = true)] public string? Posture { get; set; }
    [BindProperty(SupportsGet = true)] public string? AccountState { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;

    public ErpUsageResult Result { get; private set; } = EmptyResult();
    public IReadOnlyList<ErpUsageModuleDescriptor> ModuleOptions { get; private set; } = Array.Empty<ErpUsageModuleDescriptor>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["UseFullWidth"] = true;
        ViewData["PageShell"] = "analytics";
        ModuleOptions = _moduleCatalog.Modules;
        Result = await _usage.GetAsync(BuildQuery(), cancellationToken);
        Days = Result.Days;
        PageNumber = Result.Page;
        PageSize = Result.PageSize;
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        var maximumRows = _options.Value.MaximumExportRows;
        var rows = new List<ErpUsageUserRow>();
        var page = 1;
        ErpUsageResult result;

        do
        {
            result = await _usage.GetAsync(
                BuildQuery(page, 100),
                cancellationToken);
            rows.AddRange(result.Users.Take(Math.Max(0, maximumRows - rows.Count)));
            page++;
        }
        while (page <= result.TotalPages && rows.Count < maximumRows);

        var builder = new StringBuilder();
        SafeCsv.AppendRow(
            builder,
            "User",
            "Username",
            "Rank",
            "Roles",
            "Account state",
            "Used today",
            "Last active IST",
            "Active working days",
            "Available working days",
            "Active percentage",
            "Approximate active minutes",
            "Modules used",
            "Recorded actions",
            "Usage posture");

        foreach (var row in rows)
        {
            SafeCsv.AppendRow(
                builder,
                row.FullName,
                row.UserName,
                row.Rank,
                string.Join("; ", row.Roles),
                row.AccountState,
                row.UsedToday ? "Yes" : "No",
                row.LastActiveUtc.HasValue ? _time.FormatIst(row.LastActiveUtc) : string.Empty,
                row.ActiveWorkingDays,
                row.AvailableWorkingDays,
                row.ActivePercentage,
                row.ApproximateActiveMinutes,
                string.Join("; ", row.Modules),
                row.RecordedActionCount,
                row.Posture);
        }

        return File(
            SafeCsv.ToUtf8WithBom(builder.ToString()),
            "text/csv; charset=utf-8",
            $"erp-usage-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    public string PageUrl(int pageNumber) => Url.Page(
        "/Usage/Index",
        new
        {
            days = Days,
            search = Search,
            role = Role,
            module = Module,
            posture = Posture,
            accountState = AccountState,
            pageNumber,
            pageSize = PageSize
        }) ?? string.Empty;

    public string FormatLastActive(DateTime? utc) => _time.FormatIst(utc, "Never recorded");

    public static string FormatApproximateMinutes(int minutes)
    {
        if (minutes <= 0) return "—";
        var hours = minutes / 60;
        var remainder = minutes % 60;
        return hours > 0 ? $"{hours} h {remainder:D2} m" : $"{minutes} min";
    }

    private ErpUsageQuery BuildQuery(int? page = null, int? pageSize = null) => new()
    {
        Days = Days,
        Search = Search,
        Role = Role,
        Module = Module,
        Posture = Posture,
        AccountState = AccountState,
        Page = page ?? PageNumber,
        PageSize = pageSize ?? PageSize
    };

    private static ErpUsageResult EmptyResult() => new(
        DateOnly.MinValue,
        DateOnly.MinValue,
        30,
        80,
        new ErpUsageSummary(0, 0, 0, 0, 0, 0, 0),
        Array.Empty<ErpUsageModuleSummary>(),
        Array.Empty<string>(),
        Array.Empty<ErpUsageUserRow>(),
        0,
        1,
        25,
        1,
        Array.Empty<DateOnly>());
}
