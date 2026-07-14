using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Models.Scheduling;
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

    [BindProperty(SupportsGet = true)]
    public HolidayType? Type { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? OfficeStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IReadOnlyList<int> AvailableYears { get; private set; } = Array.Empty<int>();
    public IReadOnlyList<HolidayListItem> Items { get; private set; } = Array.Empty<HolidayListItem>();
    public AdminPageHeaderModel Header { get; private set; } = new();
    public int GazettedCount { get; private set; }
    public int RestrictedCount { get; private set; }
    public int ObservedRestrictedCount { get; private set; }
    public int OfficeHolidayDateCount { get; private set; }
    public int UpcomingOfficeHolidayCount { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        AvailableYears = await _holidays.GetAvailableYearsAsync(cancellationToken);
        var selectedYear = Year is >= 1900 and <= 9999 ? Year.Value : _time.TodayIst.Year;
        Year = selectedYear;

        var allItems = await _holidays.ListAsync(selectedYear, cancellationToken);
        if (!AvailableYears.Contains(selectedYear))
        {
            AvailableYears = AvailableYears.Append(selectedYear).Distinct().OrderByDescending(value => value).ToArray();
        }

        GazettedCount = allItems.Count(item => item.Type == HolidayType.Gazetted);
        RestrictedCount = allItems.Count(item => item.Type == HolidayType.Restricted);
        ObservedRestrictedCount = allItems.Count(item => item.Type == HolidayType.Restricted && item.IsObservedAsOfficeHoliday);
        OfficeHolidayDateCount = allItems
            .Where(item => item.AffectsSchedule)
            .Select(item => item.Date)
            .Distinct()
            .Count();
        var today = _time.TodayIst;
        UpcomingOfficeHolidayCount = allItems
            .Where(item => item.AffectsSchedule && item.Date >= today)
            .Select(item => item.Date)
            .Distinct()
            .Count();

        IEnumerable<HolidayListItem> filtered = allItems;
        if (Type.HasValue)
        {
            filtered = filtered.Where(item => item.Type == Type.Value);
        }

        OfficeStatus = NormaliseOfficeStatus(OfficeStatus);
        filtered = OfficeStatus switch
        {
            "closed" => filtered.Where(item => item.AffectsSchedule),
            "informational" => filtered.Where(item => item.Type == HolidayType.Restricted && !item.IsObservedAsOfficeHoliday),
            _ => filtered
        };

        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        if (Search is not null)
        {
            filtered = filtered.Where(item =>
                item.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
                || (item.AuthorityReference?.Contains(Search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Items = filtered.ToArray();

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
            Text = "Add holiday entry",
            Href = Url.Page("./Create", new { year = selectedYear }),
            Icon = "bi-plus-lg",
            IsPrimary = true
        });

        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Master data · Office calendar",
            Title = "Holidays",
            Description = "Maintain Gazetted Holidays, informational Restricted Holidays and office-observed Restricted Holidays from one governed calendar.",
            Icon = "bi-calendar-week",
            Actions = actions
        };
    }

    public object RouteValues(int? year = null) => new
    {
        year = year ?? Year,
        type = Type,
        officeStatus = OfficeStatus,
        search = Search
    };

    private static string? NormaliseOfficeStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "closed" => "closed",
        "informational" => "informational",
        _ => null
    };
}
