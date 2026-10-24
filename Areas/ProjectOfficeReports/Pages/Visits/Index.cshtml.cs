using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

// The Visits to SDD register: one page for browsing, filtering, exporting and the trends.
// /Visits/All redirects here with its query string.
[Authorize]
public class IndexModel : PageModel
{
    public static readonly int[] AllowedPageSizes = [25, 50, 100];

    public static readonly IReadOnlyList<(string Value, string Label)> SortOptions =
    [
        ("newest", "Newest first"),
        ("oldest", "Oldest first"),
        ("strength", "Largest group"),
        ("visitor", "Visitor A–Z"),
        ("type", "Visit type")
    ];

    private readonly VisitService _visitService;
    private readonly VisitTypeService _visitTypeService;
    private readonly IVisitExportService _visitExportService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(
        VisitService visitService,
        VisitTypeService visitTypeService,
        IVisitExportService visitExportService,
        UserManager<ApplicationUser> userManager)
    {
        _visitService = visitService;
        _visitTypeService = visitTypeService;
        _visitExportService = visitExportService;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public Guid? VisitTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    // Rows on this page of the register.
    public IReadOnlyList<VisitListItem> Items { get; private set; } = Array.Empty<VisitListItem>();

    // Every visit matching the filters; the trends are drawn from these.
    public IReadOnlyList<VisitListItem> AllItems { get; private set; } = Array.Empty<VisitListItem>();

    public int TotalItems { get; private set; }

    public int TotalPeople { get; private set; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalItems / PageSize));

    public int ShowingFrom => TotalItems == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;

    public int ShowingTo => Math.Min(PageNumber * PageSize, TotalItems);

    // Headline figures always cover the whole register, whatever the filters.
    public VisitRegisterSummary Summary { get; private set; } = VisitRegisterSummary.Empty;

    public IReadOnlyList<SelectListItem> VisitTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    // Trends for the visits matching the filters: twelve calendar months ending this month, and the mix of types.
    public IReadOnlyList<VisitMonthPoint> MonthlyTrend { get; private set; } = Array.Empty<VisitMonthPoint>();

    public IReadOnlyList<VisitTypeShare> TypeMix { get; private set; } = Array.Empty<VisitTypeShare>();

    // Visits and visitors per month across every matching visit, for the month headings in the register.
    public IReadOnlyDictionary<(int Year, int Month), (int Visits, int People)> MonthTotals { get; private set; }
        = new Dictionary<(int Year, int Month), (int Visits, int People)>();

    // A stable colour slot (1–6) per visit type, so a type keeps its colour across the page and the chart.
    public IReadOnlyDictionary<Guid, int> TypeColourSlots { get; private set; } = new Dictionary<Guid, int>();

    public bool CanManage { get; private set; }

    public bool CanManageTypes => User.IsInRole("Admin");

    public DateOnly? FromDate => ParseDate(From);

    public DateOnly? ToDate => ParseDate(To);

    public string SortKey => SortOptions.Any(option => option.Value == Sort) ? Sort! : "newest";

    public bool HasFilters => VisitTypeId.HasValue || FromDate.HasValue || ToDate.HasValue || !string.IsNullOrWhiteSpace(Q);

    // Month headings make sense only while the register is in date order.
    public bool GroupByMonth => SortKey is "newest" or "oldest";

    public string? SelectedTypeLabel => VisitTypeId.HasValue
        ? VisitTypeOptions.FirstOrDefault(option => option.Value == VisitTypeId.Value.ToString())?.Text
        : null;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanManage = IsManager();
        if (!AllowedPageSizes.Contains(PageSize))
        {
            PageSize = 25;
        }

        await PopulateVisitTypesAsync(cancellationToken);

        var matching = await _visitService.SearchAsync(BuildQuery(), cancellationToken);
        var register = HasFilters
            ? await _visitService.SearchAsync(new VisitQueryOptions(null, null, null, null), cancellationToken)
            : matching;

