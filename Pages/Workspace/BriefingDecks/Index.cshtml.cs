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
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IProjectBriefingDeckService deckService,
        IProjectBriefingSelectionService selectionService,
        IProjectBriefingDataService dataService,
        IProjectBriefingPowerPointExportService exportService,
        CommandWorkspaceService commandWorkspaceService,
        UserManager<ApplicationUser> users,
        ILogger<IndexModel> logger)
    {
        _deckService = deckService ?? throw new ArgumentNullException(nameof(deckService));
        _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _commandWorkspaceService = commandWorkspaceService ?? throw new ArgumentNullException(nameof(commandWorkspaceService));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<ProjectBriefingDeckSummaryVm> Decks { get; private set; }
        = Array.Empty<ProjectBriefingDeckSummaryVm>();
    public ProjectBriefingDeckVm? SelectedDeck { get; private set; }
    public ProjectBriefingSelectionOptionsVm SelectionOptions { get; private set; } = new();
    public CommandWorkspaceRailVm CommandRail { get; private set; } = new() { ActiveView = "briefing-decks" };

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

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
            await _deckService.UpdateSettingsAsync(
                input.DeckId,
                userId,
                new ProjectBriefingDeckSettingsCommand
                {
                    Name = input.Name,
                    Description = input.Description,
                    PresentationMode = input.PresentationMode,
                    CostMode = input.CostMode,
                    IncludeStageSummary = input.IncludeStageSummary,
                    IncludeProjectCategorySummary = input.IncludeProjectCategorySummary,
                    IncludeTechnicalCategorySummary = input.IncludeTechnicalCategorySummary,
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
        string? query,
        CancellationToken cancellationToken)
    {
        _ = RequireUserId();
        var projects = await _selectionService.SearchAsync(query, 30, cancellationToken);
        return new JsonResult(projects);
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

    public sealed class SaveDeckSettingsInput
    {
        [Required]
        public long DeckId { get; set; }

        [Required, StringLength(160, MinimumLength = 3)]
        public string Name { get; set; } = string.Empty;

        [StringLength(600)]
        public string? Description { get; set; }

        [Required]
        public ProjectBriefingPresentationMode PresentationMode { get; set; }

        [Required]
        public ProjectBriefingCostMode CostMode { get; set; }

        public bool IncludeStageSummary { get; set; }
        public bool IncludeProjectCategorySummary { get; set; }
        public bool IncludeTechnicalCategorySummary { get; set; }

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
