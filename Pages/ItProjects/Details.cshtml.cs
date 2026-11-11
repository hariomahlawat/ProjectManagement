using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
/// Remarks and attachments have their own cards (PR 4 and PR 5).
/// </summary>
[Authorize(Policy = Policies.ItProjects.View)]
public class DetailsModel : PageModel
{
    public const string OpenStages = "stages";
    public const string OpenEdit = "edit";
    public const string OpenDrop = "drop";

    public const string OpenRemark = "remark";

    /// <summary>Fragment of the attachments card, where attachment posts return.</summary>
    public const string AttachmentsFragment = "attachments";

    private readonly IItProjectReadService _reads;
    private readonly IItProjectCommandService _commands;
    private readonly IItProjectRemarkService _remarks;
    private readonly IItProjectAttachmentService _attachments;
    private readonly IClock _clock;

    public DetailsModel(IItProjectReadService reads, IItProjectCommandService commands, IItProjectRemarkService remarks, IItProjectAttachmentService attachments, IClock clock)
    {
        _attachments = attachments ?? throw new ArgumentNullException(nameof(attachments));
        _reads = reads ?? throw new ArgumentNullException(nameof(reads));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _remarks = remarks ?? throw new ArgumentNullException(nameof(remarks));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public IReadOnlyList<ItProjectRemarkView> Remarks { get; private set; } = Array.Empty<ItProjectRemarkView>();

    public IReadOnlyList<ItProjectAttachmentView> Attachments { get; private set; } = Array.Empty<ItProjectAttachmentView>();

    /// <summary>The time facts of the project (days at the stage, elapsed), from the read service.</summary>
    public ItProjectTiming? Timing { get; private set; }

    /// <summary>Remarks and stage completions, newest first.</summary>
    public IReadOnlyList<ItProjectPagePresentation.FeedEntry> Feed { get; private set; } = Array.Empty<ItProjectPagePresentation.FeedEntry>();

    /// <summary>The nine stops of the Journey card.</summary>
    public IReadOnlyList<ItProjectPagePresentation.JourneyStep> Journey { get; private set; } = Array.Empty<ItProjectPagePresentation.JourneyStep>();

    /// <summary>The status line under the title.</summary>
    public ItProjectPagePresentation.HeroStatus? HeroStatus { get; private set; }

    /// <summary>The project's type and IT ARPPP number in the latest year it counts in.</summary>
    public ItProjectYearTypeInfo? LatestYear => YearTypes.LastOrDefault();

    /// <summary>New / CL / CF in every financial year the project counts in, oldest first.</summary>
    public IReadOnlyList<ItProjectYearTypeInfo> YearTypes { get; private set; } = Array.Empty<ItProjectYearTypeInfo>();

    /// <summary>The file chosen in the attachments card.</summary>
    [BindProperty]
    public IFormFile? AttachmentFile { get; set; }

    /// <summary>The upload limit in whole megabytes, for the card's hint.</summary>
    public long AttachmentMaxMb => _attachments.MaxSizeBytes / (1024 * 1024);

    /// <summary>True when the signed-in user may write remarks (ITO, MCO, HoD, Comdt).</summary>
    public bool CanRemark { get; private set; }

    /// <summary>The role a new remark would be written as, or null.</summary>
    public string? RemarkRole { get; private set; }

    public bool IsAdmin { get; private set; }

    [BindProperty]
    public RemarkInput Remark { get; set; } = new();

    [BindProperty]
    public RemarkEditInput RemarkEdit { get; set; } = new();

    public sealed class RemarkInput
    {
        [Required(ErrorMessage = "Write the remark.")]
        [StringLength(ItProjectRemark.BodyMaxLength, MinimumLength = ItProjectRemarkService.MinBodyLength, ErrorMessage = "The remark must be between {2} and {1} characters.")]
        [Display(Name = "Remark")]
        public string Body { get; set; } = string.Empty;

        [Required(ErrorMessage = "Give the date the remark is about.")]
        [DataType(DataType.Date)]
        [Display(Name = "Event date")]
        public DateOnly? EventDate { get; set; }

        /// <summary>"Conference" for a conference remark (Comdt / HoD); anything else is a general remark.</summary>
        public string? Type { get; set; }

        public ItProjectRemarkType RemarkType => string.Equals(Type, "Conference", StringComparison.OrdinalIgnoreCase)
            ? ItProjectRemarkType.Conference
            : ItProjectRemarkType.General;
    }

    public sealed class RemarkEditInput
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Write the remark.")]
        [StringLength(ItProjectRemark.BodyMaxLength, MinimumLength = ItProjectRemarkService.MinBodyLength, ErrorMessage = "The remark must be between {2} and {1} characters.")]
        public string Body { get; set; } = string.Empty;

        [Required(ErrorMessage = "Give the date the remark is about.")]
        [DataType(DataType.Date)]
        public DateOnly? EventDate { get; set; }

        public string RowVersion { get; set; } = string.Empty;
    }

