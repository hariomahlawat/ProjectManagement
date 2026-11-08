using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ProjectManagement.Configuration;
using ProjectManagement.Infrastructure.Ui;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Services;
using ProjectManagement.Services.ItProjects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Pages.ItProjects;

/// <summary>
/// One IT project: header, stepper, description, and for Manage users the stage editor, the
/// details editor, Dropped / Reinstate and Delete (docs/ux/it-projects.md, "The project page").
/// Remarks (PR 4) and attachments (PR 5) join this page later.
/// </summary>
[Authorize(Policy = Policies.ItProjects.View)]
public class DetailsModel : PageModel
{
    public const string OpenStages = "stages";
    public const string OpenEdit = "edit";
    public const string OpenDrop = "drop";

    private readonly IItProjectReadService _reads;
    private readonly IItProjectCommandService _commands;
    private readonly IClock _clock;

    public DetailsModel(IItProjectReadService reads, IItProjectCommandService commands, IClock clock)
    {
        _reads = reads ?? throw new ArgumentNullException(nameof(reads));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Which drawer opens on load: stages, edit or drop.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Open { get; set; }

    public ItProject Project { get; private set; } = null!;

    public ItProjectStatus Status { get; private set; } = new(ItProjectStatusKind.NotStarted, ItProjectStages.Ppp);

    public IReadOnlyList<StageRow> StageRows { get; private set; } = Array.Empty<StageRow>();

    public bool CanManage { get; private set; }

    public string? UpdatedByName { get; private set; }

    public IReadOnlyList<SelectListItem> FinancialYearOptions { get; private set; } = Array.Empty<SelectListItem>();

    [BindProperty]
    public DetailsInput Details { get; set; } = new();

    [BindProperty]
    public StagesInput Stages { get; set; } = new();

    [BindProperty]
    public DropInput Drop { get; set; } = new();

    /// <summary>Row version for the Reinstate and Delete buttons.</summary>
    [BindProperty]
    public string? RowVersion { get; set; }

    public sealed record StageRow(
        string Code,
        string Label,
        bool IsOptional,
        bool CarriesCost,
        bool IsCompleted,
        DateOnly? CompletedOn,
        bool IsSkipped,
        decimal? CostLakhs,
        bool IsCurrent);

    public sealed class DetailsInput
    {
        [Required(ErrorMessage = "Give the project a title.")]
        [StringLength(ItProjectCommandService.TitleMaxLength, ErrorMessage = "The title must be {1} characters or fewer.")]
        [Display(Name = "Title")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Give the IT PPP reference number.")]
        [StringLength(ItProjectCommandService.ReferenceMaxLength, ErrorMessage = "The IT PPP reference number must be {1} characters or fewer.")]
        [Display(Name = "IT PPP reference number")]
        public string PppReferenceNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Choose the financial year.")]
        [Display(Name = "Financial year")]
        public int? FinancialYearStart { get; set; }

        [Required(ErrorMessage = "Give the project a description.")]
        [Display(Name = "Description")]
        public string Description { get; set; } = string.Empty;

        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class StagesInput
    {
        public string RowVersion { get; set; } = string.Empty;

        public List<StageItemInput> Items { get; set; } = new();
    }

    public sealed class StageItemInput
    {
        public string StageCode { get; set; } = string.Empty;

        public bool IsCompleted { get; set; }

        [DataType(DataType.Date)]
        public DateOnly? CompletedOn { get; set; }

        /// <summary>Optional stages only: true means the stage is part of this project's chain.</summary>
        public bool IsIncluded { get; set; } = true;

        [Range(0, 99_999_999.99, ErrorMessage = "The SO cost cannot be negative.")]
        public decimal? CostLakhs { get; set; }
    }

    public sealed class DropInput
    {
        [Required(ErrorMessage = "Give the date the project was dropped.")]
        [DataType(DataType.Date)]
        [Display(Name = "Dropped on")]
        public DateOnly? DroppedOn { get; set; }

        [Required(ErrorMessage = "Give the reason the project was dropped.")]
        [StringLength(ItProjectCommandService.DroppedReasonMaxLength, ErrorMessage = "The reason must be {1} characters or fewer.")]
        [Display(Name = "Reason")]
        public string Reason { get; set; } = string.Empty;

        public string RowVersion { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanManage && Open is not null)
        {
            Open = null;
        }

        FillInputsFromProject();
        return Page();
    }

    public async Task<IActionResult> OnPostDetailsAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanManage)
        {
            return Forbid();
        }

        var yearIsValid = Details.FinancialYearStart is { } year && CreateModel.IsSupportedFinancialYear(year);
        if (Details.FinancialYearStart.HasValue && !yearIsValid)
        {
            ModelState.AddModelError($"{nameof(Details)}.{nameof(Details.FinancialYearStart)}", "Choose the financial year.");
        }

        FinancialYearOptions = CreateModel.BuildFinancialYearOptions(yearIsValid ? Details.FinancialYearStart!.Value : Project.FinancialYearStart);
        ModelState.Remove($"{nameof(Stages)}");
        if (!IsSectionValid(nameof(Details)))
        {
            Open = OpenEdit;
            FillStagesInput();
            return Page();
        }

        var result = await _commands.UpdateDetailsAsync(
            id,
            new ItProjectDetailsInput(Details.Title, Details.PppReferenceNumber, Details.FinancialYearStart!.Value, Details.Description),
            DecodeRowVersion(Details.RowVersion),
            Actor(),
            cancellationToken);

        if (!result.Succeeded)
        {
            return Failed(result, OpenEdit);
        }

        TempData.ToastSuccess("Project details saved.");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostStagesAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanManage)
        {
            return Forbid();
        }

        var inputs = Stages.Items
            .Select(item => new ItProjectStageInput(
                item.StageCode,
                item.IsCompleted,
                item.CompletedOn,
                IsSkipped: ItProjectStages.IsKnown(item.StageCode) && ItProjectStages.IsOptional(item.StageCode) && !item.IsIncluded,
                item.CostLakhs))
            .ToList();

        var save = await _commands.SaveStagesAsync(id, inputs, DecodeRowVersion(Stages.RowVersion), Actor(), cancellationToken);
        if (!save.Result.Succeeded)
        {
            return Failed(save.Result, OpenStages);
        }

        TempData.ToastSuccess(StagesSavedText(save));
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDropAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanManage)
        {
            return Forbid();
        }

