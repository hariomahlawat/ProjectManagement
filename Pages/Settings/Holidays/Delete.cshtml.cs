using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Calendar;

namespace ProjectManagement.Pages.Settings.Holidays;

[Authorize(Policy = AdminPolicies.HolidaysManage)]
public sealed class DeleteModel : PageModel
{
    private readonly IHolidayAdminService _holidays;

    public DeleteModel(IHolidayAdminService holidays) =>
        _holidays = holidays ?? throw new ArgumentNullException(nameof(holidays));

    public HolidayEditItem? Item { get; private set; }

    [BindProperty]
    public string RowVersion { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        Item = await _holidays.GetAsync(id, cancellationToken);
        if (Item is null)
        {
            return NotFound();
        }

        RowVersion = Item.RowVersion;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var existing = await _holidays.GetAsync(id, cancellationToken);
        var year = existing?.Date.Year;
        var result = await _holidays.DeleteAsync(id, RowVersion, cancellationToken);

        if (!result.Succeeded)
        {
            TempData[FlashMessageKeys.AdminHolidaysError] = !string.IsNullOrWhiteSpace(result.TraceId)
                ? $"{result.UserMessage} Trace reference: {result.TraceId}."
                : result.UserMessage;
            return RedirectToPage("Index", new { year });
        }

        TempData[FlashMessageKeys.AdminHolidaysSuccess] = result.UserMessage;
        return RedirectToPage("Index", new { year });
    }
}
