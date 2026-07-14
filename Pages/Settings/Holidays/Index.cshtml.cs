using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Calendar;

namespace ProjectManagement.Pages.Settings.Holidays;

[Authorize(Policy = AdminPolicies.HolidaysManage)]
public sealed class IndexModel : PageModel
{
    private readonly IHolidayAdminService _holidays;
    private readonly IAdminTimeService _time;
    private readonly IAuthorizationService _authorization;

    public IndexModel(
        IHolidayAdminService holidays,
        IAdminTimeService time,
        IAuthorizationService authorization)
    {
        _holidays = holidays ?? throw new ArgumentNullException(nameof(holidays));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    public IReadOnlyList<int> AvailableYears { get; private set; } = Array.Empty<int>();
    public IReadOnlyList<HolidayListItem> Items { get; private set; } = Array.Empty<HolidayListItem>();
    public AdminPageHeaderModel Header { get; private set; } = new();
    public int UpcomingCount { get; private set; }
    public int ElapsedCount { get; private set; }
    public int WeekendCount { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        AvailableYears = await _holidays.GetAvailableYearsAsync(cancellationToken);
        var selectedYear = Year is >= 1900 and <= 9999 ? Year.Value : _time.TodayIst.Year;
        Year = selectedYear;
        Items = await _holidays.ListAsync(selectedYear, cancellationToken);
        if (!AvailableYears.Contains(selectedYear))
        {
            AvailableYears = AvailableYears.Append(selectedYear).Distinct().OrderByDescending(value => value).ToArray();
        }

        var today = _time.TodayIst;
        UpcomingCount = Items.Count(item => item.Date >= today);
        ElapsedCount = Items.Count(item => item.Date < today);
        WeekendCount = Items.Count(item => item.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        var actions = new List<AdminPageActionModel>();
        if ((await _authorization.AuthorizeAsync(User, AdminPolicies.MasterDataManage)).Succeeded)
        {
            actions.Add(new AdminPageActionModel
            {
                Text = "Master data centre",
                Href = Url.Page("/MasterData/Index", new { area = "Admin" }),
                Icon = "bi-arrow-left"
            });
        }
        actions.Add(new AdminPageActionModel
        {
            Text = "Add holiday",
            Href = Url.Page("./Create", new { year = selectedYear }),
            Icon = "bi-plus-lg",
            IsPrimary = true
        });

        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Master data · Calendar configuration",
            Title = "Holidays",
            Description = "Maintain official non-working dates used by project schedules and calendar views.",
            Icon = "bi-calendar-week",
            Actions = actions
        };
    }
}