    /// <summary>Whether the user may write conference remarks: the Comdt or HoD, as in the other modules.</summary>
    public bool CanWriteConference => CanRemark && ItProjectRemarkService.CanWriteConference(RemarkRole);

    /// <summary>Who may edit a remark: its author (a conference remark only while they are the Comdt or HoD).</summary>
    public bool CanEditRemark(ItProjectRemarkView remark)
        => CanRemark && remark.AuthorUserId == UserId && (remark.Type != ItProjectRemarkType.Conference || CanWriteConference);

    /// <summary>Who may delete a remark: its author (as for editing), or an Admin as moderation.</summary>
    /// <summary>Whether the user holds the HoD role (whatever role they write remarks as).</summary>
    public bool IsHoD { get; private set; }

    /// <summary>
    /// Admin and HoD delete any remark; everyone else, the Comdt included, their own. The service
    /// applies the same rule (<see cref="ItProjectRemarkService"/>); this only decides the link.
    /// </summary>
    public bool CanDeleteRemark(ItProjectRemarkView remark) => IsAdmin || IsHoD || CanEditRemark(remark);

    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

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

    /// <summary>
    /// The reopen gestures a failed stage save carried, put back into the editor's hidden field so
    /// the user's intent survives the validation round trip.
    /// </summary>
    public string? ReopenPending { get; private set; }

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

        [Required(ErrorMessage = "Give the IT ARPPP reference number.")]
        [StringLength(ItProjectCommandService.ReferenceMaxLength, ErrorMessage = "The IT ARPPP reference number must be {1} characters or fewer.")]
        [Display(Name = "IT ARPPP reference number")]
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

        FinancialYearOptions = CreateModel.BuildFinancialYearOptions(yearIsValid ? Details.FinancialYearStart!.Value : Project.FinancialYearStart, CurrentFinancialYearStart());
        if (!IsSectionValid(nameof(Details)))
        {
            // Keep what was typed in the edit drawer, but give every other form its row version.
            var posted = Details;
            Open = OpenEdit;
            FillInputsFromProject();
            Details = posted;
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

    /// <param name="reopen">
    /// Comma list of stages the user unticked since the editor last showed a server-normalised
    /// state. Applied before the rules, so a save that arrives before (or instead of) a preview
    /// still reopens what the user asked.
    /// </param>
    public async Task<IActionResult> OnPostStagesAsync(int id, string? reopen, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanManage)
        {
            return Forbid();
        }

        if (IsDropped)
        {
            // The stage drawer is not drawn for a dropped case, so say it here.
            TempData.ToastError(ItProjectCommandService.DroppedStagesMessage);
            return RedirectToPage(new { id });
        }

        ReopenPending = string.IsNullOrWhiteSpace(reopen) ? null : reopen.Trim();
        if (!IsSectionValid(nameof(Stages)))
        {
            // A date or cost that did not bind must not be saved as "empty".
            var posted = Stages;
            Open = OpenStages;
            FillInputsFromProject();
            Stages = posted;
            return Page();
        }

        var save = await _commands.SaveStagesAsync(id, ApplyReopen(ToStageInputs(Stages.Items), reopen), DecodeRowVersion(Stages.RowVersion), Actor(), cancellationToken);
        if (!save.Result.Succeeded)
        {
            return Failed(save.Result, OpenStages);
        }

        TempData.ToastSuccess(StagesSavedText(save));
        return RedirectToPage(new { id });
    }

