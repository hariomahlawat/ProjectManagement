using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Calendar;

namespace ProjectManagement.Pages.Settings.Holidays;

[Authorize(Policy = AdminPolicies.HolidaysManage)]
public sealed class IndexModel : PageModel
{
    private readonly IHolidayAdminService _holidays;
    private readonly IAdminTimeService _time;

    public IndexModel(IHolidayAdminService holidays, IAdminTimeService time)
    {
        _holidays = holidays ?? throw new ArgumentNullException(nameof(holidays));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    public IReadOnlyList<int> AvailableYears { get; private set; } = Array.Empty<int>();
    public IReadOnlyList<HolidayListItem> Items { get; private set; } = Array.Empty<HolidayListItem>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        AvailableYears = await _holidays.GetAvailableYearsAsync(cancellationToken);
        var selectedYear = Year is >= 1900 and <= 9999
            ? Year.Value
            : _time.TodayIst.Year;
        Year = selectedYear;
        Items = await _holidays.ListAsync(selectedYear, cancellationToken);

        if (!AvailableYears.Contains(selectedYear))
        {
            AvailableYears = AvailableYears
                .Append(selectedYear)
                .Distinct()
                .OrderByDescending(year => year)
                .ToArray();
        }
    }
}
