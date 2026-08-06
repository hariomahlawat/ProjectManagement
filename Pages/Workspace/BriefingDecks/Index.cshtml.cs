using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
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
    private readonly IFfcFootprintService _ffcFootprintService;
    private readonly CommandWorkspaceService _commandWorkspaceService;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IProjectBriefingDeckService deckService,
        IProjectBriefingSelectionService selectionService,
        IProjectBriefingDataService dataService,
        IProjectBriefingPowerPointExportService exportService,
        IFfcFootprintService ffcFootprintService,
        CommandWorkspaceService commandWorkspaceService,
        UserManager<ApplicationUser> users,
        ILogger<IndexModel> logger)
    {
        _deckService = deckService ?? throw new ArgumentNullException(nameof(deckService));
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _ffcFootprintService = ffcFootprintService ?? throw new ArgumentNullException(nameof(ffcFootprintService));
        _commandWorkspaceService = commandWorkspaceService ?? throw new ArgumentNullException(nameof(commandWorkspaceService));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<ProjectBriefingDeckSummaryVm> Decks { get; private set; }
        = Array.Empty<ProjectBriefingDeckSummaryVm>();
    public ProjectBriefingDeckVm? SelectedDeck { get; private set; }
    public ProjectBriefingSelectionOptionsVm SelectionOptions { get; private set; } = new();
    public CommandWorkspaceRailVm CommandRail { get; private set; } = new() { ActiveView = "briefing-decks" };
    public FfcFootprintSummary FfcFootprintPreviewSummary { get; private set; } = new(0, 0, 0, 0, 0, 0);

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [TempData]
    public bool ReopenInstitutionalProfile { get; set; }

    [TempData]
    public bool ReopenRoleCharter { get; set; }

    [TempData]
    public bool ReopenFfcGlobalFootprint { get; set; }

    public async Task<IActionResult> OnGetAsync(long? deckId, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        await LoadPageAsync(userId, deckId, cancellationToken);
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
            var deckId = await _deckService.CreateAsync(userId, input.Name, input.Description, cancellationToken);
            StatusMessage = "New briefing deck created.";
            return RedirectToPage(new { deckId });
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
            StatusMessage = "Briefing deck deleted.";
            return RedirectToPage();
        }
        catch (KeyNotFoundException exception)
        {
            ErrorMessage = exception.Message;
            return RedirectToPage();
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "The deck was updated by another user and was not deleted. Reload and try again.";
            return RedirectToPage(new { deckId });
        }
    }

    public async Task<IActionResult> OnPostSaveSettingsAsync(
        [FromForm] SaveDeckSettingsInput input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (!ModelState.IsValid)
        {
            ErrorMessage = FirstModelError("Review the deck settings and try again.");
            return RedirectToPage(new { deckId = input.DeckId });
        }

        try
        {
            var existingDeck = await _deckService.GetEntityAsync(
                input.DeckId,
                userId,
                includeItems: false,
                cancellationToken)
                ?? throw new KeyNotFoundException("The shared command deck was not found.");
            var additionalSlides = ProjectBriefingDeckConfigurationCodec
                .Read(existingDeck.SelectionRulesJson);

            await _deckService.UpdateSettingsAsync(
                input.DeckId,
                userId,
                new ProjectBriefingDeckSettingsCommand
                {
                    Name = input.Name,
                    Description = input.Description,
                    Layout = input.Layout,
                    PresentationMode = input.PresentationMode,
                    CostMode = input.CostMode,
                    NarrativeMode = input.NarrativeMode,
                    ProjectBriefLayout = input.ProjectBriefLayout,
                    ShowPresentStage = input.ShowPresentStage,
                    ShowPresentStatus = input.ShowPresentStatus,
                    PresentationTheme = input.PresentationTheme,
                    ClosingSlideType = input.ClosingSlideType,
                    InstitutionalProfileOptions = additionalSlides.InstitutionalProfileOptions,
                    RoleCharterOptions = additionalSlides.RoleCharterOptions,
                    FfcGlobalFootprintOptions = additionalSlides.FfcGlobalFootprintOptions,
                    AdditionalSlideOrder = additionalSlides.AdditionalSlideOrder,
                    BrandingScope = input.BrandingScope,
                    IncludeCoverSlide = input.IncludeCoverSlide,
                    IncludePortfolioSummarySlide = input.IncludePortfolioSummarySlide,
                    IncludeStageSummary = input.IncludeStageSummary,
                    IncludeProjectCategorySummary = input.IncludeProjectCategorySummary,
                    IncludeTechnicalCategorySummary = input.IncludeTechnicalCategorySummary,
                    UpdateSheetRows = ResolveUpdateSheetRows(input.UpdateSheetRows, input.UpdateSheetRowOrder),
                    HideEmptyUpdateSheetValues = input.HideEmptyUpdateSheetValues,
                    HandlingMarking = input.HandlingMarking,
                    RowVersion = input.RowVersion
                },
                cancellationToken);
            StatusMessage = "Deck settings saved.";
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "This deck was updated by another user. Reload the page before saving.";
        }
        catch (DbUpdateException)
        {
            ErrorMessage = "The deck settings could not be saved. A deck with the same name may already exist.";
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage(new { deckId = input.DeckId });
    }

    public async Task<IActionResult> OnPostSaveInstitutionalProfileAsync(
        [FromForm] SaveInstitutionalProfileInput input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (!ModelState.IsValid)
        {
            ErrorMessage = FirstModelError("Review the SDD institutional profile and try again.");
            ReopenInstitutionalProfile = true;
            return RedirectToPage(new { deckId = input.DeckId });
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

            await _deckService.UpdateInstitutionalProfileAsync(
                input.DeckId,
                userId,
                options,
                input.RowVersion,
                cancellationToken);
            StatusMessage = "SDD institutional profile saved.";
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "This deck was updated by another user. Reload before saving the profile.";
            ReopenInstitutionalProfile = true;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
            ReopenInstitutionalProfile = true;
        }

        return RedirectToPage(new { deckId = input.DeckId });
    }

    public async Task<IActionResult> OnPostToggleInstitutionalProfileAsync(
        long deckId,
        bool enabled,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        try
        {
            var deck = await _deckService.GetEntityAsync(deckId, userId, includeItems: false, cancellationToken)
                ?? throw new KeyNotFoundException("The shared command deck was not found.");
            var current = ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson).InstitutionalProfileOptions;
            var options = ProjectBriefingInstitutionalProfileOptions.Normalize(
                enabled,
                current.Title,
                current.IncludeHistory,
                current.HistoryMilestones,
                current.Modules,
                current.ProjectScope,
                current.MaximumDetailRows,
                current.TrainingHighlightTechnicalCategory,
                current.PartnershipEntries,
                current.IncludeFooterStrip,
                current.FooterStripText,
                current.FooterStripEmphasisValue,
                current.FooterStripStyle,
                current.FooterStripAlignment);

            await _deckService.UpdateInstitutionalProfileAsync(
                deckId,
                userId,
                options,
                rowVersion,
                cancellationToken);
            StatusMessage = enabled
                ? "SDD institutional profile added to the deck."
                : "SDD institutional profile removed from the deck.";
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "This deck was updated by another user. Reload and try again.";
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
            ReopenInstitutionalProfile = enabled;
        }
        catch (KeyNotFoundException exception)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage(new { deckId });
    }

    public async Task<IActionResult> OnPostSaveRoleCharterAsync(
        [FromForm] SaveRoleCharterInput input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (!ModelState.IsValid)
        {
            ErrorMessage = FirstModelError("Review the Role & Charter slide and try again.");
            ReopenRoleCharter = true;
            return RedirectToPage(new { deckId = input.DeckId });
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
            await _deckService.UpdateRoleCharterAsync(
                input.DeckId,
                userId,
                options,
                input.RowVersion,
                cancellationToken);
            StatusMessage = "Role & Charter slide saved.";
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "This deck was updated by another user. Reload before saving Role & Charter.";
            ReopenRoleCharter = true;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
            ReopenRoleCharter = true;
        }

        return RedirectToPage(new { deckId = input.DeckId });
    }

    public async Task<IActionResult> OnPostSaveFfcGlobalFootprintAsync(
        [FromForm] SaveFfcGlobalFootprintInput input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (!ModelState.IsValid)
        {
            ErrorMessage = FirstModelError("Review the FFC Global Footprint slide and try again.");
            ReopenFfcGlobalFootprint = true;
            return RedirectToPage(new { deckId = input.DeckId });
        }

        try
        {
            var options = ProjectBriefingFfcGlobalFootprintOptions.Normalize(
                input.IncludeFfcGlobalFootprint,
                input.FfcGlobalFootprintTitle,
                input.MaximumCountryRows);
            await _deckService.UpdateFfcGlobalFootprintAsync(
                input.DeckId,
                userId,
                options,
                input.RowVersion,
                cancellationToken);
            StatusMessage = "FFC Global Footprint slide saved.";
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "This deck was updated by another user. Reload before saving the FFC Global Footprint slide.";
            ReopenFfcGlobalFootprint = true;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
            ReopenFfcGlobalFootprint = true;
        }

        return RedirectToPage(new { deckId = input.DeckId });
    }

    public async Task<IActionResult> OnPostToggleAdditionalSlideAsync(
        long deckId,
        ProjectBriefingAdditionalSlideType slideType,
        bool enabled,
        bool ensureAdded,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        try
        {
            await _deckService.ToggleAdditionalSlideAsync(
                deckId,
                userId,
                slideType,
                enabled,
                ensureAdded,
                rowVersion,
                cancellationToken);
            StatusMessage = enabled
                ? $"{AdditionalSlideLabel(slideType)} added to the deck."
                : $"{AdditionalSlideLabel(slideType)} disabled.";
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "This deck was updated by another user. Reload and try again.";
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
            ReopenInstitutionalProfile = enabled && slideType == ProjectBriefingAdditionalSlideType.InstitutionalProfile;
            ReopenRoleCharter = enabled && slideType == ProjectBriefingAdditionalSlideType.RoleAndCharter;
            ReopenFfcGlobalFootprint = enabled && slideType == ProjectBriefingAdditionalSlideType.FfcGlobalFootprint;
        }

        return RedirectToPage(new { deckId });
    }

    public async Task<IActionResult> OnPostMoveAdditionalSlideAsync(
        long deckId,
        ProjectBriefingAdditionalSlideType slideType,
        string direction,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        try
        {
            var deck = await _deckService.GetEntityAsync(deckId, userId, includeItems: false, cancellationToken)
                ?? throw new KeyNotFoundException("The shared command deck was not found.");
            var order = ProjectBriefingDeckConfigurationCodec.Read(deck.SelectionRulesJson).AdditionalSlideOrder.ToList();
            var index = order.IndexOf(slideType);
            var target = string.Equals(direction, "up", StringComparison.OrdinalIgnoreCase)
                ? index - 1
                : index + 1;
            if (index >= 0 && target >= 0 && target < order.Count)
            {
                (order[index], order[target]) = (order[target], order[index]);
                await _deckService.UpdateAdditionalSlideOrderAsync(
                    deckId,
                    userId,
                    order,
                    rowVersion,
                    cancellationToken);
                StatusMessage = "Additional slide order updated.";
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "This deck was updated by another user. Reload and try again.";
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage(new { deckId });
    }



    public async Task<IActionResult> OnPostReorderAdditionalSlidesAsync(
        long deckId,
        string? order,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        try
        {
            var parsed = (order ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Enum.TryParse<ProjectBriefingAdditionalSlideType>(value, ignoreCase: true, out var slideType)
                    ? slideType
                    : (ProjectBriefingAdditionalSlideType?)null)
                .Where(value => value.HasValue && Enum.IsDefined(value.Value))
                .Select(value => value!.Value)
                .Distinct()
                .ToArray();
            await _deckService.UpdateAdditionalSlideOrderAsync(
                deckId,
                userId,
                parsed,
                rowVersion,
                cancellationToken);
            StatusMessage = "Additional slide order updated.";
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "This deck was updated by another user. Reload and try again.";
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage(new { deckId });
    }

    public async Task<IActionResult> OnPostRemoveAdditionalSlideAsync(
        long deckId,
        ProjectBriefingAdditionalSlideType slideType,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        try
        {
            await _deckService.RemoveAdditionalSlideAsync(
                deckId,
                userId,
                slideType,
                rowVersion,
                cancellationToken);
            StatusMessage = $"{AdditionalSlideLabel(slideType)} removed from the deck.";
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "This deck was updated by another user. Reload and try again.";
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage(new { deckId });
    }

    public async Task<IActionResult> OnPostAddSelectionAsync(
        [FromForm] AddSelectionInput input,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (string.IsNullOrWhiteSpace(input.RowVersion))
        {
            ErrorMessage = "The deck version is missing. Reload and try again.";
            return RedirectToPage(new { deckId = input.DeckId });
        }

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
                    CompletionYearTo = input.CompletionYearTo
                },
                cancellationToken);

            if (selection.ProjectIds.Count == 0)
            {
                ErrorMessage = "No projects match the selected criteria.";
                return RedirectToPage(new { deckId = input.DeckId });
            }

            var added = await _deckService.AddProjectsAsync(
                input.DeckId,
                userId,
                selection.ProjectIds,
                selection.SelectionRulesJson,
                input.RowVersion,
                cancellationToken);
            StatusMessage = added == 0
                ? "All matching projects are already in this deck."
                : $"{added} project{(added == 1 ? string.Empty : "s")} added — {selection.RuleSummary}.";
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "The matching projects could not be added because another user updated the deck. Reload and try again.";
        }
        catch (DbUpdateException)
        {
            ErrorMessage = "The matching projects could not be added because of a database error. Reload and try again.";
        }

        return RedirectToPage(new { deckId = input.DeckId });
    }

    public async Task<IActionResult> OnPostRemoveProjectAsync(
        long deckId,
        int projectId,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        try
        {
            await _deckService.RemoveProjectAsync(deckId, projectId, userId, rowVersion, cancellationToken);
            StatusMessage = "Project removed from the deck.";
        }
        catch (KeyNotFoundException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (DbUpdateConcurrencyException)
        {
            ErrorMessage = "The deck was updated by another user. Reload the page before removing the project.";
        }

        return RedirectToPage(new { deckId });
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
                ?? throw new KeyNotFoundException("The shared command deck was not found.");

            return new JsonResult(new
            {
                saved = true,
                result.AddedCount,
                result.RemovedCount,
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
            return new JsonResult(new { saved = true, rowVersion });
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

    public async Task<IActionResult> OnGetSearchProjectsAsync(
        long deckId,
        string? query,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (deckId <= 0)
        {
            return BadRequest(new { message = "Select a valid shared deck before searching projects." });
        }

        var projects = await _selectionService.SearchAsync(query, 40, cancellationToken);
        var deck = await _deckService.GetEntityAsync(deckId, userId, includeItems: true, cancellationToken);
        if (deck is null)
        {
            return NotFound(new { message = "The shared command deck was not found." });
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
        Decks = await _deckService.ListAsync(userId, cancellationToken);
        var deckId = requestedDeckId ?? Decks.FirstOrDefault()?.Id;
        if (deckId.HasValue)
        {
            SelectedDeck = await _dataService.GetDeckAsync(deckId.Value, userId, cancellationToken);
            if (SelectedDeck is null && Decks.Count > 0)
            {
                SelectedDeck = await _dataService.GetDeckAsync(Decks[0].Id, userId, cancellationToken);
            }
        }

        SelectionOptions = await _selectionService.GetOptionsAsync(cancellationToken);
        if (SelectedDeck is not null)
        {
            try
            {
                var footprint = await _ffcFootprintService.GetAsync(
                    new FfcFootprintRequest(
                        Metric: FfcFootprintMetric.TotalUnits,
                        Sort: FfcFootprintSort.TotalUnits),
                    cancellationToken);
                FfcFootprintPreviewSummary = footprint.Summary;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "FFC footprint preview could not be loaded for the briefing-deck workspace. DeckId={DeckId}",
                    SelectedDeck.Id);
                FfcFootprintPreviewSummary = new FfcFootprintSummary(0, 0, 0, 0, 0, 0);
            }
        }

        var navigation = await _commandWorkspaceService.GetNavigationShellAsync("briefing-decks", cancellationToken);
        CommandRail = new CommandWorkspaceRailVm
        {
            CanSwitchWorkspace =
                (User.IsInRole(RoleNames.Comdt) || User.IsInRole(RoleNames.HoD))
                && User.IsInRole(RoleNames.ProjectOfficer),
            ActiveView = "briefing-decks",
            ProjectOfficerCount = navigation.ProjectOfficerCount,
            TotalOngoingProjects = navigation.TotalOngoingProjects
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

    private static string AdditionalSlideLabel(ProjectBriefingAdditionalSlideType slideType)
        => ProjectBriefingAdditionalSlideCatalog.IsRegistered(slideType)
            ? ProjectBriefingAdditionalSlideCatalog.Get(slideType).DisplayName
            : "Additional slide";

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

        [Range(6, 10)]
        public int MaximumCountryRows { get; set; } = 8;
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

        [Required]
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

    public sealed class UpdateDescriptionInput
    {
        public long DeckId { get; set; }
        public int ProjectId { get; set; }
        public string? Value { get; set; }
        public string RowVersion { get; set; } = string.Empty;
    }
}