        if (!IsSectionValid(nameof(Drop)))
        {
            Open = OpenDrop;
            FillInputsFromProject(keepDrop: true);
            return Page();
        }

        var result = await _commands.DropAsync(id, Drop.DroppedOn!.Value, Drop.Reason, DecodeRowVersion(Drop.RowVersion), Actor(), cancellationToken);
        if (!result.Succeeded)
        {
            return Failed(result, OpenDrop);
        }

        TempData.ToastSuccess("Project marked dropped. It stays on the board for the record.");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostReinstateAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanManage)
        {
            return Forbid();
        }

        var result = await _commands.ReinstateAsync(id, DecodeRowVersion(RowVersion), Actor(), cancellationToken);
        if (!result.Succeeded)
        {
            return Failed(result, null);
        }

        TempData.ToastSuccess("Project reinstated.");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanManage)
        {
            return Forbid();
        }

        var title = Project.Title;
        var result = await _commands.DeleteAsync(id, DecodeRowVersion(RowVersion), Actor(), cancellationToken);
        if (!result.Succeeded)
        {
            return Failed(result, null);
        }

        TempData.ToastSuccess($"IT project \"{title}\" deleted.");
        return RedirectToPage("./Index");
    }

    // ---- helpers ---------------------------------------------------------------------------

    public string RowVersionToken => EncodeRowVersion(Project.RowVersion);

    public bool IsDropped => Project.DroppedOn.HasValue;

    public bool IsCompleted => Status.Kind == ItProjectStatusKind.Completed;

    private async Task<bool> LoadAsync(int id, CancellationToken cancellationToken)
    {
        var project = await _reads.GetAsync(id, cancellationToken);
        if (project is null)
        {
            return false;
        }

        Project = project;
        Status = ItProjectStatus.Resolve(project);
        CanManage = ItProjectAccess.CanManage(User, project);
        var active = Status.Kind is ItProjectStatusKind.NotStarted or ItProjectStatusKind.InProgress;
        var byCode = project.Stages.ToDictionary(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase);
        StageRows = ItProjectStages.All
            .Select(definition =>
            {
                byCode.TryGetValue(definition.Code, out var row);
                return new StageRow(
                    definition.Code,
                    definition.Label,
                    definition.IsOptional,
                    definition.CarriesCost,
                    row?.IsCompleted ?? false,
                    row?.CompletedOn,
                    row?.IsSkipped ?? definition.IsOptional,
                    row?.CostLakhs,
                    IsCurrent: active && Status.CurrentStageCode == definition.Code);
            })
            .ToList();
        UpdatedByName = project.UpdatedByUserId is { } userId
            ? await _reads.GetUserDisplayNameAsync(userId, cancellationToken)
            : null;
        return true;
    }

    private void FillInputsFromProject(bool keepDrop = false)
    {
        FinancialYearOptions = CreateModel.BuildFinancialYearOptions(
            CreateModel.IsSupportedFinancialYear(Project.FinancialYearStart) ? Project.FinancialYearStart : CurrentFinancialYearStart());
        Details = new DetailsInput
        {
            Title = Project.Title,
            PppReferenceNumber = Project.PppReferenceNumber,
            FinancialYearStart = Project.FinancialYearStart,
            Description = Project.Description,
            RowVersion = RowVersionToken
        };
        FillStagesInput();
        if (!keepDrop)
        {
            Drop = new DropInput
            {
                DroppedOn = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
                RowVersion = RowVersionToken
            };
        }

        RowVersion = RowVersionToken;
    }

    private void FillStagesInput()
    {
        Stages = new StagesInput
        {
            RowVersion = RowVersionToken,
            Items = StageRows.Select(row => new StageItemInput
            {
                StageCode = row.Code,
                IsCompleted = row.IsCompleted,
                CompletedOn = row.CompletedOn,
                IsIncluded = !row.IsSkipped,
                CostLakhs = row.CostLakhs
            }).ToList()
        };
    }

    private bool IsSectionValid(string prefix)
        => ModelState.Where(entry => entry.Key.StartsWith(prefix + ".", StringComparison.Ordinal) || entry.Key == prefix)
            .All(entry => entry.Value?.Errors.Count == 0);

    private IActionResult Failed(ItProjectCommandResult result, string? open)
    {
        if (result.ConcurrencyConflict)
        {
            TempData.ToastError(ItProjectCommandService.ConcurrencyConflictMessage);
            return RedirectToPage(new { id = Project.Id });
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(open is null ? string.Empty : open, error);
        }

        Open = open;
        var keepDrop = open == OpenDrop;
        var postedDetails = Details;
        var postedStages = Stages;
        FillInputsFromProject(keepDrop);
        if (open == OpenEdit)
        {
            Details = postedDetails;
        }
        else if (open == OpenStages)
        {
            Stages = postedStages;
        }

        return Page();
    }

    private static string StagesSavedText(ItProjectStageSaveResult save)
    {
        if (save.AutoCompletedStageCodes.Count > 0)
        {
            return $"Stages saved. Completed: {string.Join(", ", save.AutoCompletedStageCodes.Select(ItProjectStages.LabelOf))}.";
        }

        if (save.ReopenedStageCodes.Count > 0)
        {
            return $"Stages saved. Reopened: {string.Join(", ", save.ReopenedStageCodes.Select(ItProjectStages.LabelOf))}.";
        }

        return "Stages saved.";
    }

    private ItProjectActor Actor()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("The signed-in user has no id.");
        }

        return new ItProjectActor(userId, User.Identity?.Name);
    }

    private int CurrentFinancialYearStart()
        => FinancialYearHelper.GetStartYear(DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime));

    internal static string EncodeRowVersion(byte[]? value)
        => value is { Length: > 0 } ? Convert.ToBase64String(value) : string.Empty;

    internal static byte[] DecodeRowVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<byte>();
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }
}