        var today = DateOnly.FromDateTime(DateTime.Today);
        Summary = VisitRegisterSummary.From(register, today);

        AllItems = matching;
        TotalItems = matching.Count;
        TotalPeople = matching.Sum(v => v.Strength);

        BuildTrends(matching, today);

        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Items = ApplySort(matching)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, string rowVersion, CancellationToken cancellationToken)
    {
        if (!IsManager())
        {
            return Forbid();
        }

        var bytes = DecodeRowVersion(rowVersion);
        if (bytes == null)
        {
            TempData["ToastError"] = "We could not verify your request. Please try again.";
            return RedirectToPage(new { VisitTypeId, From, To, Q });
        }

        var result = await _visitService.DeleteAsync(id, bytes, _userManager.GetUserId(User) ?? string.Empty, cancellationToken);
        switch (result.Outcome)
        {
            case VisitDeletionOutcome.Success:
                TempData["ToastSuccess"] = "Visit deleted.";
                break;
            case VisitDeletionOutcome.ConcurrencyConflict:
                TempData["ToastError"] = "The visit was modified by someone else. Please reload the page.";
                break;
            default:
                TempData["ToastError"] = "Visit not found.";
                break;
        }

        return RedirectToPage(new { VisitTypeId, From, To, Q });
    }

    // Exports cover every visit matching the filters, not just the page on screen.
    public Task<IActionResult> OnPostExportAsync(CancellationToken cancellationToken)
        => ExportAsync(pdf: false, cancellationToken);

    public Task<IActionResult> OnPostExportPdfAsync(CancellationToken cancellationToken)
        => ExportAsync(pdf: true, cancellationToken);

    private async Task<IActionResult> ExportAsync(bool pdf, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var options = BuildQuery();
        var request = new VisitExportRequest(
            options.VisitTypeId,
            options.StartDate,
            options.EndDate,
            options.RemarksQuery,
            userId);

        var result = pdf
            ? await _visitExportService.ExportPdfAsync(request, cancellationToken)
            : await _visitExportService.ExportAsync(request, cancellationToken);

        if (!result.Success || result.File is null)
        {
            // Go back to the register with the filters kept, so the page and its figures stay intact.
            TempData["ToastError"] = result.Errors.Count > 0 ? result.Errors[0] : "The export could not be created. Please try again.";
            return RedirectToPage(new { VisitTypeId, From, To, Q, Sort });
        }

        return File(result.File.Content, result.File.ContentType, result.File.FileName);
    }

    // Route values for links that keep the current filters, with some values replaced.
    public Dictionary<string, string?> RouteWith(
        string? removeKey = null,
        int? pageNumber = null,
        string? sort = null,
        int? pageSize = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["VisitTypeId"] = VisitTypeId?.ToString(),
            ["From"] = FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["To"] = ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Q"] = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim(),
            ["Sort"] = (sort ?? SortKey) == "newest" ? null : sort ?? SortKey,
            ["PageSize"] = (pageSize ?? PageSize) == 25 ? null : (pageSize ?? PageSize).ToString(CultureInfo.InvariantCulture),
            ["PageNumber"] = pageNumber is > 1 ? pageNumber.Value.ToString(CultureInfo.InvariantCulture) : null
        };

        if (removeKey is not null)
        {
            values[removeKey] = null;
        }

        return values
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private IEnumerable<VisitListItem> ApplySort(IReadOnlyList<VisitListItem> items)
    {
        // The service returns newest first (then most recently recorded); OrderBy is stable,
        // so every other order keeps that as its tie-breaker.
        return SortKey switch
        {
            "oldest" => items.Reverse(),
            "strength" => items.OrderByDescending(v => v.Strength),
            "visitor" => items.OrderBy(v => v.VisitorName, StringComparer.CurrentCultureIgnoreCase),
            "type" => items.OrderBy(v => v.VisitTypeName, StringComparer.CurrentCultureIgnoreCase),
            _ => items
        };
    }

    public int ColourSlotFor(Guid visitTypeId) => VisitTypeColours.SlotOf(TypeColourSlots, visitTypeId);

    private void BuildTrends(IReadOnlyList<VisitListItem> visits, DateOnly today)
    {
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        var byMonth = visits
            .GroupBy(v => (v.DateOfVisit.Year, v.DateOfVisit.Month))
            .ToDictionary(g => g.Key, g => (Visits: g.Count(), People: g.Sum(v => v.Strength)));

        MonthTotals = byMonth;
        MonthlyTrend = Enumerable.Range(0, 12)
            .Select(i => firstOfThisMonth.AddMonths(i - 11))
            .Select(month =>
            {
                var totals = byMonth.TryGetValue((month.Year, month.Month), out var found) ? found : (Visits: 0, People: 0);
                return new VisitMonthPoint(month.ToString("MMM yy", CultureInfo.InvariantCulture), totals.Visits, totals.People);
            })
            .ToList();

        var lastYearFrom = today.AddYears(-1).AddDays(1);
        TypeMix = visits
            .GroupBy(v => (v.VisitTypeId, v.VisitTypeName))
            .OrderByDescending(g => g.Count())
            .Select(g => new VisitTypeShare(g.Key.VisitTypeName, g.Count(), g.Count(v => v.DateOfVisit >= lastYearFrom && v.DateOfVisit <= today), ColourSlotFor(g.Key.VisitTypeId)))
            .ToList();
    }

    private VisitQueryOptions BuildQuery()
    {
        return new VisitQueryOptions(VisitTypeId, FromDate, ToDate, Q);
    }

    private async Task PopulateVisitTypesAsync(CancellationToken cancellationToken)
    {
        // Inactive types stay filterable: older visits still carry them.
        var types = await _visitTypeService.GetAllAsync(includeInactive: true, cancellationToken);
        TypeColourSlots = VisitTypeColours.SlotsFor(types);


        var list = new List<SelectListItem>
        {
            new("All types", string.Empty)
        };

        foreach (var type in types.OrderByDescending(t => t.IsActive).ThenBy(t => t.Name))
        {
            list.Add(new SelectListItem(type.IsActive ? type.Name : $"{type.Name} (inactive)", type.Id.ToString())
            {
                Selected = VisitTypeId.HasValue && type.Id == VisitTypeId.Value
            });
        }

        VisitTypeOptions = list;
    }

    private bool IsManager()
    {
        return User.IsInRole("Admin") || User.IsInRole("HoD") || User.IsInRole("Project Office") || User.IsInRole("ProjectOffice");
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        if (DateOnly.TryParse(value, out date))
        {
            return date;
        }

        return null;
    }

    private static byte[]? DecodeRowVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

