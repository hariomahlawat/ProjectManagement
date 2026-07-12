using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Calendar;

namespace ProjectManagement.Areas.Admin.Pages.Calendar;

[Authorize(Policy = AdminPolicies.RecoveryManage)]
public sealed class DeletedModel : PageModel
{
    private readonly ICalendarRecoveryService _recovery;
    private readonly IAdminTimeService _time;

    public DeletedModel(ICalendarRecoveryService recovery, IAdminTimeService time)
    {
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public EventCategory? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public DeletedCalendarEventPage Result { get; private set; } =
        new(Array.Empty<DeletedCalendarEventItem>(), 0, 1, 20);

    public IReadOnlyList<EventCategory> Categories { get; } = Enum.GetValues<EventCategory>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var search = Search?.Trim();
        Search = string.IsNullOrWhiteSpace(search)
            ? null
            : search[..Math.Min(search.Length, 160)];

        Result = await _recovery.QueryAsync(
            new DeletedCalendarEventQuery(Search, Category, PageNumber, 20),
            cancellationToken);
        PageNumber = Result.Page;
    }

    public async Task<IActionResult> OnPostRestoreAsync(
        Guid id,
        string? search,
        EventCategory? category,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        var result = await _recovery.RestoreAsync(id, cancellationToken);
        TempData[result.Succeeded
            ? FlashMessageKeys.AdminCalendarRecoverySuccess
            : FlashMessageKeys.AdminCalendarRecoveryError] = FormatMessage(result);

        return RedirectToPage(new
        {
            Search = search,
            Category = category,
            PageNumber = Math.Max(1, pageNumber)
        });
    }

    public string FormatSchedule(DeletedCalendarEventItem item)
    {
        var start = _time.ToIst(item.StartUtc);
        var end = _time.ToIst(item.EndUtc);

        if (item.IsAllDay)
        {
            var startDate = DateOnly.FromDateTime(start.DateTime);
            var endDate = DateOnly.FromDateTime(end.DateTime.AddTicks(-1));
            return startDate == endDate
                ? startDate.ToString("dd MMM yyyy")
                : $"{startDate:dd MMM yyyy} – {endDate:dd MMM yyyy}";
        }

        return start.Date == end.Date
            ? $"{start:dd MMM yyyy, HH:mm}–{end:HH:mm} IST"
            : $"{start:dd MMM yyyy, HH:mm} – {end:dd MMM yyyy, HH:mm} IST";
    }

    public string FormatDeletedAt(DateTimeOffset utc) => _time.FormatIst(utc);

    private static string FormatMessage(AdminOperationResult result) =>
        !string.IsNullOrWhiteSpace(result.TraceId)
            ? $"{result.UserMessage} Trace reference: {result.TraceId}."
            : result.UserMessage ?? (result.Succeeded ? "Operation completed." : "Operation failed.");
}
