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
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Training;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewTrainingTracker)]
[ValidateAntiForgeryToken]
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

    public TrainingDeleteRequestSummary? PendingDeleteRequest { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? id, CancellationToken cancellationToken)
    {
        IsFeatureEnabled = _options.Value.Enabled;
        TrainingId = id;

        await LoadOptionsAsync(cancellationToken);

        if (!IsFeatureEnabled)
        {
            PendingDeleteRequest = null;
            return Page();
        }

        if (!id.HasValue)
        {
            Input = InputModel.CreateDefault();
            PendingDeleteRequest = null;
            return Page();
        }

        var existing = await _readService.GetEditorAsync(id.Value, cancellationToken);
        if (existing is null)
        {
            TempData["ToastError"] = "The requested training could not be found.";
            return RedirectToPage("./Index");
        }

        Input = InputModel.FromEditor(existing);
        ApplyPendingDeleteRequest(existing.PendingDeleteRequest);
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
            if (Input.Id.HasValue)
            {
                await RefreshRosterAsync(Input.Id.Value, cancellationToken);
            }
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

    public async Task<IActionResult> OnPostUpsertRosterAsync([FromBody] UpsertRosterRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { ok = false, message = "An empty request was received." });
        }

        if (request.TrainingId == Guid.Empty)
        {
            return BadRequest(new { ok = false, message = "A valid training identifier is required." });
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new UnauthorizedObjectResult(new { ok = false, message = "The current user could not be identified." });
        }

        var expectedRowVersion = DecodeRowVersion(request.RowVersion);

        var rosterRows = request.Rows ?? new List<TrainingRosterRow>();

        var result = await _writeService.UpsertRosterAsync(
            request.TrainingId,
            rosterRows,
            expectedRowVersion,
            userId,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.FailureCode switch
            {
                TrainingRosterFailureCode.TrainingNotFound => NotFound(new { ok = false, message = result.ErrorMessage ?? "The training could not be found." }),
                TrainingRosterFailureCode.ConcurrencyConflict => new ConflictObjectResult(new { ok = false, message = result.ErrorMessage ?? "Another user has updated this training. Reload and try again." }),
                TrainingRosterFailureCode.MissingUserId => new UnauthorizedObjectResult(new { ok = false, message = result.ErrorMessage ?? "The current user context is missing." }),
                TrainingRosterFailureCode.DuplicateArmyNumber => BadRequest(new { ok = false, message = result.ErrorMessage ?? "Each Army number must be unique." }),
                TrainingRosterFailureCode.InvalidRequest => BadRequest(new { ok = false, message = result.ErrorMessage ?? "The roster request was invalid." }),
                _ => BadRequest(new { ok = false, message = result.ErrorMessage ?? "The roster could not be saved." })
            };
        }

        var rowVersion = result.RowVersion is { Length: > 0 }
            ? Convert.ToBase64String(result.RowVersion)
            : string.Empty;

        var counters = result.Counters ?? new TrainingRosterCounters(0, 0, 0, 0, TrainingCounterSource.Legacy);

        return new JsonResult(new
        {
            ok = true,
            rowVersion,
            counters = new
            {
                officers = counters.Officers,
                jcos = counters.JuniorCommissionedOfficers,
                ors = counters.OtherRanks,
                total = counters.Total,
                source = counters.Source.ToString()
            },
            roster = result.Roster.Select(row => new
            {
                id = row.Id,
                armyNumber = row.ArmyNumber,
                rank = row.Rank,
                name = row.Name,
                unitName = row.UnitName,
                category = row.Category
            })
        });
    }

    public async Task<IActionResult> OnPostRequestDeleteAsync(DeleteRequestForm form, CancellationToken cancellationToken)
    {
        if (form is null || form.TrainingId == Guid.Empty)
        {
            TempData["ToastError"] = "The training could not be identified.";
            return RedirectToPage("./Manage", new { id = form?.TrainingId ?? Guid.Empty });
        }

        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            TempData["ToastError"] = "Provide a reason for the delete request.";
            return RedirectToPage("./Manage", new { id = form.TrainingId });
        }

        var expectedRowVersion = DecodeRowVersion(form.RowVersion);
        if (expectedRowVersion is null)
        {
            TempData["ToastError"] = "We could not verify the training details. Reload and try again.";
            return RedirectToPage("./Manage", new { id = form.TrainingId });
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            TempData["ToastError"] = "You are not signed in or your session has expired.";
            return RedirectToPage("./Manage", new { id = form.TrainingId });
        }

        var result = await _writeService.RequestDeleteAsync(
            form.TrainingId,
            form.Reason,
            expectedRowVersion,
            userId,
            cancellationToken);

        if (!result.IsSuccess)
        {
            var message = result.FailureCode switch
            {
                TrainingDeleteFailureCode.TrainingNotFound => "The training could not be found.",
                TrainingDeleteFailureCode.ConcurrencyConflict => "The training was updated by another user. Reload and try again.",
                TrainingDeleteFailureCode.PendingRequestExists => "A delete request is already pending for this training.",
                TrainingDeleteFailureCode.InvalidReason => "Provide a reason for the delete request.",
                TrainingDeleteFailureCode.MissingUserId => "You are not signed in or your session has expired.",
                _ => result.ErrorMessage ?? "The delete request could not be submitted."
            };

            TempData["ToastError"] = message;
        }
        else
        {
            TempData["ToastMessage"] = "Delete request submitted for approval.";
        }

        return RedirectToPage("./Manage", new { id = form.TrainingId });
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
                ApplyRosterMetadata(Input, refreshed);
                ApplyPendingDeleteRequest(refreshed.PendingDeleteRequest);
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

    private async Task RefreshRosterAsync(Guid trainingId, CancellationToken cancellationToken)
    {
        var editor = await _readService.GetEditorAsync(trainingId, cancellationToken);
        if (editor is null)
        {
            return;
        }

        Input ??= InputModel.CreateDefault();
        ApplyRosterMetadata(Input, editor);
        ApplyPendingDeleteRequest(editor.PendingDeleteRequest);
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

    private static void ApplyRosterMetadata(InputModel model, TrainingEditorData editor)
    {
        model.Roster = CloneRoster(editor.Roster);
        model.HasRoster = editor.HasRoster;
        model.CounterOfficers = editor.CounterOfficers;
        model.CounterJcos = editor.CounterJcos;
        model.CounterOrs = editor.CounterOrs;
        model.CounterTotal = editor.CounterTotal;
        model.CounterSource = editor.CounterSource;
    }

    private void ApplyPendingDeleteRequest(TrainingDeleteRequestSummary? summary)
    {
        PendingDeleteRequest = summary is { Status: TrainingDeleteRequestStatus.Pending }
            ? summary
            : null;
    }

    private static List<TrainingRosterRow> CloneRoster(IReadOnlyList<TrainingRosterRow> roster)
    {
        if (roster is null || roster.Count == 0)
        {
            return new List<TrainingRosterRow>();
        }

        return roster
            .Select(row => new TrainingRosterRow
            {
                Id = row.Id,
                ArmyNumber = row.ArmyNumber,
                Rank = row.Rank,
                Name = row.Name,
                UnitName = row.UnitName,
                Category = row.Category
            })
            .ToList();
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

        if (!input.HasRoster && input.LegacyOfficerCount + input.LegacyJcoCount + input.LegacyOrCount <= 0)
        {
            ModelState.AddModelError(string.Empty, "Enter at least one attendee in the legacy counts.");
        }

        if (input.ProjectIds is null)
        {
            input.ProjectIds = new List<int>();
        }
    }

    public sealed class UpsertRosterRequest
    {
        public Guid TrainingId { get; set; }

        public string? RowVersion { get; set; }

        public List<TrainingRosterRow> Rows { get; set; } = new();
    }

    public sealed class DeleteRequestForm
    {
        public Guid TrainingId { get; set; }

        public string Reason { get; set; } = string.Empty;

        public string RowVersion { get; set; } = string.Empty;
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

        public List<TrainingRosterRow> Roster { get; set; } = new();

        public bool HasRoster { get; set; }

        public int CounterOfficers { get; set; }

        public int CounterJcos { get; set; }

        public int CounterOrs { get; set; }

        public int CounterTotal { get; set; }

        public TrainingCounterSource CounterSource { get; set; } = TrainingCounterSource.Legacy;

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
                ScheduleMode = TrainingScheduleMode.DateRange,
                Roster = new List<TrainingRosterRow>(),
                HasRoster = false,
                CounterOfficers = 0,
                CounterJcos = 0,
                CounterOrs = 0,
                CounterTotal = 0,
                CounterSource = TrainingCounterSource.Legacy
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

            ApplyRosterMetadata(model, editor);

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
