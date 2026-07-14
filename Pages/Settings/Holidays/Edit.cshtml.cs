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
public sealed class EditModel : PageModel
{
    private readonly IHolidayAdminService _holidays;
    public EditModel(IHolidayAdminService holidays) => _holidays = holidays ?? throw new ArgumentNullException(nameof(holidays));

    [BindProperty]
    public InputModel Input { get; set; } = new();
    public bool CurrentObservedStatus { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var holiday = await _holidays.GetAsync(id, cancellationToken);
        if (holiday is null) return NotFound();
        Map(holiday);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var current = await _holidays.GetAsync(Input.Id, cancellationToken);
            if (current is null) return NotFound();
            CurrentObservedStatus = current.IsObservedAsOfficeHoliday;
            return Page();
        }

        var result = await _holidays.UpdateAsync(
            Input.Id, Input.Date, Input.Name, Input.Type,
            Input.AuthorityReference, Input.Remarks, Input.RowVersion, cancellationToken);

        if (!result.Succeeded)
        {
            var message = !string.IsNullOrWhiteSpace(result.TraceId)
                ? $"{result.UserMessage} Trace reference: {result.TraceId}."
                : result.UserMessage ?? "The holiday could not be updated.";
            if (result.ErrorCode is "DuplicateHoliday" or "GazettedDateAlreadyExists") ModelState.AddModelError("Input.Date", message);
            else ModelState.AddModelError(string.Empty, message);

            var latest = await _holidays.GetAsync(Input.Id, cancellationToken);
            if (latest is not null)
            {
                CurrentObservedStatus = latest.IsObservedAsOfficeHoliday;
                if (result.ErrorCode == "ConcurrencyConflict") Input.RowVersion = latest.RowVersion;
            }
            return Page();
        }

        TempData[FlashMessageKeys.AdminHolidaysSuccess] = result.UserMessage;
        return RedirectToPage("Index", new { year = Input.Date.Year });
    }

    private void Map(HolidayEditItem holiday)
    {
        Input = new InputModel
        {
            Id = holiday.Id,
            Date = holiday.Date,
            Name = holiday.Name,
            Type = holiday.Type,
            AuthorityReference = holiday.AuthorityReference,
            Remarks = holiday.ObservanceRemarks,
            RowVersion = holiday.RowVersion
        };
        CurrentObservedStatus = holiday.IsObservedAsOfficeHoliday;
    }

    public sealed class InputModel
    {
        [Required] public int Id { get; set; }
        [Required, DataType(DataType.Date)] public DateOnly Date { get; set; }
        [Required, StringLength(160), Display(Name = "Holiday name")] public string Name { get; set; } = string.Empty;
        [Required, Display(Name = "Classification")] public HolidayType Type { get; set; }
        [StringLength(240), Display(Name = "Authority / order reference")] public string? AuthorityReference { get; set; }
        [StringLength(1200)] public string? Remarks { get; set; }
        [Required] public string RowVersion { get; set; } = string.Empty;
    }
}