    /// <summary>
    /// The stage editor's live preview: the posted stages after <see cref="ItProjectStageRules"/>
    /// has run, plus what a save would also complete or reopen. The browser only renders this,
    /// so the chain is calculated in one place. <paramref name="reopen"/> is the comma list of
    /// stages the user unticked (each opens with the later ones first); <paramref name="touched"/>
    /// is a comma list of the stages the user changed directly, left out of the summary.
    /// </summary>
    public async Task<IActionResult> OnPostPreviewAsync(int id, string? reopen, string? touched, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanManage)
        {
            return Forbid();
        }

        if (IsDropped)
        {
            return new JsonResult(new { valid = false, errors = new[] { ItProjectCommandService.DroppedStagesMessage } });
        }

        if (!IsSectionValid(nameof(Stages)))
        {
            // A value that did not bind is reported, never normalised into "empty".
            return new JsonResult(new { valid = false, errors = DrawerErrors(OpenStages, nameof(Stages)) });
        }

        var rules = ItProjectStageRules.Apply(ApplyReopen(ToStageInputs(Stages.Items), reopen));
        if (!rules.IsValid)
        {
            return new JsonResult(new { valid = false, errors = rules.Errors });
        }

        var (completed, reopened) = ItProjectStageRules.Diff(Project.Stages, rules.Stages);
        var touchedCodes = (touched ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var completedElsewhere = completed.Where(code => !touchedCodes.Contains(code)).ToList();
        var reopenedElsewhere = reopened.Where(code => !touchedCodes.Contains(code)).ToList();

        return new JsonResult(new
        {
            valid = true,
            stages = rules.Stages.Select(state => new
            {
                code = state.StageCode,
                completed = state.IsCompleted,
                completedOn = state.CompletedOn?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                skipped = state.IsSkipped,
                costLakhs = state.CostLakhs
            }),
            completed = completedElsewhere,
            reopened = reopenedElsewhere,
            summary = ItProjectBoardPresentation.CascadeSummary(completedElsewhere, reopenedElsewhere)
        });
    }

    // ---- remarks -----------------------------------------------------------------------------

