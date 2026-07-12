using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Pages.Logs;

[Authorize(Policy = AdminPolicies.LogsView)]
public sealed class IndexModel : PageModel
{
    private const int MaximumExportRows = 100_000;

    private readonly IAdminLogQueryService _queries;
    private readonly ISafeCsvWriter _csv;
    private readonly IAdminTimeService _time;

    public IndexModel(
        IAdminLogQueryService queries,
        ISafeCsvWriter csv,
        IAdminTimeService time)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _csv = csv ?? throw new ArgumentNullException(nameof(csv));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    [BindProperty(SupportsGet = true)] public string? Level { get; set; }
    [BindProperty(SupportsGet = true)] public string? Action { get; set; }
    [BindProperty(SupportsGet = true, Name = "User")] public string? UserName { get; set; }
    [BindProperty(SupportsGet = true)] public string? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ip { get; set; }
    [BindProperty(SupportsGet = true)] public string? Contains { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "Time";
    [BindProperty(SupportsGet = true)] public string Dir { get; set; } = "desc";

    public int Total { get; private set; }
    public int TotalPages { get; private set; } = 1;
    public IReadOnlyList<AdminLogRow> Rows { get; private set; } = Array.Empty<AdminLogRow>();
    public IReadOnlyList<string> ActionOptions { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> SeriesLabels { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<int> SeriesCounts { get; private set; } = Array.Empty<int>();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await _queries.GetAsync(BuildQuery(), cancellationToken);
        Rows = result.Rows;
        ActionOptions = result.ActionOptions;
        SeriesLabels = result.SeriesLabels;
        SeriesCounts = result.SeriesCounts;
        Total = result.Total;
        PageNo = result.Page;
        PageSize = result.PageSize;
        TotalPages = result.TotalPages;
        return Page();
    }

    public async Task<FileResult> OnGetExportCsvAsync(CancellationToken cancellationToken)
    {
        var rows = await _queries.GetForExportAsync(BuildQuery(), MaximumExportRows, cancellationToken);
        var bytes = _csv.Write(
            new[] { "TimeIST", "Level", "Action", "UserId", "User", "IP", "Message", "DataJson" },
            rows.Select(row => (IReadOnlyList<object?>)new object?[]
            {
                _time.FormatIst(row.TimeUtc),
                row.Level,
                row.Action,
                row.UserId,
                row.UserName,
                row.Ip,
                row.Message,
                row.DataJson
            }));

        return File(
            bytes,
            "text/csv; charset=utf-8",
            $"logs_{_time.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    public string FormatIst(DateTime utc) => _time.FormatIst(utc);

    private AdminLogQuery BuildQuery() => new(
        Level,
        Action,
        UserName,
        UserId,
        Ip,
        Contains,
        From.HasValue ? DateOnly.FromDateTime(From.Value) : null,
        To.HasValue ? DateOnly.FromDateTime(To.Value) : null,
        PageNo,
        PageSize,
        Sort,
        Dir);
}
