using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewTrainingTracker)]
public class ManageModel : PageModel
{
    private readonly IOptionsSnapshot<TrainingTrackerOptions> _options;
    private readonly TrainingTrackerReadService _readService;
    private readonly TrainingWriteService _writeService;
    private readonly IUserContext _userContext;

    public ManageModel(
        IOptionsSnapshot<TrainingTrackerOptions> options,
        TrainingTrackerReadService readService,
        TrainingWriteService writeService,
        IUserContext userContext)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _writeService = writeService ?? throw new ArgumentNullException(nameof(writeService));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    public bool IsFeatureEnabled { get; private set; }

    public Guid? TrainingId { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = InputModel.CreateDefault();

    public IReadOnlyList<SelectListItem> TrainingTypes { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> ProjectOptions { get; private set; } = Array.Empty<SelectListItem>();

    public async Task<IActionResult> OnGetAsync(Guid? id, CancellationToken cancellationToken)
    {
        IsFeatureEnabled = _options.Value.Enabled;
        TrainingId = id;

        await LoadOptionsAsync(cancellationToken);

        if (!IsFeatureEnabled)
        {
            return Page();
        }

        if (!id.HasValue)
        {
            Input = InputModel.CreateDefault();
            return Page();
        }

        var existing = await _readService.GetEditorAsync(id.Value, cancellationToken);
        if (existing is null)
        {
            TempData["ToastError"] = "The requested training could not be found.";
            return RedirectToPage("./Index");
        }

        Input = InputModel.FromEditor(existing);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        IsFeatureEnabled = _options.Value.Enabled;
        TrainingId = Input?.Id;

        if (!IsFeatureEnabled)
        {
            ModelState.AddModelError(string.Empty, "The training tracker is currently disabled.");
        }

        if (Input is null)
        {
            Input = InputModel.CreateDefault();
        }

        ValidateInput(Input);

        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync(cancellationToken);
            return Page();
        }

        var command = Input.ToCommand();
        var userId = _userContext.UserId;

        TrainingMutationResult result;
        if (Input.Id.HasValue)
        {
            var expectedRowVersion = DecodeRowVersion(Input.RowVersion);
            if (expectedRowVersion is null)
            {
                ModelState.AddModelError(string.Empty, "The record could not be updated because its version is missing.");
                await LoadOptionsAsync(cancellationToken);
                return Page();
            }

            result = await _writeService.UpdateAsync(Input.Id.Value, command, expectedRowVersion, userId ?? string.Empty, cancellationToken);
        }
        else
        {
            result = await _writeService.CreateAsync(command, userId ?? string.Empty, cancellationToken);
        }

        if (!result.IsSuccess)
        {
            await HandleFailureAsync(result, cancellationToken);
            return Page();
        }

        TempData["ToastMessage"] = Input.Id.HasValue ? "Training updated." : "Training created.";
        return RedirectToPage("./Manage", new { id = result.TrainingId });
    }

    private async Task HandleFailureAsync(TrainingMutationResult result, CancellationToken cancellationToken)
    {
        switch (result.FailureCode)
        {
            case TrainingMutationFailureCode.TrainingTypeNotFound:
                ModelState.AddModelError(nameof(Input.TrainingTypeId), "Select a training type.");
                break;
            case TrainingMutationFailureCode.TrainingTypeInactive:
                ModelState.AddModelError(nameof(Input.TrainingTypeId), "The selected training type is inactive.");
                break;
            case TrainingMutationFailureCode.InvalidProjects:
                ModelState.AddModelError(nameof(Input.ProjectIds), result.ErrorMessage ?? "One or more selected projects are invalid.");
                break;
            case TrainingMutationFailureCode.TrainingNotFound:
                ModelState.AddModelError(string.Empty, "The training could not be found.");
                break;
            case TrainingMutationFailureCode.ConcurrencyConflict:
                ModelState.AddModelError(string.Empty, "Another user has updated this training. Reload and try again.");
                break;
            case TrainingMutationFailureCode.MissingUserId:
                ModelState.AddModelError(string.Empty, "The current user is not recognized.");
                break;
            default:
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    ModelState.AddModelError(string.Empty, result.ErrorMessage);
                }
                break;
        }

        if (Input.Id.HasValue)
        {
            var refreshed = await _readService.GetEditorAsync(Input.Id.Value, cancellationToken);
            if (refreshed is not null)
            {
                Input.RowVersion = Convert.ToBase64String(refreshed.RowVersion);
            }
        }

        await LoadOptionsAsync(cancellationToken);
    }

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        TrainingTypes = (await _readService.GetTrainingTypesAsync(cancellationToken))
            .Select(option => new SelectListItem(option.Name, option.Id.ToString()))
            .ToList();

