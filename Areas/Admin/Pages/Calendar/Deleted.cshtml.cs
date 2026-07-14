using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Calendar;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.Calendar;

[Authorize(Policy = AdminPolicies.RecoveryManage)]
public sealed class DeletedModel : PageModel
{
    private readonly ICalendarRecoveryService _recovery;
    private readonly IAdminTimeService _time;
    private readonly IAdminNavigationUrlBuilder _navigation;

    public DeletedModel(
        ICalendarRecoveryService recovery,
        IAdminTimeService time,
        IAdminNavigationUrlBuilder navigation)
    {
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public EventCategory? Category { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateTime? DeletedFrom { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateTime? DeletedTo { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty] public Guid RestoreEventId { get; set; }

    public AdminPageHeaderModel Header { get; private set; } = new();
    public DeletedCalendarEventPage Result { get; private set; } =
        new(Array.Empty<DeletedCalendarEventItem>(), 0, 0, 0, 1, 25);
    public IReadOnlyList<EventCategory> Categories { get; } = Enum.GetValues<EventCategory>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRestoreAsync(CancellationToken cancellationToken)
    {
        NormalizeFilters();
        var result = await _recovery.RestoreAsync(RestoreEventId, cancellationToken);
        TempData[result.Succeeded ? FlashMessageKeys.AdminCalendarRecoverySuccess : FlashMessageKeys.AdminCalendarRecoveryError] =
            FormatMessage(result);
        return RedirectToPage(RouteValues());
    }

    public string FormatSchedule(DeletedCalendarEventItem item)
    {
        var start = _time.ToIst(item.StartUtc);
        var end = _time.ToIst(item.EndUtc);
        if (item.IsAllDay)
        {
            var startDate = DateOnly.FromDateTime(start.DateTime);
            var endDate = DateOnly.FromDateTime(end.DateTime.AddTicks(-1));
            return startDate == endDate ? startDate.ToString("dd MMM yyyy") : $"{startDate:dd MMM yyyy} – {endDate:dd MMM yyyy}";
        }
        return start.Date == end.Date
            ? $"{start:dd MMM yyyy, HH:mm}–{end:HH:mm} IST"
            : $"{start:dd MMM yyyy, HH:mm} – {end:dd MMM yyyy, HH:mm} IST";
    }

    public string FormatDeletedAt(DateTimeOffset utc) => _time.FormatIst(utc);
    public string PageUrl(int page) => Url.Page(null, new
    {
        Search,
        Category,
        DeletedFrom = DeletedFrom?.ToString("yyyy-MM-dd"),
        DeletedTo = DeletedTo?.ToString("yyyy-MM-dd"),
        PageNumber = page,
        PageSize
    }) ?? "#";

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Result = await _recovery.QueryAsync(new DeletedCalendarEventQuery(
            Search,
            Category,
            DeletedFrom.HasValue ? DateOnly.FromDateTime(DeletedFrom.Value) : null,
            DeletedTo.HasValue ? DateOnly.FromDateTime(DeletedTo.Value) : null,
            PageNumber,
            PageSize), cancellationToken);
        PageNumber = Result.Page;
        PageSize = Result.PageSize;
        Header = new AdminPageHeaderModel
        {
            Eyebrow = "Recovery and retention",
            Title = "Deleted calendar events",
            Description = "Review removed calendar records and restore the original event or recurring series to the shared calendar.",
            Icon = "bi-calendar-x",
            Actions = new[]
            {
                new AdminPageActionModel
                {
                    Text = "Recovery centre",
                    Href = _navigation.GetPath(HttpContext, AdminNavigationKeys.RecoveryCentre),
                    Icon = "bi-arrow-left"
                }
            }
        };
    }

    private void NormalizeFilters()
    {
        Search = Normalize(Search, 160);
        PageNumber = Math.Max(1, PageNumber);
        PageSize = PageSize is 10 or 25 or 50 or 100 ? PageSize : 25;
        if (DeletedFrom.HasValue) DeletedFrom = DeletedFrom.Value.Date;
        if (DeletedTo.HasValue) DeletedTo = DeletedTo.Value.Date;
        if (DeletedFrom.HasValue && DeletedTo.HasValue && DeletedFrom > DeletedTo)
            (DeletedFrom, DeletedTo) = (DeletedTo, DeletedFrom);
    }

    private object RouteValues() => new
    {
        Search,
        Category,
        DeletedFrom = DeletedFrom?.ToString("yyyy-MM-dd"),
        DeletedTo = DeletedTo?.ToString("yyyy-MM-dd"),
        PageNumber,
        PageSize
    };

    private static string FormatMessage(AdminOperationResult result) =>
        !string.IsNullOrWhiteSpace(result.TraceId)
            ? $"{result.UserMessage} Trace reference: {result.TraceId}."
            : result.UserMessage ?? (result.Succeeded ? "Operation completed." : "Operation failed.");

    private static string? Normalize(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
