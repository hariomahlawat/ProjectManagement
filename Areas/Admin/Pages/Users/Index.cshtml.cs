using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Pages.Users;

[Authorize(Policy = AdminPolicies.UsersManage)]
[ResponseCache(NoStore = true)]
public sealed class IndexModel : PageModel
{
    private static readonly int[] SupportedPageSizes = { 10, 25, 50, 100 };
    private static readonly HashSet<string> SupportedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "must-change-password",
        "locked",
        "disabled",
        "pending-deletion"
    };

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

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Role { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNo { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    public AdminPageHeaderModel Header { get; private set; } = new();

    public IReadOnlyList<AdminUserRow> Users { get; private set; } =
        Array.Empty<AdminUserRow>();

    public IReadOnlyList<string> AllRoles { get; private set; } =
        Array.Empty<string>();

    public AdminUserSummary Summary { get; private set; } =
        new(0, 0, 0, 0, 0, 0);

    public IReadOnlyList<UserStatusFilter> StatusFilters { get; private set; } =
        Array.Empty<UserStatusFilter>();

    public IReadOnlyList<int> PageSizeOptions => SupportedPageSizes;

    public int Total { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public int FirstRowNumber => Total == 0 ? 0 : ((PageNo - 1) * PageSize) + 1;

    public int LastRowNumber => Math.Min(Total, PageNo * PageSize);

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Q)
        || !string.IsNullOrWhiteSpace(Role)
        || !string.IsNullOrWhiteSpace(Status);

    public int ActiveFilterCount =>
        (string.IsNullOrWhiteSpace(Q) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(Role) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(Status) ? 0 : 1);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormaliseRequest();
        var result = await _queries.GetAsync(BuildRequest(), cancellationToken);

        Users = result.Rows;
        AllRoles = result.Roles;
        Summary = result.Summary;
        Total = result.Total;
        PageNo = result.Page;
        PageSize = result.PageSize;
        TotalPages = result.TotalPages;
        StatusFilters = BuildStatusFilters(Summary);
        Header = BuildHeader();
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        NormaliseRequest();
        var rows = await _queries.GetForExportAsync(
            BuildRequest() with { Page = 1, PageSize = 100 },
            cancellationToken);

        var bytes = _csv.Write(
            new[]
            {
                "Username",
                "FullName",
                "Rank",
                "Roles",
                "AccountState",
                "LastLoginIST",
                "LoginCount",
                "CreatedIST"
            },
            rows.Select(user => (IReadOnlyList<object?>)new object?[]
            {
                user.UserName,
                user.FullName,
                user.Rank,
                string.Join(';', user.Roles),
                user.AccountState.DisplayName,
                _time.FormatIst(user.LastLoginUtc),
                user.LoginCount,
                _time.FormatIst(user.CreatedUtc)
            }));

        var fileName = $"prism-users-{_time.UtcNow:yyyyMMdd}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    public string FormatIst(DateTime? utc) => _time.FormatIst(utc);

    public string FormatIst(DateTimeOffset? utc) => _time.FormatIst(utc);

    public bool IsStatusActive(string? status) =>
        string.Equals(Status?.Trim(), status?.Trim(), StringComparison.OrdinalIgnoreCase);

    public string StatusCountLabel(int count) => count == 1 ? "1 account" : $"{count:N0} accounts";

    private AdminPageHeaderModel BuildHeader() => new()
    {
        Eyebrow = "Access & security",
        Title = "Users",
        Description = "Manage identities, assigned roles and account lifecycle state.",
        Icon = "bi-people",
        Actions = new[]
        {
            new AdminPageActionModel
            {
                Text = "Create user",
                Href = Url.Page("./Create"),
                Icon = "bi-person-plus",
                IsPrimary = true
            }
        }
    };

    private AdminUserListRequest BuildRequest() => new(
        Q,
        Role,
        Status,
        PageNo,
        PageSize);

    private void NormaliseRequest()
    {
        Q = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();
        if (Q is { Length: > 100 })
        {
            Q = Q[..100];
        }
        Role = string.IsNullOrWhiteSpace(Role) ? null : Role.Trim();
        Status = string.IsNullOrWhiteSpace(Status) ? null : Status.Trim().ToLowerInvariant();
        if (Status is not null && !SupportedStatuses.Contains(Status))
        {
            Status = null;
        }
        PageNo = Math.Max(1, PageNo);
        PageSize = SupportedPageSizes.Contains(PageSize) ? PageSize : 25;
    }

    private static IReadOnlyList<UserStatusFilter> BuildStatusFilters(AdminUserSummary summary) =>
        new[]
        {
            new UserStatusFilter(null, "All users", summary.Total, "bi-people", "neutral"),
            new UserStatusFilter("active", "Active", summary.Active, "bi-check-circle", "success"),
            new UserStatusFilter(
                "must-change-password",
                "Password change",
                summary.MustChangePassword,
                "bi-key",
                "info"),
            new UserStatusFilter(
                "locked",
                "Locked",
                summary.TemporarilyLocked,
                "bi-lock",
                "warning"),
            new UserStatusFilter(
                "disabled",
                "Disabled",
                summary.Disabled,
                "bi-person-slash",
                "neutral"),
            new UserStatusFilter(
                "pending-deletion",
                "Pending deletion",
                summary.PendingDeletion,
                "bi-person-x",
                "danger")
        };

    public sealed record UserStatusFilter(
        string? Key,
        string Label,
        int Count,
        string Icon,
        string Tone);
}
