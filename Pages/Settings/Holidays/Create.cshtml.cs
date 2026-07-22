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
public sealed class CreateModel : PageModel
{
    private readonly IHolidayAdminService _holidays;
    private readonly IAdminTimeService _time;

    public CreateModel(IHolidayAdminService holidays, IAdminTimeService time)
    {
        _holidays = holidays ?? throw new ArgumentNullException(nameof(holidays));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet(int? year)
    {
        var selectedYear = year is >= 1900 and <= 9999 ? year.Value : _time.TodayIst.Year;
        var today = _time.TodayIst;
        Input.Date = today.Year == selectedYear ? today : new DateOnly(selectedYear, 1, 1);
        Input.Type = HolidayType.Gazetted;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _holidays.CreateAsync(
            Input.Date,
            Input.Name,
            Input.Type,
            Input.AuthorityReference,
            Input.Remarks,
            cancellationToken);
        if (!result.Succeeded)
        {
            AddResultError(result);
            return Page();
        }

        TempData[FlashMessageKeys.AdminHolidaysSuccess] = result.UserMessage;
        return RedirectToPage("Index", new { year = Input.Date.Year });
    }

    private void AddResultError(AdminOperationResult<int> result)
    {
        var message = !string.IsNullOrWhiteSpace(result.TraceId)
            ? $"{result.UserMessage} Trace reference: {result.TraceId}."
            : result.UserMessage ?? "The holiday entry could not be created.";

        if (result.ErrorCode is "DuplicateHoliday" or "GazettedDateAlreadyExists")
        {
            ModelState.AddModelError("Input.Date", message);
        }
        else
        {
            ModelState.AddModelError(string.Empty, message);
        }
    }

    public sealed class InputModel
    {
        [Required, DataType(DataType.Date)]
        public DateOnly Date { get; set; }

        [Required, StringLength(160)]
        [Display(Name = "Holiday name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Classification")]
        public HolidayType Type { get; set; } = HolidayType.Gazetted;

        [StringLength(240)]
        [Display(Name = "Authority / order reference")]
        public string? AuthorityReference { get; set; }

        [StringLength(1200)]
        public string? Remarks { get; set; }
    }
}
