using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Pages.Users;

[Authorize(Policy = AdminPolicies.UsersManage)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : PageModel
{
    private readonly IAdminUserQueryService _queries;
    private readonly ISafeCsvWriter _csv;
    private readonly IAdminTimeService _time;

    public IndexModel(
        IAdminUserQueryService queries,
        ISafeCsvWriter csv,
        IAdminTimeService time)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
        _csv = csv ?? throw new ArgumentNullException(nameof(csv));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNo { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;

    public IReadOnlyList<AdminUserRow> Users { get; private set; } = Array.Empty<AdminUserRow>();
    public IReadOnlyList<string> AllRoles { get; private set; } = Array.Empty<string>();
    public int Total { get; private set; }
    public int TotalPages { get; private set; } = 1;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await _queries.GetAsync(BuildRequest(), cancellationToken);
        Users = result.Rows;
        AllRoles = result.Roles;
        Total = result.Total;
        PageNo = result.Page;
        PageSize = result.PageSize;
        TotalPages = result.TotalPages;
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        var rows = await _queries.GetForExportAsync(BuildRequest() with { Page = 1, PageSize = 100 }, cancellationToken);
        var bytes = _csv.Write(
            new[] { "UserName", "FullName", "Rank", "Roles", "LastLoginIST", "LoginCount", "Status" },
            rows.Select(user => (IReadOnlyList<object?>)new object?[]
            {
                user.UserName,
                user.FullName,
                user.Rank,
                string.Join(';', user.Roles),
                _time.FormatIst(user.LastLoginUtc),
                user.LoginCount,
                user.AccountState.DisplayName
            }));

        return File(bytes, "text/csv; charset=utf-8", "users.csv");
    }

    public string FormatIst(DateTime? utc) => _time.FormatIst(utc);
    public string FormatIst(DateTimeOffset? utc) => _time.FormatIst(utc);

    private AdminUserListRequest BuildRequest() => new(
        Q,
        Role,
        Status,
        PageNo,
        PageSize);
}