// Headline figures for the register: the last 12 months and the last 30 days, both inclusive of today.
public sealed record VisitRegisterSummary(int VisitsLastYear, int PeopleLastYear, int VisitsLast30, int PeopleLast30, int TotalVisits, DateOnly? LatestVisit)
{
    public static readonly VisitRegisterSummary Empty = new(0, 0, 0, 0, 0, null);

    public static VisitRegisterSummary From(IReadOnlyList<VisitListItem> visits, DateOnly today)
    {
        var lastYearFrom = today.AddYears(-1).AddDays(1);
        var last30From = today.AddDays(-29);
        var lastYear = visits.Where(v => v.DateOfVisit >= lastYearFrom && v.DateOfVisit <= today).ToList();
        var last30 = lastYear.Where(v => v.DateOfVisit >= last30From).ToList();

        return new VisitRegisterSummary(
            lastYear.Count,
            lastYear.Sum(v => v.Strength),
            last30.Count,
            last30.Sum(v => v.Strength),
            visits.Count,
            visits.Count == 0 ? null : visits.Max(v => v.DateOfVisit));
    }
}

public sealed record VisitMonthPoint(string Label, int Visits, int Strength);

public sealed record VisitTypeShare(string Name, int Count, int CountLastYear, int ColourSlot);