    public async Task<IActionResult> OnPostRemarkAddAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanRemark)
        {
            return Forbid();
        }

        if (!IsSectionValid(nameof(Remark)))
        {
            var posted = Remark;
            Open = OpenRemark;
            FillInputsFromProject();
            Remark = posted;
            return Page();
        }

        var (result, _) = await _remarks.AddAsync(
            id,
            new ItProjectRemarkInput(Remark.Body, Remark.EventDate!.Value, Remark.RemarkType),
            RemarkActor(),
            cancellationToken);

        if (!result.Succeeded)
        {
            var posted = Remark;
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(OpenRemark, error);
            }

            Open = OpenRemark;
            FillInputsFromProject();
            Remark = posted;
            return Page();
        }

        TempData.ToastSuccess("Remark added.");
        return RedirectToPage(null, null, new { id }, "remarks");
    }

    public async Task<IActionResult> OnPostRemarkEditAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanRemark)
        {
            return Forbid();
        }

        if (!IsSectionValid(nameof(RemarkEdit)))
        {
            TempData.ToastError("The remark was not saved: " + string.Join(" ", FieldErrors(nameof(RemarkEdit))));
            return RedirectToPage(null, null, new { id }, "remarks");
        }

        var result = await _remarks.EditAsync(
            id,
            RemarkEdit.Id,
            new ItProjectRemarkInput(RemarkEdit.Body, RemarkEdit.EventDate!.Value),
            DecodeRowVersion(RemarkEdit.RowVersion),
            RemarkActor(),
            cancellationToken);

        return RemarkOutcome(id, result, "Remark saved.");
    }

    public async Task<IActionResult> OnPostRemarkDeleteAsync(int id, int remarkId, string? remarkRowVersion, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanRemark && !IsAdmin && !IsHoD)
        {
            return Forbid();
        }

        var result = await _remarks.DeleteAsync(id, remarkId, DecodeRowVersion(remarkRowVersion), RemarkActor(), cancellationToken);
        return RemarkOutcome(id, result, "Remark deleted.");
    }

    private IActionResult RemarkOutcome(int id, ItProjectCommandResult result, string successText)
    {
        if (result.Succeeded)
        {
            TempData.ToastSuccess(successText);
        }
        else
        {
            TempData.ToastError(result.ConcurrencyConflict
                ? "This remark was changed by someone else. Refresh and try again."
                : string.Join(" ", result.Errors));
        }

        return RedirectToPage(null, null, new { id }, "remarks");
    }

    // ---- attachments -------------------------------------------------------------------------

    public async Task<IActionResult> OnPostAttachmentUploadAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanManage)
        {
            return Forbid();
        }

        var file = AttachmentFile ?? (Request.HasFormContentType ? Request.Form.Files.FirstOrDefault() : null);
        if (file is null)
        {
            TempData.ToastError("Choose a file to attach.");
            return RedirectToPage(null, null, new { id }, AttachmentsFragment);
        }

        var (result, _) = await _attachments.UploadAsync(id, file, Actor(), cancellationToken);
        return AttachmentOutcome(id, result, $"\"{Path.GetFileName(file.FileName)}\" attached.");
    }

    /// <summary>Sends a live attachment of this project. Any signed-in user may read it (View policy on the page).</summary>
    public async Task<IActionResult> OnGetAttachmentAsync(int id, Guid attachmentId, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var download = await _attachments.OpenAsync(id, attachmentId, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        return File(download.Content, download.ContentType, download.FileName);
    }

    public async Task<IActionResult> OnPostAttachmentDeleteAsync(int id, Guid attachmentId, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanManage)
        {
            return Forbid();
        }

        var result = await _attachments.DeleteAsync(id, attachmentId, Actor(), cancellationToken);
        return AttachmentOutcome(id, result, "File removed.");
    }

    private IActionResult AttachmentOutcome(int id, ItProjectCommandResult result, string successText)
    {
        if (result.Succeeded)
        {
            TempData.ToastSuccess(successText);
        }
        else
        {
            TempData.ToastError(string.Join(" ", result.Errors));
        }

        return RedirectToPage(null, null, new { id }, AttachmentsFragment);
    }

    private IEnumerable<string> FieldErrors(string prefix)
        => ModelState
            .Where(entry => entry.Key == prefix || entry.Key.StartsWith(prefix + ".", StringComparison.Ordinal))
            .SelectMany(entry => entry.Value?.Errors ?? new ModelErrorCollection())
            .Select(error => error.ErrorMessage)
            .Where(text => !string.IsNullOrWhiteSpace(text));

    private ItProjectRemarkActor RemarkActor()
    {
        var actor = Actor();
        return new ItProjectRemarkActor(actor.UserId, actor.UserName, RemarkRole, IsAdmin, IsHoD);
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

    public async Task<IActionResult> OnPostYearAsync(int id, int fy, string? yearType, string? yearReference, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!CanManage)
        {
            return Forbid();
        }

        ItProjectYearType? type;
        switch ((yearType ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "NEW": type = ItProjectYearType.New; break;
            case "CL": type = ItProjectYearType.CommittedLiability; break;
            case "CF": type = ItProjectYearType.CarriedForward; break;
            case "":
            case "AUTO": type = null; break;
            default:
                TempData.ToastError("Choose New, CL or CF.");
                return RedirectToPage(new { id });
        }

        var result = await _commands.SaveYearAsync(id, fy, new ItProjectYearInput(type, yearReference), DecodeRowVersion(RowVersion), Actor(), cancellationToken);
        if (!result.Succeeded)
        {
            if (result.ConcurrencyConflict)
            {
                return Failed(result, null);
            }

            TempData.ToastError(string.Join(" ", result.Errors));
            return RedirectToPage(new { id });
        }

        TempData.ToastSuccess($"FY {FinancialYearHelper.Format(fy)} saved.");
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

    /// <summary>Today in India: the latest date the drop date picker offers (the command checks it too).</summary>
    public string DropDateMax => ItProjectYearTypes.TodayIst(_clock.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The day the project was added, in India (the stored time is UTC).</summary>
    public DateOnly AddedOn => ItProjectYearTypes.ToIstDate(Project.CreatedAtUtc);

    /// <summary>The day of the last change, in India.</summary>
    public DateOnly LastChangedOn => ItProjectYearTypes.ToIstDate(Project.UpdatedAtUtc);

    /// <summary>The latest save, remark or file, for the Record card.</summary>
    public ItProjectPagePresentation.Activity LastActivity
        => ItProjectPagePresentation.LastActivity(Project, UpdatedByName, Remarks, Attachments);

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
        YearTypes = _reads.GetYearTypes(project);
        Timing = _reads.GetTiming(project);
        HeroStatus = ItProjectPagePresentation.Status(project, Status, Timing);
        Journey = ItProjectPagePresentation.Journey(project, Status, Timing);
        Remarks = await _remarks.ListAsync(id, cancellationToken);
        Attachments = await _attachments.ListAsync(id, cancellationToken);
        Feed = ItProjectPagePresentation.Feed(project, Remarks);
        CanRemark = ItProjectAccess.CanRemark(User, project);
        RemarkRole = ItProjectAccess.RemarkRoleOf(User);
        IsAdmin = User.IsInRole(RoleNames.Admin);
        IsHoD = User.IsInRole(RoleNames.HoD);
        return true;
    }

    private void FillInputsFromProject(bool keepDrop = false)
    {
        FinancialYearOptions = CreateModel.BuildFinancialYearOptions(
            CreateModel.IsSupportedFinancialYear(Project.FinancialYearStart) ? Project.FinancialYearStart : CurrentFinancialYearStart(),
            CurrentFinancialYearStart());
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
                DroppedOn = ItProjectYearTypes.TodayIst(_clock.UtcNow),
                RowVersion = RowVersionToken
            };
        }

        RowVersion = RowVersionToken;
        // The Comdt writes conference directions most of the time, so the composer starts there for
        // them, as in the other modules; everyone else starts on a general remark.
        Remark = new RemarkInput
        {
            EventDate = ItProjectYearTypes.TodayIst(_clock.UtcNow),
            Type = string.Equals(RemarkRole, RoleNames.Comdt, StringComparison.OrdinalIgnoreCase) ? "Conference" : "General"
        };
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

    private static IReadOnlyCollection<ItProjectStageInput> ApplyReopen(IReadOnlyCollection<ItProjectStageInput> inputs, string? reopen)
    {
        if (string.IsNullOrWhiteSpace(reopen))
        {
            return inputs;
        }

        foreach (var code in reopen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            inputs = ItProjectStageRules.Reopen(inputs, code);
        }

        return inputs;
    }

    private static List<ItProjectStageInput> ToStageInputs(IEnumerable<StageItemInput> items)
        => items
            .Select(item => new ItProjectStageInput(
                item.StageCode,
                item.IsCompleted,
                item.CompletedOn,
                IsSkipped: ItProjectStages.IsKnown(item.StageCode) && ItProjectStages.IsOptional(item.StageCode) && !item.IsIncluded,
                item.CostLakhs))
            .ToList();

    private bool IsSectionValid(string prefix)
        => ModelState.Where(entry => entry.Key.StartsWith(prefix + ".", StringComparison.Ordinal) || entry.Key == prefix)
            .All(entry => entry.Value?.Errors.Count == 0);

    /// <summary>
    /// Every error a drawer should list: the command's errors (keyed by the drawer) and the
    /// binding or validation errors of its fields. Each field error names the stage when it can.
    /// </summary>
    public IReadOnlyList<string> DrawerErrors(string open, string prefix)
    {
        var errors = new List<string>();
        foreach (var entry in ModelState)
        {
            if (entry.Value is null || entry.Value.Errors.Count == 0)
            {
                continue;
            }

            if (entry.Key != open && !(entry.Key == prefix || entry.Key.StartsWith(prefix + ".", StringComparison.Ordinal)))
            {
                continue;
            }

            var stage = StageOfKey(entry.Key);
            foreach (var error in entry.Value.Errors)
            {
                var text = string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "A value could not be read. Check the dates and the cost."
                    : error.ErrorMessage;
                errors.Add(stage is null ? text : $"{stage}: {text}");
            }
        }

        return errors.Distinct().ToList();
    }

    private string? StageOfKey(string key)
    {
        const string marker = "Stages.Items[";
        var start = key.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var close = key.IndexOf(']', start);
        if (close < 0 || !int.TryParse(key.AsSpan(start + marker.Length, close - start - marker.Length), out var index))
        {
            return null;
        }

        return index >= 0 && index < Stages.Items.Count && ItProjectStages.IsKnown(Stages.Items[index].StageCode)
            ? ItProjectStages.LabelOf(Stages.Items[index].StageCode)
            : null;
    }

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
        => ItProjectYearTypes.CurrentFinancialYear(_clock.UtcNow);

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
