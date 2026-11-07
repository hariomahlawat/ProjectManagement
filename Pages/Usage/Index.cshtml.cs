using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
    private readonly IErpUsageModuleCatalog _moduleCatalog;

    public IndexModel(
        IErpUsageQueryService usage,
        IAdminTimeService time,
        IErpUsageModuleCatalog moduleCatalog)
    {
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _moduleCatalog = moduleCatalog ?? throw new ArgumentNullException(nameof(moduleCatalog));
    }

    [BindProperty(SupportsGet = true)] public int Days { get; set; } = 30;
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }
    [BindProperty(SupportsGet = true)] public string? Module { get; set; }
    [BindProperty(SupportsGet = true)] public string? Posture { get; set; }
    [BindProperty(SupportsGet = true)] public string? QuickFilter { get; set; }
    [BindProperty(SupportsGet = true)] public bool IncludeDisabledAccounts { get; set; }
    [BindProperty(SupportsGet = true)] public bool IncludeNonHumanAccounts { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 15;

    public ErpUsageResult Result { get; private set; } = EmptyResult();
    public IReadOnlyList<ErpUsageModuleDescriptor> ModuleOptions { get; private set; } =
        Array.Empty<ErpUsageModuleDescriptor>();

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

    public async Task<IActionResult> OnGetExportAsync(
        [FromServices] IAdminExportAuditService exportAudit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exportAudit);
        var rows = await _usage.GetExportRowsAsync(BuildQuery(), cancellationToken);

        var builder = new StringBuilder();
        SafeCsv.AppendRow(
            builder,
            "User",
            "Username",
            "Rank",
            "Roles",
            "Account state",
            "Account classification",
            "Effective monitored start",
            "Used today",
            "Last recorded activity IST",
            "Active working days",
            "Available monitored working days",
            "Active percentage",
            "Approximate active minutes",
            "Modules used",
            "Operational actions",
            "Administrative actions",
            "Adoption posture");

        foreach (var row in rows)
        {
            SafeCsv.AppendRow(
                builder,
                row.FullName,
                row.UserName,
                row.Rank,
                string.Join("; ", row.Roles),
                row.AccountState,
                row.AccountKind,
                row.EffectiveTrackingStart.ToString("yyyy-MM-dd"),
                row.UsedToday ? "Yes" : "No",
                row.LastActiveUtc.HasValue ? _time.FormatIst(row.LastActiveUtc) : string.Empty,
                row.ActiveWorkingDays,
                row.AvailableWorkingDays,
                row.ActivePercentage,
                row.ApproximateActiveMinutes,
                string.Join("; ", row.Modules),
                row.OperationalActionCount,
                row.AdministrativeActionCount,
                row.Posture);
        }

        await exportAudit.RecordAsync(
            new AdminExportRecord(
                AdminExportKind.ErpUsage,
                rows.Count,
                new Dictionary<string, string?>
                {
                    ["Days"] = Days.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Search"] = Search,
                    ["Role"] = Role,
                    ["Module"] = Module,
                    ["Posture"] = Posture,
                    ["QuickFilter"] = QuickFilter,
                    ["IncludeDisabledAccounts"] = IncludeDisabledAccounts ? "true" : null,
                    ["IncludeNonHumanAccounts"] = IncludeNonHumanAccounts ? "true" : null
                }),
            cancellationToken);

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
            quickFilter = QuickFilter,
            includeDisabledAccounts = IncludeDisabledAccounts,
            includeNonHumanAccounts = IncludeNonHumanAccounts,
            pageNumber,
            pageSize = PageSize
        }) ?? string.Empty;


    public DateOnly TrackingInceptionDateIst =>
        DateOnly.FromDateTime(_time.ToIst(Result.TrackingInceptionUtc).DateTime);

    public bool ShouldShowEffectiveTrackingStart(ErpUsageUserRow user) =>
        user.EffectiveTrackingStart > TrackingInceptionDateIst;

    public bool IsTrackingInceptionDate(DateOnly date) =>
        date == TrackingInceptionDateIst;

    public string FormatLastActive(DateTime? utc) => _time.FormatIst(utc, "Never recorded");

    public string FormatTrackingInception() => _time.FormatIst(Result.TrackingInceptionUtc);

    public static string FormatApproximateMinutes(int minutes)
    {
        if (minutes <= 0) return "—";
        var hours = minutes / 60;
        var remainder = minutes % 60;
        return hours > 0 ? $"{hours} h {remainder:D2} m" : $"{minutes} min";
    }

    private ErpUsageQuery BuildQuery() => new()
    {
        Days = Days,
        Search = Search,
        Role = Role,
        Module = Module,
        Posture = Posture,
        QuickFilter = QuickFilter,
        IncludeDisabledAccounts = IncludeDisabledAccounts,
        IncludeNonHumanAccounts = IncludeNonHumanAccounts,
        Page = PageNumber,
        PageSize = PageSize
    };

    private static ErpUsageResult EmptyResult() => new(
        DateOnly.MinValue,
        DateOnly.MinValue,
        30,
        80,
        DateTimeOffset.MinValue,
        0,
        false,
        false,
        false,
        new ErpUsageSummary(0, 0, 0, 0, 0, 0, 0, 0),
        Array.Empty<ErpUsageModuleSummary>(),
        Array.Empty<string>(),
        Array.Empty<ErpUsageUserRow>(),
        0,
        1,
        15,
        1,
        Array.Empty<DateOnly>());
}
