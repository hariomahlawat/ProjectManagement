using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Utilities;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.ARPP;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageArpp)]
public sealed class CreateModel : PageModel
{
    private readonly IArppReadService _readService;
    private readonly IArppCommandService _commandService;
    private readonly IClock _clock;

    public CreateModel(
        IArppReadService readService,
        IArppCommandService commandService,
        IClock clock)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    [BindProperty]
    public ArppIssueInputModel Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var todayIst = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);
        Input.FinancialYearStart = FinancialYearHelper.GetStartYear(todayIst);
        Input.IssueDate = todayIst;

        var suggestedSequence = await _readService.GetSuggestedIssueSequenceAsync(
            Input.FinancialYearStart,
            cancellationToken);
        var originalExists = await _readService.HasOriginalIssueAsync(
            Input.FinancialYearStart,
            cancellationToken);

        Input.Kind = originalExists ? ArppIssueKind.Addendum : ArppIssueKind.Original;
        Input.IssueSequence = originalExists ? suggestedSequence : 0;
        ViewData["SuggestedAddendumSequence"] = suggestedSequence;
    }

    public async Task<IActionResult> OnGetSuggestionAsync(
        int financialYearStart,
        CancellationToken cancellationToken)
    {
        if (financialYearStart is < FinancialYearHelper.MinimumSupportedStartYear or > FinancialYearHelper.MaximumSupportedStartYear)
        {
            return BadRequest(new { message = "Enter a valid four-digit financial-year start." });
        }

        var originalExists = await _readService.HasOriginalIssueAsync(
            financialYearStart,
            cancellationToken);
        var suggestedSequence = await _readService.GetSuggestedIssueSequenceAsync(
            financialYearStart,
            cancellationToken);

        return new JsonResult(new
        {
            originalExists,
            suggestedKind = (int)(originalExists ? ArppIssueKind.Addendum : ArppIssueKind.Original),
            suggestedSequence = originalExists ? suggestedSequence : 0
        });
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewData["SuggestedAddendumSequence"] = await _readService.GetSuggestedIssueSequenceAsync(
                Input.FinancialYearStart,
                cancellationToken);
            return Page();
        }

        var result = await _commandService.CreateIssueAsync(
            new ArppIssueCreateCommand(
                Input.FinancialYearStart,
                Input.Kind!.Value,
                Input.IssueSequence,
                Input.Name,
                Input.IssueDate!.Value,
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                User.Identity?.Name),
            cancellationToken);

        if (!result.Success)
        {
            ApplyErrors(result);
            ViewData["SuggestedAddendumSequence"] = await _readService.GetSuggestedIssueSequenceAsync(
                Input.FinancialYearStart,
                cancellationToken);
            return Page();
        }

        TempData["StatusMessage"] = result.Message;
        if (result.Warnings.Count > 0)
        {
            TempData["ArppWarningMessage"] = string.Join(" ", result.Warnings);
        }

        return RedirectToPage("/ARPP/Manage", new { area = "ProjectOfficeReports", id = result.EntityId });
    }

    private void ApplyErrors(ArppCommandResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            ModelState.AddModelError(string.Empty, result.Message);
        }

        foreach (var pair in result.FieldErrors)
        {
            var key = pair.Key switch
            {
                "financialYearStart" or nameof(ArppIssueCreateCommand.FinancialYearStart) => "Input.FinancialYearStart",
                "kind" or nameof(ArppIssueCreateCommand.Kind) => "Input.Kind",
                "issueSequence" or nameof(ArppIssueCreateCommand.IssueSequence) => "Input.IssueSequence",
                "name" or nameof(ArppIssueCreateCommand.Name) => "Input.Name",
                "issueDate" or nameof(ArppIssueCreateCommand.IssueDate) => "Input.IssueDate",
                _ => string.Empty
            };

            foreach (var message in pair.Value)
            {
                ModelState.AddModelError(key, message);
            }
        }
    }
}
