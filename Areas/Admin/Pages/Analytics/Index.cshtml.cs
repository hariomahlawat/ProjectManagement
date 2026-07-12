using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Areas.Admin.Pages.Analytics;

[Authorize(Policy = AdminPolicies.SecurityView)]
public sealed class IndexModel : PageModel
{
    private readonly IAdminLoginOverviewService _overview;
    private readonly IAdminTimeService _time;

    public IndexModel(
        IAdminLoginOverviewService overview,
        IAdminTimeService time)
    {
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public int TotalUsers { get; private set; }
    public int ActiveUsers { get; private set; }
    public int RestrictedUsers { get; private set; }
    public IReadOnlyList<(DateOnly Date, int Count)> LoginsPerDay { get; private set; }
        = Array.Empty<(DateOnly, int)>();
    public IReadOnlyList<AdminLoginOverviewUser> TopUsers { get; private set; }
        = Array.Empty<AdminLoginOverviewUser>();
    public int[] LoginsLast30Days { get; private set; } = Array.Empty<int>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _overview.GetAsync(cancellationToken);
        TotalUsers = snapshot.TotalUsers;
        ActiveUsers = snapshot.ActiveUsers;
        RestrictedUsers = snapshot.RestrictedUsers;
        LoginsPerDay = snapshot.LoginsPerDay;
        TopUsers = snapshot.TopUsers;
        LoginsLast30Days = snapshot.LoginCounts;
    }

    public string FormatIst(DateTimeOffset? utc) => _time.FormatIst(utc);
}
