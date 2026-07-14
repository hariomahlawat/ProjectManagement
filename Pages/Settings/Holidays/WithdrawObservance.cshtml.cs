using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Calendar;

namespace ProjectManagement.Pages.Settings.Holidays;

[Authorize(Policy = AdminPolicies.HolidaysManage)]
public sealed class WithdrawObservanceModel : PageModel
{
    private readonly IHolidayAdminService _holidays;
    public WithdrawObservanceModel(IHolidayAdminService holidays) => _holidays = holidays ?? throw new ArgumentNullException(nameof(holidays));

    public HolidayEditItem? Item { get; private set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        Item = await _holidays.GetAsync(id, cancellationToken);
        if (Item is null) return NotFound();
        if (Item.Type != HolidayType.Restricted) return RedirectToPage("./Index", new { year = Item.Date.Year });
        if (!Item.IsObservedAsOfficeHoliday) return RedirectToPage("./Observe", new { id });
        Input = new InputModel { Id = Item.Id, Remarks = Item.ObservanceRemarks, RowVersion = Item.RowVersion };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Item = await _holidays.GetAsync(Input.Id, cancellationToken);
        if (Item is null) return NotFound();
        if (!Input.Confirmed) ModelState.AddModelError("Input.Confirmed", "Confirm that the office will remain open on this date before continuing.");
        if (!ModelState.IsValid) return Page();

        var result = await _holidays.WithdrawOfficeObservanceAsync(Input.Id, Input.Remarks, Input.RowVersion, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, !string.IsNullOrWhiteSpace(result.TraceId)
                ? $"{result.UserMessage} Trace reference: {result.TraceId}."
                : result.UserMessage ?? "Office observance could not be withdrawn.");
            var latest = await _holidays.GetAsync(Input.Id, cancellationToken);
            if (latest is not null) { Item = latest; Input.RowVersion = latest.RowVersion; }
            return Page();
        }

        TempData[FlashMessageKeys.AdminHolidaysSuccess] = result.UserMessage;
        return RedirectToPage("./Index", new { year = Item.Date.Year });
    }

    public sealed class InputModel
    {
        [Required] public int Id { get; set; }
        [StringLength(1200), Display(Name = "Withdrawal remarks")] public string? Remarks { get; set; }
        [Required] public string RowVersion { get; set; } = string.Empty;
        [Display(Name = "I confirm that this date will become a normal working day while the RH remains visible for information.")]
        public bool Confirmed { get; set; }
    }
}
