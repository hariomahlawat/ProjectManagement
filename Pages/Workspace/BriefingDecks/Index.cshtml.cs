using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Infrastructure;
using ProjectManagement.Infrastructure.Ui;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Pages.Workspace.BriefingDecks;

[Authorize(Policy = Policies.ProjectBriefingDecks.Manage)]
public sealed class IndexModel : PageModel
{
    private readonly IProjectBriefingDeckService _deckService;
    private readonly IProjectBriefingSelectionService _selectionService;
    private readonly IProjectBriefingDataService _dataService;
    private readonly IProjectBriefingPowerPointExportService _exportService;
    private readonly CommandWorkspaceService _commandWorkspaceService;
    private readonly ProjectOfficerWorkspaceService _projectOfficerWorkspaceService;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<IndexModel> _logger;

    private readonly IItoWorkspaceQuery? _itoWorkspace;

    public IndexModel(
        IProjectBriefingDeckService deckService,
        IProjectBriefingSelectionService selectionService,
        IProjectBriefingDataService dataService,
        IProjectBriefingPowerPointExportService exportService,
        CommandWorkspaceService commandWorkspaceService,
        ProjectOfficerWorkspaceService projectOfficerWorkspaceService,
        UserManager<ApplicationUser> users,
        ILogger<IndexModel> logger,
        IItoWorkspaceQuery? itoWorkspace = null)
    {
        _itoWorkspace = itoWorkspace;
        _deckService = deckService ?? throw new ArgumentNullException(nameof(deckService));
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _commandWorkspaceService = commandWorkspaceService ?? throw new ArgumentNullException(nameof(commandWorkspaceService));
        _projectOfficerWorkspaceService = projectOfficerWorkspaceService ?? throw new ArgumentNullException(nameof(projectOfficerWorkspaceService));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<ProjectBriefingDeckSummaryVm> Decks { get; private set; }
        = Array.Empty<ProjectBriefingDeckSummaryVm>();
    public IReadOnlyList<ProjectBriefingDeletedDeckVm> DeletedDecks { get; private set; }
        = Array.Empty<ProjectBriefingDeletedDeckVm>();
    public ProjectBriefingDeckVm? SelectedDeck { get; private set; }

    // The library's "Continue" deck: the one this user edited last, else the latest edited.
    public ProjectBriefingDeckSummaryVm? ContinueDeck { get; private set; }

    // Other decks whose data moved since they were last generated, most changes first.
    public IReadOnlyList<ProjectBriefingDeckSummaryVm> MovedDecks { get; private set; } = Array.Empty<ProjectBriefingDeckSummaryVm>();
    public ProjectBriefingSelectionOptionsVm SelectionOptions { get; private set; } = new();
    public CommandWorkspaceRailVm CommandRail { get; private set; } = new() { HasCommandAccess = true, ActiveLens = "command", ActiveItem = "command-briefing-decks" };
    public FfcFootprintSummary FfcFootprintPreviewSummary { get; private set; } = new(0, 0, 0, 0, 0, 0);

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    // Shown for this request only: unlike the TempData messages it must not follow the person
    // back to the library on their next visit.
    public string? DeckNotFoundMessage { get; private set; }

    [TempData]
    public bool ReopenInstitutionalProfile { get; set; }

    [TempData]
    public bool ReopenRoleCharter { get; set; }

    [TempData]
    public bool ReopenFfcGlobalFootprint { get; set; }

    // Opened by a new deck: the Studio starts on Add projects.
    public bool StartWithAddProjects { get; private set; }

    // Comdt and HoD share command decks; anyone else here is a Project Officer with decks of
    // their own (ProjectBriefingDeckAccessRules). Only wording and the rail follow this; every
    // deck is checked on the server.
    public bool IsCommandUser { get; private set; }

    // The open deck is a Project Officer's own (shown as theirs, not as shared).
    public bool SelectedDeckIsPersonal { get; private set; }

    // How many projects this Project Officer leads, for the "My projects" quick add.
    public int MyProjectCount { get; private set; }

    public async Task<IActionResult> OnGetAsync(long? deckId, string? start, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        await LoadPageAsync(userId, deckId, cancellationToken);
        StartWithAddProjects = SelectedDeck is not null
            && string.Equals(start, "projects", StringComparison.Ordinal);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(
        [FromForm] CreateDeckInput input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (!ModelState.IsValid)
        {
            ErrorMessage = FirstModelError("Enter a valid deck name.");
            return RedirectToPage();
        }

        try
        {
            var deckId = await _deckService.CreateAsync(
                userId,
                input.Name,
                input.Description,
                cancellationToken,
                input.Template);
            // A new deck opens on Add projects: the next step.
            return RedirectToPage(new { deckId, start = "projects" });
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
            return RedirectToPage();
        }
        catch (DbUpdateException)
        {
            ErrorMessage = "The deck could not be created. A deck with the same name may already exist.";
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostDuplicateAsync(long deckId, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        try
        {
            var duplicateId = await _deckService.DuplicateAsync(deckId, userId, cancellationToken);
            StatusMessage = "Deck duplicated. You can now tailor the copy.";
            return RedirectToPage(new { deckId = duplicateId });
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
            return RedirectToPage(new { deckId });
        }
        catch (DbUpdateException)
        {
            ErrorMessage = "The deck could not be duplicated because the shared deck was updated. Reload and try again.";
            return RedirectToPage(new { deckId });
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        long deckId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        try
        {
            await _deckService.DeleteAsync(deckId, userId, rowVersion, cancellationToken);
            // The glass toast offers Undo through the same handler as Recently deleted's Restore,
            // which re-checks access and the retention window (docs/ux/toasts.md).
            TempData.ToastWithAction("success", "Briefing deck moved to Recently deleted.", new ToastAction(
                "Undo",
                Url.Page("/Workspace/BriefingDecks/Index", "Restore", new { deckId = (long?)null })!,
                new Dictionary<string, string>
                {
                    ["deckId"] = deckId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
            return RedirectToLibrary();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            // Not found, or a Project Officer's deck a command user may only view.
            ErrorMessage = exception.Message;
            return RedirectToLibrary();
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "The deck was updated by another user and was not deleted. Reload and try again.";
            return RedirectToPage(new { deckId });
        }
        catch (DbUpdateException)
        {
            ErrorMessage = "The deck could not be deleted. Reload and try again.";
            return RedirectToPage(new { deckId });
        }
    }

    public async Task<IActionResult> OnPostRestoreAsync(
        long deckId,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        try
        {
            var restored = await _deckService.RestoreAsync(deckId, userId, cancellationToken);
            StatusMessage = restored.Renamed
                ? $"Briefing deck restored as “{restored.Name}” because another deck now uses its name."
                : "Briefing deck restored.";
            return RedirectToPage(new { deckId = restored.DeckId });
        }
        catch (KeyNotFoundException exception)
        {
            ErrorMessage = exception.Message;
            return RedirectToLibrary();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else restored the deck between loading it and saving: it is back, so open it.
            StatusMessage = "Someone else restored this deck a moment ago. It is already back.";
            return RedirectToPage(new { deckId });
        }
        catch (DbUpdateException)
        {
            // Includes a failed audit write: the restore was rolled back, so nothing changed.
            ErrorMessage = "The deck could not be restored, and nothing was changed. Reload and try again.";
            return RedirectToLibrary();
        }
    }

    // The library itself. A bare RedirectToPage() would keep the deck id from the current URL
    // (/Workspace/BriefingDecks/{deckId}), which after a delete opens a deck that is gone.
    private IActionResult RedirectToLibrary() => RedirectToPage(new { deckId = (long?)null });

    public async Task<IActionResult> OnPostSaveSettingsAsync(
        [FromForm] SaveDeckSettingsInput input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        // The Studio's Deck tab saves each change in place and refreshes from the returned deck;
        // a plain form post keeps the redirect with a status message.
        var isAjax = IsAjaxRequest();
        IActionResult Fail(string message, int statusCode = StatusCodes.Status400BadRequest)
        {
            if (isAjax)
            {
                return new JsonResult(new { message }) { StatusCode = statusCode };
            }

            ErrorMessage = message;
            return RedirectToPage(new { deckId = input.DeckId });
        }

        if (!ModelState.IsValid)
        {
            return Fail(FirstModelError("Review the deck settings and try again."));
        }

        string? settingsRowVersion = null;
        try
        {
            var existingDeck = await _deckService.GetEntityAsync(
                input.DeckId,
                userId,
                includeItems: false,
                cancellationToken)
                ?? throw await UnavailableDeckAsync(input.DeckId, userId, cancellationToken);
            var additionalSlides = ProjectBriefingDeckConfigurationCodec
                .Read(existingDeck.SelectionRulesJson);
            // The design card sets the layout, what each project gets and key points; a page that
            // posts no design keeps sending those fields itself.
            var design = ProjectBriefingProjectSlideDesigns.Settings(
                input.ProjectDesign
                    ?? ProjectBriefingProjectSlideDesigns.Of(
                        input.Layout,
                        input.NarrativeMode,
                        ProjectBriefingStandardSlideOptions.Default with { KeyPoints = ResolveKeyPointPlacement(input) }),
                input.NarrativeMode,
                ResolveKeyPointPlacement(input),
                input.ShowKeyPoints,
                input.KeyPointSlide);

            settingsRowVersion = await _deckService.UpdateSettingsAsync(
                input.DeckId,
                userId,
                new ProjectBriefingDeckSettingsCommand
                {
                    Name = input.Name,
                    Description = input.Description,
                    Layout = design.Layout,
                    // The block list owns what the deck shows (UpdateBlocks); a settings save keeps it.
                    PresentationMode = existingDeck.PresentationMode,
                    CostMode = input.CostMode,
                    NarrativeMode = design.NarrativeMode,
                    ProjectBriefLayout = input.ProjectBriefLayout,
                    ShowPresentStage = input.ShowPresentStage,
                    ShowPresentStatus = input.ShowPresentStatus,
                    // No longer offered (the stage-wise distribution has its own list, PR 7); kept as stored
                    // so a rolled-back build still draws the table a deck had.
                    IncludeStageDistributionTable = additionalSlides.StandardSlideOptions.IncludeStageDistributionTable,
                    KeyPoints = design.KeyPoints,
                    KeyPointsOnly = design.KeyPointsOnly,
                    PresentationTheme = input.PresentationTheme,
                    ClosingSlideType = input.ClosingSlideType,
                    InstitutionalProfileOptions = additionalSlides.InstitutionalProfileOptions,
                    RoleCharterOptions = additionalSlides.RoleCharterOptions,
                    FfcGlobalFootprintOptions = additionalSlides.FfcGlobalFootprintOptions,
                    AdditionalSlideOrder = additionalSlides.AdditionalSlideOrder,
                    BrandingScope = input.BrandingScope,
                    IncludeCoverSlide = existingDeck.IncludeCoverSlide,
                    IncludePortfolioSummarySlide = existingDeck.IncludePortfolioSummarySlide,
                    IncludeStageSummary = existingDeck.IncludeStageSummary,
                    IncludeProjectCategorySummary = existingDeck.IncludeProjectCategorySummary,
                    IncludeTechnicalCategorySummary = existingDeck.IncludeTechnicalCategorySummary,
                    UpdateSheetRows = ResolveUpdateSheetRows(input.UpdateSheetRows, input.UpdateSheetRowOrder),
                    HideEmptyUpdateSheetValues = input.HideEmptyUpdateSheetValues,
                    HandlingMarking = input.HandlingMarking,
                    RowVersion = input.RowVersion
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail("This deck was updated by another user. Reload the page before saving.", StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException)
        {
            return Fail("The deck settings could not be saved. A deck with the same name may already exist.");
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return Fail(exception.Message);
        }

        if (isAjax)
        {
            var deck = await _dataService.GetDeckAsync(input.DeckId, userId, cancellationToken);
            // The version this save produced; the re-read deck may already carry someone else's.
            return new JsonResult(new { saved = true, rowVersion = settingsRowVersion, deck });
        }

        StatusMessage = "Deck settings saved.";
        return RedirectToPage(new { deckId = input.DeckId });
    }

    // The Deck tab's block list: which slides are in the deck and in what order. Saved in place.
    // Why a deck cannot be changed: view-only for a command user on a Project Officer's deck,
    // otherwise not found (review 2 fixes, Low 1).
    private async Task<Exception> UnavailableDeckAsync(long deckId, string userId, CancellationToken cancellationToken)
        => await _deckService.GetAccessAsync(deckId, userId, cancellationToken) == ProjectBriefingDeckAccessLevel.View
            ? new InvalidOperationException(ProjectBriefingDeckService.ViewOnlyMessage)
            : new KeyNotFoundException(ProjectBriefingDeckService.NotFoundMessage);

    public async Task<IActionResult> OnPostUpdateBlocksAsync(
        [FromBody] UpdateBlocksInput? input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (input is null || input.DeckId <= 0 || string.IsNullOrWhiteSpace(input.RowVersion) || input.Blocks is null)
        {
            return BadRequest(new { message = "The slide list is invalid." });
        }

        try
        {
            var rowVersion = await _deckService.UpdateBlocksAsync(
                input.DeckId,
                userId,
                input.Blocks
                    .Select(block => new ProjectBriefingDeckBlock(block.Type, block.Enabled))
                    .ToArray(),
                input.RowVersion,
                cancellationToken);
            var deck = await _dataService.GetDeckAsync(input.DeckId, userId, cancellationToken);
            return new JsonResult(new { saved = true, rowVersion, deck });
        }
        catch (DbUpdateConcurrencyException)
        {
            return new JsonResult(new { message = "The deck was updated by another user. Reload before changing its slides." })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private bool IsAjaxRequest()
        => string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    // The content editors (SDD profile, Role & Charter, FFC footprint) save in place like the rest of
    // the Studio: an in-page request gets the message, the version its own save produced and the
    // refreshed deck; a plain form post keeps the redirect (review 2 fixes, PR 4).
    private async Task<IActionResult> EditorReplyAsync(
        long deckId,
        string userId,
        string? rowVersion,
        int failureStatus,
        CancellationToken cancellationToken)
    {
        if (!IsAjaxRequest())
        {
            return RedirectToPage(new { deckId });
        }

        // TempData is for the redirect; an in-place reply carries its message itself.
        var error = ErrorMessage;
        var message = StatusMessage;
        ErrorMessage = null;
        StatusMessage = null;
        ReopenInstitutionalProfile = false;
        ReopenRoleCharter = false;
        ReopenFfcGlobalFootprint = false;
        if (error is not null || rowVersion is null)
        {
            return new JsonResult(new { message = error ?? "The slide could not be saved." }) { StatusCode = failureStatus };
        }

        var deck = await _dataService.GetDeckAsync(deckId, userId, cancellationToken);
        return new JsonResult(new { saved = true, message, rowVersion, deck });
    }

    public async Task<IActionResult> OnPostSaveInstitutionalProfileAsync(
        [FromForm] SaveInstitutionalProfileInput input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        string? editorRowVersion = null;
        var failure = StatusCodes.Status400BadRequest;
        if (!ModelState.IsValid)
        {
            ErrorMessage = FirstModelError("Review the SDD profile and try again.");
            ReopenInstitutionalProfile = true;
            return await EditorReplyAsync(input.DeckId, userId, editorRowVersion, failure, cancellationToken);
        }

        try
        {
            var options = ProjectBriefingInstitutionalProfileOptions.Normalize(
                input.IncludeInstitutionalProfile,
                input.InstitutionalProfileTitle,
                input.IncludeInstitutionalHistory,
                ParseInstitutionalHistory(input.InstitutionalHistoryLines),
                ResolveInstitutionalModules(input.InstitutionalModules, input.InstitutionalModuleOrder),
                input.InstitutionalProjectScope,
                input.InstitutionalMaximumDetailRows,
                input.InstitutionalTrainingHighlightCategory,
                ParseSimpleLines(input.InstitutionalPartnershipLines),
                input.IncludeInstitutionalFooterStrip,
                input.InstitutionalFooterStripText,
                input.InstitutionalFooterStripEmphasisValue,
                input.InstitutionalFooterStripStyle,
                input.InstitutionalFooterStripAlignment);

            editorRowVersion = await _deckService.UpdateInstitutionalProfileAsync(
                input.DeckId,
                userId,
                options,
                input.RowVersion,
                cancellationToken);
            StatusMessage = "SDD profile saved.";
        }
        catch (DbUpdateConcurrencyException)
        {
            failure = StatusCodes.Status409Conflict;
            ErrorMessage = "This deck was updated by another user. Reload before saving the profile.";
            ReopenInstitutionalProfile = true;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
            ReopenInstitutionalProfile = true;
        }

        return await EditorReplyAsync(input.DeckId, userId, editorRowVersion, failure, cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveRoleCharterAsync(
        [FromForm] SaveRoleCharterInput input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        string? editorRowVersion = null;
        var failure = StatusCodes.Status400BadRequest;
        if (!ModelState.IsValid)
        {
            ErrorMessage = FirstModelError("Review Role & Charter and try again.");
            ReopenRoleCharter = true;
            return await EditorReplyAsync(input.DeckId, userId, editorRowVersion, failure, cancellationToken);
        }

        try
        {
            var options = ProjectBriefingRoleCharterOptions.Normalize(
                input.IncludeRoleCharter,
                input.RoleCharterTitle,
                input.RoleCharterLayout,
                input.UseSharedRoleCharterContent,
                ParseRoleCharterEntries(input.RoleStatementLines),
                ParseRoleCharterEntries(input.CharterItemLines));
            editorRowVersion = await _deckService.UpdateRoleCharterAsync(
                input.DeckId,
                userId,
                options,
                input.RowVersion,
                cancellationToken);
            StatusMessage = "Role & Charter saved.";
        }
        catch (DbUpdateConcurrencyException)
        {
            failure = StatusCodes.Status409Conflict;
            ErrorMessage = "This deck was updated by another user. Reload before saving Role & Charter.";
            ReopenRoleCharter = true;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
            ReopenRoleCharter = true;
        }

        return await EditorReplyAsync(input.DeckId, userId, editorRowVersion, failure, cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveFfcGlobalFootprintAsync(
        [FromForm] SaveFfcGlobalFootprintInput input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        string? editorRowVersion = null;
        var failure = StatusCodes.Status400BadRequest;
        if (!ModelState.IsValid)
        {
            ErrorMessage = FirstModelError("Review the FFC footprint and try again.");
            ReopenFfcGlobalFootprint = true;
            return await EditorReplyAsync(input.DeckId, userId, editorRowVersion, failure, cancellationToken);
        }

        try
        {
            var options = ProjectBriefingFfcGlobalFootprintOptions.Normalize(
                input.IncludeFfcGlobalFootprint,
                input.FfcGlobalFootprintTitle,
                input.FfcGlobalFootprintLayout,
                input.MaximumCountryRows,
                input.IncludeCountryWiseBreakdown);
            editorRowVersion = await _deckService.UpdateFfcGlobalFootprintAsync(
                input.DeckId,
                userId,
                options,
                input.RowVersion,
                cancellationToken);
            StatusMessage = "FFC footprint saved.";
        }
        catch (DbUpdateConcurrencyException)
        {
            failure = StatusCodes.Status409Conflict;
            ErrorMessage = "This deck was updated by another user. Reload before saving the FFC footprint.";
            ReopenFfcGlobalFootprint = true;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
            ReopenFfcGlobalFootprint = true;
        }

        return await EditorReplyAsync(input.DeckId, userId, editorRowVersion, failure, cancellationToken);
    }



    public async Task<IActionResult> OnPostAddSelectionAsync(
        [FromForm] AddSelectionInput input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        // The Studio's Add projects dialog posts in place and refreshes from the returned deck;
        // a plain form post keeps the redirect with a status message.
        var isAjax = string.Equals(
            Request.Headers["X-Requested-With"],
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);
        IActionResult Fail(string message, int statusCode = StatusCodes.Status400BadRequest)
        {
            if (isAjax)
            {
                return new JsonResult(new { message }) { StatusCode = statusCode };
            }

            ErrorMessage = message;
            return RedirectToPage(new { deckId = input.DeckId });
        }

        if (string.IsNullOrWhiteSpace(input.RowVersion))
        {
            return Fail("The deck version is missing. Reload and try again.");
        }

        string message;
        string? addedRowVersion = null;
        try
        {
            var selection = await _selectionService.ResolveAsync(
                new ProjectBriefingSelectionRequest
                {
                    Kind = input.Kind,
                    ProjectCategoryIds = input.ProjectCategoryIds,
                    TechnicalCategoryIds = input.TechnicalCategoryIds,
                    ProjectIds = input.ProjectIds,
                    CompletionYearFrom = input.CompletionYearFrom,
                    CompletionYearTo = input.CompletionYearTo,
                    OfficerUserId = userId
                },
                cancellationToken);

            if (selection.ProjectIds.Count == 0)
            {
                return Fail("No projects match the selected criteria.");
            }

            var added = await _deckService.AddProjectsAsync(
                input.DeckId,
                userId,
                selection.ProjectIds,
                selection.SelectionRulesJson,
                input.RowVersion,
                cancellationToken);
            addedRowVersion = added.RowVersion;
            message = added.Added == 0
                ? "All matching projects are already in this deck."
                : $"{added.Added} project{(added.Added == 1 ? string.Empty : "s")} added — {selection.RuleSummary}.";
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return Fail(exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(
                "The matching projects could not be added because another user updated the deck. Reload and try again.",
                StatusCodes.Status409Conflict);
        }
        catch (DbUpdateException)
        {
            return Fail("The matching projects could not be added because of a database error. Reload and try again.");
        }

        if (isAjax)
        {
            var deck = await _dataService.GetDeckAsync(input.DeckId, userId, cancellationToken);
            return new JsonResult(new { saved = true, message, rowVersion = addedRowVersion, deck });
        }

        StatusMessage = message;
        return RedirectToPage(new { deckId = input.DeckId });
    }

    public async Task<IActionResult> OnPostUpdateMembershipAsync(
        [FromBody] UpdateMembershipInput? input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (input is null || input.DeckId <= 0 || string.IsNullOrWhiteSpace(input.RowVersion))
        {
            return BadRequest(new { message = "The deck-membership request is invalid." });
        }

        try
        {
            var result = await _deckService.UpdateMembershipAsync(
                input.DeckId,
                userId,
                input.AddProjectIds,
                input.RemoveProjectIds,
                input.RowVersion,
                cancellationToken);
            var deck = await _dataService.GetDeckAsync(input.DeckId, userId, cancellationToken)
                ?? throw new KeyNotFoundException(ProjectBriefingDeckService.NotFoundMessage);

            return new JsonResult(new
            {
                saved = true,
                result.AddedCount,
                result.RemovedCount,
                rowVersion = result.RowVersion,
                deck
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return new JsonResult(new
            {
                message = "This deck was updated by another user. Reload to review the latest version before applying your changes."
            })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    public async Task<IActionResult> OnPostReorderAsync(
        [FromBody] ReorderInput? input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (input is null || input.DeckId <= 0 || input.ProjectIds is null || string.IsNullOrWhiteSpace(input.RowVersion))
        {
            return BadRequest(new { message = "The deck order request is invalid." });
        }

        try
        {
            var rowVersion = await _deckService.ReorderAsync(
                input.DeckId,
                userId,
                input.ProjectIds,
                input.RowVersion,
                cancellationToken);
            return new JsonResult(new { saved = true, rowVersion });
        }
        catch (DbUpdateConcurrencyException)
        {
            return new JsonResult(new
            {
                message = "The deck was updated by another user. Reload before changing the slide order."
            })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    public async Task<IActionResult> OnPostUpdateDescriptionAsync(
        [FromBody] UpdateDescriptionInput? input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (input is null || input.DeckId <= 0 || input.ProjectId <= 0 || string.IsNullOrWhiteSpace(input.RowVersion))
        {
            return BadRequest(new { message = "The briefing-description request is invalid." });
        }

        try
        {
            var rowVersion = await _deckService.UpdateBriefDescriptionAsync(
                input.DeckId,
                input.ProjectId,
                userId,
                input.Value,
                input.RowVersion,
                cancellationToken);
            // The refreshed deck lets the Studio update the project's text and content gaps.
            var deck = await _dataService.GetDeckAsync(input.DeckId, userId, cancellationToken);
            return new JsonResult(new { saved = true, rowVersion, deck });
        }
        catch (DbUpdateConcurrencyException)
        {
            return new JsonResult(new
            {
                message = "The deck was updated by another user. Reload before editing the description."
            })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    public async Task<IActionResult> OnPostUpdateItemPresentationAsync(
        [FromBody] UpdateItemPresentationInput? input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (input is null || input.DeckId <= 0 || input.ProjectId <= 0 || string.IsNullOrWhiteSpace(input.RowVersion))
        {
            return BadRequest(new { message = "The request to change how the project appears is invalid." });
        }

        try
        {
            var rowVersion = await _deckService.UpdateItemPresentationAsync(
                input.DeckId,
                input.ProjectId,
                userId,
                input.Presentation,
                input.RowVersion,
                cancellationToken);
            var deck = await _dataService.GetDeckAsync(input.DeckId, userId, cancellationToken);
            return new JsonResult(new { saved = true, rowVersion, deck });
        }
        catch (DbUpdateConcurrencyException)
        {
            return new JsonResult(new
            {
                message = "The deck was updated by another user. Reload before changing how the project appears."
            })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    public async Task<IActionResult> OnGetPhotoOptionsAsync(
        long deckId,
        int projectId,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (deckId <= 0 || projectId <= 0)
        {
            return BadRequest(new { message = "The photo request is invalid." });
        }

        if (await _deckService.GetAccessAsync(deckId, userId, cancellationToken) != ProjectBriefingDeckAccessLevel.Edit)
        {
            return NotFound(new { message = ProjectBriefingDeckService.NotFoundMessage });
        }

        var photos = await _dataService.GetPhotoOptionsAsync(deckId, projectId, cancellationToken);
        return photos is null
            ? NotFound(new { message = "The project is not part of this deck." })
            : new JsonResult(new { photos });
    }

    public async Task<IActionResult> OnPostUpdateSpeakerNotesAsync(
        [FromBody] UpdateSpeakerNotesInput? input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (input is null || input.DeckId <= 0 || input.ProjectId <= 0 || string.IsNullOrWhiteSpace(input.RowVersion))
        {
            return BadRequest(new { message = "The speaker-notes request is invalid." });
        }

        try
        {
            var rowVersion = await _deckService.UpdateItemSpeakerNotesAsync(
                input.DeckId,
                input.ProjectId,
                userId,
                input.Notes,
                input.RowVersion,
                cancellationToken);
            var deck = await _dataService.GetDeckAsync(input.DeckId, userId, cancellationToken);
            return new JsonResult(new { saved = true, rowVersion, deck });
        }
        catch (DbUpdateConcurrencyException)
        {
            return new JsonResult(new
            {
                message = "The deck was updated by another user. Reload before editing the speaker notes."
            })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    public async Task<IActionResult> OnPostUpdateItemPhotoAsync(
        [FromBody] UpdateItemPhotoInput? input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (input is null || input.DeckId <= 0 || input.ProjectId <= 0 || input.PhotoId <= 0
            || string.IsNullOrWhiteSpace(input.RowVersion))
        {
            return BadRequest(new { message = "The photo choice is invalid." });
        }

        try
        {
            var rowVersion = await _deckService.UpdateItemPhotoAsync(
                input.DeckId,
                input.ProjectId,
                userId,
                input.PhotoId,
                input.RowVersion,
                cancellationToken);
            var deck = await _dataService.GetDeckAsync(input.DeckId, userId, cancellationToken);
            return new JsonResult(new { saved = true, rowVersion, deck });
        }
        catch (DbUpdateConcurrencyException)
        {
            return new JsonResult(new
            {
                message = "The deck was updated by another user. Reload before choosing the photograph."
            })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    public async Task<IActionResult> OnGetSearchProjectsAsync(
        long deckId,
        string? query,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (deckId <= 0)
        {
            return BadRequest(new { message = "Select a deck before searching projects." });
        }

        var projects = await _selectionService.SearchAsync(query, 40, cancellationToken);
        var deck = await _deckService.GetEntityAsync(deckId, userId, includeItems: true, cancellationToken);
        if (deck is null)
        {
            return NotFound(new { message = ProjectBriefingDeckService.NotFoundMessage });
        }

        var membership = deck.Items.ToDictionary(item => item.ProjectId, item => item.SortOrder);
        var rows = projects.Select(project => new ProjectBriefingManageSearchResultVm(
            project.ProjectId,
            project.ProjectName,
            project.Lifecycle,
            project.PresentStage,
            project.ProjectCategory,
            project.TechnicalCategory,
            project.ProjectOfficer,
            project.CaseFileNumber,
            membership.ContainsKey(project.ProjectId),
            membership.TryGetValue(project.ProjectId, out var sortOrder) ? sortOrder : null))
            .ToArray();
        return new JsonResult(rows);
    }

    public async Task<IActionResult> OnGetPreviewAsync(long deckId, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        try
        {
            var composed = await _exportService.ComposeAsync(deckId, userId, cancellationToken);
            var preview = ProjectBriefingSlidePreviewRenderer.Render(
                composed.Content,
                composed.Data.GeneratedAtUtc,
                ReadKnownPreviewImageKeys());
            // Project facts and gaps travel with the slides, so a rebuild after a fix made in
            // another tab also clears the Studio's outline badges and checks.
            var deck = await _dataService.GetDeckAsync(deckId, userId, cancellationToken);
            return new JsonResult(new
            {
                preview.SlideCount,
                generatedAt = IstClock.ToIst(preview.GeneratedAtUtc).ToString("dd MMM yyyy, HH:mm"),
                slides = preview.Slides,
                images = preview.Images,
                outline = composed.Outline.Select(item => new
                {
                    item.Number,
                    role = item.Role.ToString(),
                    item.ProjectId,
                    item.ListedProjectIds
                }),
                projects = deck?.Projects,
                contentGaps = deck?.ContentGaps
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProjectBriefingPresentationIntegrityException exception)
        {
            var traceId = HttpContext.TraceIdentifier;
            _logger.LogError(exception, "Project briefing preview failed integrity validation. DeckId={DeckId}, TraceId={TraceId}", deckId, traceId);
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new { message = $"The slides could not be previewed: {exception.GetUserSafeSummary()}. Reference: {traceId}" });
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (Exception exception)
        {
            var traceId = HttpContext.TraceIdentifier;
            _logger.LogError(exception, "Project briefing preview failed. DeckId={DeckId}, TraceId={TraceId}", deckId, traceId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { message = $"The slide preview could not be built. Reference: {traceId}" });
        }
    }

    // The Studio sends the image keys it already holds (hex hashes, comma-separated) so a rebuild
    // after each edit does not ship every photograph again. Anything else in the header is ignored.
    private IReadOnlySet<string> ReadKnownPreviewImageKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in Request.Headers["X-Pbd-Known-Images"])
        {
            foreach (var key in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (keys.Count >= 512) return keys;
                if (key.Length == 16 && key.All(Uri.IsHexDigit)) keys.Add(key.ToLowerInvariant());
            }
        }

        return keys;
    }

    // The PowerPoint of a deck this person may see, without recording a generation: how a command
    // user takes a Project Officer's deck they may not change. The deck itself is untouched.
    public async Task<IActionResult> OnPostDownloadAsync(long deckId, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        try
        {
            var result = await _exportService.DownloadAsync(deckId, userId, cancellationToken);
            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
            return RedirectToLibrary();
        }
        catch (Exception exception)
        {
            var traceId = HttpContext.TraceIdentifier;
            _logger.LogError(exception, "Project briefing deck download failed. DeckId={DeckId}, TraceId={TraceId}", deckId, traceId);
            ErrorMessage = $"The PowerPoint could not be made. Reference: {traceId}";
            return RedirectToLibrary();
        }
    }

    public async Task<IActionResult> OnPostGenerateAsync(long deckId, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var isAjax = string.Equals(
            Request.Headers["X-Requested-With"],
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);

        try
        {
            var result = await _exportService.GenerateAsync(deckId, userId, cancellationToken);
            Response.Headers["X-Project-Briefing-Slides"] = result.SlideCount.ToString();
            return File(result.Content, result.ContentType, result.FileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProjectBriefingPresentationIntegrityException exception)
        {
            var traceId = HttpContext.TraceIdentifier;
            _logger.LogError(
                exception,
                "Project briefing deck failed integrity validation. DeckId={DeckId}, TraceId={TraceId}, Issues={IntegrityIssues}",
                deckId,
                traceId,
                string.Join(" || ", exception.Issues));

            var message =
                $"PowerPoint integrity check failed: {exception.GetUserSafeSummary()}. Reference: {traceId}";
            if (isAjax)
            {
                return StatusCode(
                    StatusCodes.Status422UnprocessableEntity,
                    new
                    {
                        message,
                        traceId,
                        issues = exception.Issues.Take(6).ToArray()
                    });
            }

            ErrorMessage = message;
            return RedirectToPage(new { deckId });
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            if (isAjax)
            {
                return BadRequest(new { message = exception.Message });
            }

            ErrorMessage = exception.Message;
            return RedirectToPage(new { deckId });
        }
        catch (Exception exception)
        {
            var traceId = HttpContext.TraceIdentifier;
            _logger.LogError(
                exception,
                "Project briefing deck generation failed. DeckId={DeckId}, TraceId={TraceId}",
                deckId,
                traceId);
            var message = $"The PowerPoint deck could not be generated. Reference: {traceId}";
            if (isAjax)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message, traceId });
            }

            ErrorMessage = message;
            return RedirectToPage(new { deckId });
        }
    }

    private async Task LoadPageAsync(
        string userId,
        long? requestedDeckId,
        CancellationToken cancellationToken)
    {
        IsCommandUser = User.IsInRole(RoleNames.Comdt) || User.IsInRole(RoleNames.HoD);
        Decks = await _deckService.ListAsync(userId, cancellationToken);
        DeletedDecks = await _deckService.ListDeletedAsync(userId, cancellationToken);

        // No deck in the address: the library, where a deck is chosen. A deck is never opened
        // (and so never edited) without the person picking it. The Studio is for decks this person
        // may change; a Project Officer's deck a command user may only look at stays in the
        // library, with Download and Duplicate.
        if (requestedDeckId.HasValue)
        {
            var access = await _deckService.GetAccessAsync(requestedDeckId.Value, userId, cancellationToken);
            if (access == ProjectBriefingDeckAccessLevel.Edit)
            {
                SelectedDeck = await _dataService.GetDeckAsync(requestedDeckId.Value, userId, cancellationToken, includeFfcFootprintSummary: true);
                SelectedDeckIsPersonal = Decks.FirstOrDefault(row => row.Id == requestedDeckId.Value)?.Scope
                    == ProjectBriefingDeckScope.Personal;
            }

            if (SelectedDeck is null && string.IsNullOrWhiteSpace(ErrorMessage))
            {
                DeckNotFoundMessage = access == ProjectBriefingDeckAccessLevel.View
                    ? "That deck is a Project Officer's own. Download its PowerPoint or duplicate it from the list below; only its owner can change it."
                    : "That deck was not found. It may have been deleted; deleted decks can be restored from Recently deleted.";
            }
        }

        if (SelectedDeck is null)
        {
            Decks = await _dataService.WithDataMovementAsync(Decks, cancellationToken);
            // Continue and Needs attention offer decks this person can open in the Studio.
            var editable = Decks.Where(deck => deck.CanEdit).ToList();
            ContinueDeck = editable.FirstOrDefault(deck => deck.LastEditedByMe) ?? editable.FirstOrDefault();
            MovedDecks = editable
                .Where(deck => deck.Id != ContinueDeck?.Id && deck.DataMovement is { HasMoved: true })
                .OrderByDescending(deck => deck.DataMovement!.Total)
                .ThenByDescending(deck => deck.LastGeneratedAtUtc)
                .Take(3)
                .ToList();
        }
        else
        {
            SelectionOptions = await _selectionService.GetOptionsAsync(cancellationToken);
            FfcFootprintPreviewSummary = SelectedDeck.FfcFootprintPreviewSummary;
            if (User.IsInRole(RoleNames.ProjectOfficer))
            {
                MyProjectCount = (await _selectionService.ResolveAsync(
                    new ProjectBriefingSelectionRequest
                    {
                        Kind = ProjectBriefingSelectionKind.MyProjects,
                        OfficerUserId = userId
                    },
                    cancellationToken)).ProjectIds.Count;
            }
        }

        var navigation = IsCommandUser
            ? await _commandWorkspaceService.GetNavigationShellAsync("briefing-decks", cancellationToken)
            : null;
        var hasProjectOfficerAccess = User.IsInRole(RoleNames.ProjectOfficer);
        var personalNavigation = hasProjectOfficerAccess
            ? await _projectOfficerWorkspaceService.GetProjectOfficerWorkspaceAsync(
                userId,
                User,
                ProjectOfficerWorkspaceView.Conference,
                includeDocuments: false,
                ct: cancellationToken)
            : new ProjectOfficerWorkspaceVm();

        // An ITO keeps the conference review and IT projects links on every workspace page.
        var hasItoAccess = User.IsInRole(RoleNames.Ito);
        var liveItProjectCount = hasItoAccess && _itoWorkspace is not null
            ? await _itoWorkspace.CountLiveItProjectsAsync(cancellationToken)
            : 0;

        CommandRail = new CommandWorkspaceRailVm
        {
            HasCommandAccess = IsCommandUser,
            HasProjectOfficerAccess = hasProjectOfficerAccess,
            HasItoAccess = hasItoAccess,
            LiveItProjectCount = liveItProjectCount,
            AssignedModuleLinks = await AssignedModuleLinksAsync(cancellationToken),
            CanViewDocuments = hasProjectOfficerAccess,
            ActiveLens = IsCommandUser ? "command" : "personal",
            ActiveItem = IsCommandUser ? "command-briefing-decks" : "personal-briefing-decks",
            ProjectOfficerCount = navigation?.ProjectOfficerCount ?? 0,
            TotalOngoingProjects = navigation?.TotalOngoingProjects ?? 0,
            AssignedProjectCount = personalNavigation.AssignedProjectCount,
            AssignedTaskCount = personalNavigation.OfficialTaskCount,
            AssignedIdeaCount = personalNavigation.AssignedIdeaCount,
            FollowUpCount = personalNavigation.FollowUpCount,
            AotsUnreadCount = personalNavigation.AotsUnreadCount
        };
    }

    private string RequireUserId()
        => _users.GetUserId(User)
           ?? throw new UnauthorizedAccessException("The current user could not be resolved.");

    private string FirstModelError(string fallback)
        => ModelState.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage).FirstOrDefault()
           ?? fallback;

    public sealed class CreateDeckInput
    {
        [Required, StringLength(160, MinimumLength = 3)]
        public string Name { get; set; } = string.Empty;

        [StringLength(600)]
        public string? Description { get; set; }

        // The template (slide design) the deck starts from; none gives the standard settings.
        public ProjectBriefingDeckPreset? Template { get; set; }
    }

    private static IReadOnlyList<ProjectBriefingUpdateSheetRow> ResolveUpdateSheetRows(
        IReadOnlyCollection<ProjectBriefingUpdateSheetRow>? selectedRows,
        string? orderedRows)
    {
        var selected = (selectedRows ?? Array.Empty<ProjectBriefingUpdateSheetRow>())
            .Where(Enum.IsDefined)
            .ToHashSet();
        var ordered = (orderedRows ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Enum.TryParse<ProjectBriefingUpdateSheetRow>(value, ignoreCase: true, out var row) ? row : (ProjectBriefingUpdateSheetRow?)null)
            .Where(row => row.HasValue && Enum.IsDefined(row.Value) && selected.Contains(row.Value))
            .Select(row => row!.Value)
            .Distinct()
            .ToList();

        ordered.AddRange(selected.Where(row => !ordered.Contains(row)));
        return ordered;
    }

    private static IReadOnlyList<ProjectBriefingInstitutionalProfileModule> ResolveInstitutionalModules(
        IReadOnlyCollection<ProjectBriefingInstitutionalProfileModule>? selectedModules,
        string? orderedModules)
    {
        var selected = (selectedModules ?? Array.Empty<ProjectBriefingInstitutionalProfileModule>())
            .Where(Enum.IsDefined)
            .ToHashSet();
        var ordered = (orderedModules ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Enum.TryParse<ProjectBriefingInstitutionalProfileModule>(value, ignoreCase: true, out var module)
                ? module
                : (ProjectBriefingInstitutionalProfileModule?)null)
            .Where(module => module.HasValue && Enum.IsDefined(module.Value) && selected.Contains(module.Value))
            .Select(module => module!.Value)
            .Distinct()
            .ToList();

        ordered.AddRange(selected.Where(module => !ordered.Contains(module)));
        return ordered;
    }

    private static IReadOnlyList<ProjectBriefingInstitutionalHistoryMilestone> ParseInstitutionalHistory(string? value)
    {
        var result = new List<ProjectBriefingInstitutionalHistoryMilestone>();
        foreach (var line in ParseSimpleLines(value))
        {
            var separator = line.IndexOf('|');
            if (separator <= 0 || separator >= line.Length - 1)
            {
                continue;
            }

            if (int.TryParse(line[..separator].Trim(), out var year))
            {
                result.Add(new ProjectBriefingInstitutionalHistoryMilestone(
                    year,
                    line[(separator + 1)..].Trim()));
            }
        }

        return result;
    }

    private static IReadOnlyList<ProjectBriefingRoleCharterEntry> ParseRoleCharterEntries(string? value)
    {
        var result = new List<ProjectBriefingRoleCharterEntry>();
        foreach (var line in ParseSimpleLines(value))
        {
            var separator = line.IndexOf('\t');
            if (separator < 0)
            {
                separator = line.IndexOf('|');
            }

            result.Add(separator < 0
                ? new ProjectBriefingRoleCharterEntry(string.Empty, line.Trim())
                : new ProjectBriefingRoleCharterEntry(
                    line[..separator].Trim(),
                    line[(separator + 1)..].Trim()));
        }

        return result;
    }

    private static IReadOnlyList<string> ParseSimpleLines(string? value)
        => (value ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

    public sealed class SaveInstitutionalProfileInput
    {
        [Range(1, long.MaxValue)]
        public long DeckId { get; set; }

        [Required]
        public string RowVersion { get; set; } = string.Empty;

        public bool IncludeInstitutionalProfile { get; set; }

        [StringLength(120)]
        public string? InstitutionalProfileTitle { get; set; }

        public bool IncludeInstitutionalHistory { get; set; }

        [StringLength(1200)]
        public string? InstitutionalHistoryLines { get; set; }

        public List<ProjectBriefingInstitutionalProfileModule> InstitutionalModules { get; set; } = new();

        [StringLength(300)]
        public string? InstitutionalModuleOrder { get; set; }
        public ProjectBriefingInstitutionalProjectScope InstitutionalProjectScope { get; set; }
            = ProjectBriefingInstitutionalProjectScope.OriginalCompleted;

        [Range(3, 7)]
        public int InstitutionalMaximumDetailRows { get; set; } = 6;

        [StringLength(80)]
        public string? InstitutionalTrainingHighlightCategory { get; set; }

        [StringLength(1200)]
        public string? InstitutionalPartnershipLines { get; set; }
        public bool IncludeInstitutionalFooterStrip { get; set; }

        [StringLength(160)]
        public string? InstitutionalFooterStripText { get; set; }

        [StringLength(40)]
        public string? InstitutionalFooterStripEmphasisValue { get; set; }
        public ProjectBriefingInstitutionalFooterStyle InstitutionalFooterStripStyle { get; set; }
            = ProjectBriefingInstitutionalFooterStyle.Outline;
        public ProjectBriefingInstitutionalFooterAlignment InstitutionalFooterStripAlignment { get; set; }
            = ProjectBriefingInstitutionalFooterAlignment.Center;
    }

    public sealed class SaveRoleCharterInput
    {
        [Range(1, long.MaxValue)]
        public long DeckId { get; set; }

        [Required]
        public string RowVersion { get; set; } = string.Empty;

        public bool IncludeRoleCharter { get; set; }

        [StringLength(120)]
        public string? RoleCharterTitle { get; set; }

        public ProjectBriefingRoleCharterLayout RoleCharterLayout { get; set; }
            = ProjectBriefingRoleCharterLayout.RoleAndTwoColumnCharter;

        public bool UseSharedRoleCharterContent { get; set; } = true;

        [StringLength(1600)]
        public string? RoleStatementLines { get; set; }

        [StringLength(6000)]
        public string? CharterItemLines { get; set; }
    }

    public sealed class SaveFfcGlobalFootprintInput
    {
        [Range(1, long.MaxValue)]
        public long DeckId { get; set; }

        [Required]
        public string RowVersion { get; set; } = string.Empty;

        public bool IncludeFfcGlobalFootprint { get; set; }

        [StringLength(120)]
        public string? FfcGlobalFootprintTitle { get; set; }

        [Required]
        public ProjectBriefingFfcGlobalFootprintLayout FfcGlobalFootprintLayout { get; set; }
            = ProjectBriefingFfcGlobalFootprintLayout.MapWithCountryBreakdown;

        [Range(6, 10)]
        public int MaximumCountryRows { get; set; } = 8;

        public bool IncludeCountryWiseBreakdown { get; set; }
    }

    // Key points go on the only project slide the content draws; with both, on the chosen one.
    internal static ProjectBriefingKeyPointPlacement ResolveKeyPointPlacement(SaveDeckSettingsInput input)
    {
        if (!input.ShowKeyPoints)
        {
            return ProjectBriefingKeyPointPlacement.None;
        }

        return input.NarrativeMode switch
        {
            ProjectBriefingNarrativeMode.CapabilityOverview => ProjectBriefingKeyPointPlacement.CapabilitySlide,
            ProjectBriefingNarrativeMode.ProjectBrief => ProjectBriefingKeyPointPlacement.ProjectBriefSlide,
            _ => input.KeyPointSlide == ProjectBriefingKeyPointPlacement.CapabilitySlide
                ? ProjectBriefingKeyPointPlacement.CapabilitySlide
                : ProjectBriefingKeyPointPlacement.ProjectBriefSlide
        };
    }

    public sealed class SaveDeckSettingsInput
    {
        [Required]
        public long DeckId { get; set; }

        [Required, StringLength(160, MinimumLength = 3)]
        public string Name { get; set; } = string.Empty;

        [StringLength(600)]
        public string? Description { get; set; }

        [Required]
        public ProjectBriefingLayout Layout { get; set; }

        // Not posted since the block list owns it (PR 4); kept for older pages and ignored.
        public ProjectBriefingPresentationMode PresentationMode { get; set; }

        [Required]
        public ProjectBriefingCostMode CostMode { get; set; }

        [Required]
        public ProjectBriefingNarrativeMode NarrativeMode { get; set; }

        [Required]
        public ProjectBriefingProjectBriefLayout ProjectBriefLayout { get; set; }
            = ProjectBriefingProjectBriefLayout.Automatic;

        public bool ShowPresentStage { get; set; } = true;
        public bool ShowPresentStatus { get; set; } = true;
        public bool IncludeStageDistributionTable { get; set; }

        // The project slide design (Deck tab, Project slides); null from a page without the cards.
        public ProjectBriefingProjectSlideDesign? ProjectDesign { get; set; }

        // Show key points, and the slide they go on when the deck draws both project slides.
        public bool ShowKeyPoints { get; set; }
        public ProjectBriefingKeyPointPlacement KeyPointSlide { get; set; }
            = ProjectBriefingKeyPointPlacement.ProjectBriefSlide;

        [Required]
        public ProjectBriefingPresentationTheme PresentationTheme { get; set; }

        [Required]
        public ProjectBriefingClosingSlideType ClosingSlideType { get; set; }
            = ProjectBriefingClosingSlideType.JaiHind;

        public bool IncludeInstitutionalProfile { get; set; }

        [StringLength(120)]
        public string? InstitutionalProfileTitle { get; set; }

        public bool IncludeInstitutionalHistory { get; set; } = true;

        [StringLength(1600)]
        public string? InstitutionalHistoryLines { get; set; }

        public List<ProjectBriefingInstitutionalProfileModule> InstitutionalModules { get; set; } = new();

        [StringLength(300)]
        public string? InstitutionalModuleOrder { get; set; }

        public ProjectBriefingInstitutionalProjectScope InstitutionalProjectScope { get; set; }
            = ProjectBriefingInstitutionalProjectScope.OriginalCompleted;

        [Range(3, 7)]
        public int InstitutionalMaximumDetailRows { get; set; } = 6;

        [StringLength(80)]
        public string? InstitutionalTrainingHighlightCategory { get; set; }

        [StringLength(1200)]
        public string? InstitutionalPartnershipLines { get; set; }

        public bool IncludeInstitutionalFooterStrip { get; set; }

        [StringLength(160)]
        public string? InstitutionalFooterStripText { get; set; }

        [StringLength(40)]
        public string? InstitutionalFooterStripEmphasisValue { get; set; }

        public ProjectBriefingInstitutionalFooterStyle InstitutionalFooterStripStyle { get; set; }
            = ProjectBriefingInstitutionalFooterStyle.Outline;

        public ProjectBriefingInstitutionalFooterAlignment InstitutionalFooterStripAlignment { get; set; }
            = ProjectBriefingInstitutionalFooterAlignment.Center;

        [Required]
        public ProjectBriefingBrandingScope BrandingScope { get; set; }

        public bool IncludeCoverSlide { get; set; }
        public bool IncludePortfolioSummarySlide { get; set; }
        public bool IncludeStageSummary { get; set; }
        public bool IncludeProjectCategorySummary { get; set; }
        public bool IncludeTechnicalCategorySummary { get; set; }

        public List<ProjectBriefingUpdateSheetRow> UpdateSheetRows { get; set; } = new();

        public string? UpdateSheetRowOrder { get; set; }

        public bool HideEmptyUpdateSheetValues { get; set; }

        [StringLength(80)]
        [RegularExpression(@"^[^\r\n]*$", ErrorMessage = "The handling/classification marking must be entered on one line.")]
        public string? HandlingMarking { get; set; }

        [Required]
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class AddSelectionInput
    {
        [Required]
        public long DeckId { get; set; }

        [Required]
        public ProjectBriefingSelectionKind Kind { get; set; }

        public List<int> ProjectCategoryIds { get; set; } = new();
        public List<int> TechnicalCategoryIds { get; set; } = new();
        public List<int> ProjectIds { get; set; } = new();
        public int? CompletionYearFrom { get; set; }
        public int? CompletionYearTo { get; set; }
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class UpdateMembershipInput
    {
        public long DeckId { get; set; }
        public List<int> AddProjectIds { get; set; } = new();
        public List<int> RemoveProjectIds { get; set; } = new();
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class ReorderInput
    {
        public long DeckId { get; set; }
        public List<int> ProjectIds { get; set; } = new();
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class UpdateBlocksInput
    {
        public long DeckId { get; set; }
        public string RowVersion { get; set; } = string.Empty;
        public List<UpdateBlockInput>? Blocks { get; set; }
    }

    public sealed class UpdateBlockInput
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ProjectBriefingDeckBlockType Type { get; set; }
        public bool Enabled { get; set; }
    }

    public sealed class ApplyPresetInput
    {
        public long DeckId { get; set; }
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public ProjectBriefingDeckPreset Preset { get; set; }
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class UpdateDescriptionInput
    {
        public long DeckId { get; set; }
        public int ProjectId { get; set; }
        public string? Value { get; set; }
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class UpdateSpeakerNotesInput
    {
        public long DeckId { get; set; }
        public int ProjectId { get; set; }
        public string? Notes { get; set; }
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class UpdateItemPhotoInput
    {
        public long DeckId { get; set; }
        public int ProjectId { get; set; }
        // Null goes back to the project's cover photo.
        public int? PhotoId { get; set; }
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class UpdateItemPresentationInput
    {
        public long DeckId { get; set; }
        public int ProjectId { get; set; }
        public ProjectBriefingItemPresentation Presentation { get; set; }
        public string RowVersion { get; set; } = string.Empty;
    }

    /// <summary>The MOLTI and SRE modules in which the signed-in officer has live projects assigned, for the rail.</summary>
    private async Task<IReadOnlyList<WorkspaceModuleLinkVm>> AssignedModuleLinksAsync(CancellationToken cancellationToken)
        => _itoWorkspace is null
            ? Array.Empty<WorkspaceModuleLinkVm>()
            : await _itoWorkspace.GetAssignedModuleLinksAsync(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), cancellationToken);
}