        ProjectOptions = (await _readService.GetProjectOptionsAsync(cancellationToken))
            .Select(option => new SelectListItem(option.Name, option.Id.ToString()))
            .ToList();
    }

    private static byte[]? DecodeRowVersion(string? value)
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

    private void ValidateInput(InputModel input)
    {
        if (input.TrainingTypeId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(Input.TrainingTypeId), "Select a training type.");
        }

        if (input.ScheduleMode == TrainingScheduleMode.DateRange)
        {
            if (!input.StartDate.HasValue)
            {
                ModelState.AddModelError(nameof(Input.StartDate), "Enter a start date.");
            }

            if (!input.EndDate.HasValue)
            {
                ModelState.AddModelError(nameof(Input.EndDate), "Enter an end date.");
            }

            if (input.StartDate.HasValue && input.EndDate.HasValue && input.EndDate.Value < input.StartDate.Value)
            {
                ModelState.AddModelError(nameof(Input.EndDate), "End date cannot be earlier than the start date.");
            }

            input.TrainingMonth = null;
            input.TrainingYear = null;
        }
        else if (input.ScheduleMode == TrainingScheduleMode.MonthAndYear)
        {
            if (!input.TrainingMonth.HasValue)
            {
                ModelState.AddModelError(nameof(Input.TrainingMonth), "Select a month.");
            }
            else if (input.TrainingMonth.Value is < 1 or > 12)
            {
                ModelState.AddModelError(nameof(Input.TrainingMonth), "Month must be between 1 and 12.");
            }

            if (!input.TrainingYear.HasValue)
            {
                ModelState.AddModelError(nameof(Input.TrainingYear), "Enter a year.");
            }
            else if (input.TrainingYear.Value is < 2000 or > 2100)
            {
                ModelState.AddModelError(nameof(Input.TrainingYear), "Year must be between 2000 and 2100.");
            }

            input.StartDate = null;
            input.EndDate = null;
        }
        else
        {
            ModelState.AddModelError(nameof(Input.ScheduleMode), "Select how the training period should be captured.");
        }

        if (input.LegacyOfficerCount + input.LegacyJcoCount + input.LegacyOrCount <= 0)
        {
            ModelState.AddModelError(string.Empty, "Enter at least one attendee in the legacy counts.");
        }

        if (input.ProjectIds is null)
        {
            input.ProjectIds = new List<int>();
        }
    }

    public enum TrainingScheduleMode
    {
        DateRange = 0,
        MonthAndYear = 1
    }

    public sealed class InputModel
    {
        public Guid? Id { get; set; }

        [Display(Name = "Training type")]
        public Guid TrainingTypeId { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Start date")]
        public DateOnly? StartDate { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "End date")]
        public DateOnly? EndDate { get; set; }

        [Range(1, 12)]
        [Display(Name = "Month")]
        public int? TrainingMonth { get; set; }

        [Range(2000, 2100)]
        [Display(Name = "Year")]
        public int? TrainingYear { get; set; }

        [Range(0, int.MaxValue)]
        [Display(Name = "Officers")]
        public int LegacyOfficerCount { get; set; }

        [Range(0, int.MaxValue)]
        [Display(Name = "JCOs")]
        public int LegacyJcoCount { get; set; }

        [Range(0, int.MaxValue)]
        [Display(Name = "ORs")]
        public int LegacyOrCount { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public List<int> ProjectIds { get; set; } = new();

        public string? RowVersion { get; set; }

        public TrainingScheduleMode ScheduleMode { get; set; } = TrainingScheduleMode.DateRange;

        public TrainingMutationCommand ToCommand()
        {
            return new TrainingMutationCommand(
                TrainingTypeId,
                ScheduleMode == TrainingScheduleMode.DateRange ? StartDate : null,
                ScheduleMode == TrainingScheduleMode.DateRange ? EndDate : null,
                ScheduleMode == TrainingScheduleMode.MonthAndYear ? TrainingMonth : null,
                ScheduleMode == TrainingScheduleMode.MonthAndYear ? TrainingYear : null,
                LegacyOfficerCount,
                LegacyJcoCount,
                LegacyOrCount,
                Notes,
                ProjectIds);
        }

        public static InputModel CreateDefault()
        {
            return new InputModel
            {
                LegacyOfficerCount = 0,
                LegacyJcoCount = 0,
                LegacyOrCount = 0,
                ProjectIds = new List<int>(),
                ScheduleMode = TrainingScheduleMode.DateRange
            };
        }

        public static InputModel FromEditor(TrainingEditorData editor)
        {
            if (editor is null)
            {
                throw new ArgumentNullException(nameof(editor));
            }

            var model = new InputModel
            {
                Id = editor.Id,
                TrainingTypeId = editor.TrainingTypeId,
                StartDate = editor.StartDate,
                EndDate = editor.EndDate,
                TrainingMonth = editor.TrainingMonth,
                TrainingYear = editor.TrainingYear,
                LegacyOfficerCount = editor.LegacyOfficerCount,
                LegacyJcoCount = editor.LegacyJcoCount,
                LegacyOrCount = editor.LegacyOrCount,
                Notes = editor.Notes,
                ProjectIds = editor.ProjectIds.ToList(),
                RowVersion = Convert.ToBase64String(editor.RowVersion)
            };

            if (editor.StartDate.HasValue || editor.EndDate.HasValue)
            {
                model.ScheduleMode = TrainingScheduleMode.DateRange;
            }
            else if (editor.TrainingMonth.HasValue && editor.TrainingYear.HasValue)
            {
                model.ScheduleMode = TrainingScheduleMode.MonthAndYear;
            }

            return model;
        }
    }
}
