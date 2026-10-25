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

    public IReadOnlyList<int> FinancialYearOptions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var todayIst = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);
        Input.FinancialYearStart = FinancialYearHelper.GetStartYear(todayIst);
        Input.IssueDate = todayIst;
        BuildFinancialYearOptions(Input.FinancialYearStart);

        var originalExists = await _readService.HasOriginalIssueAsync(
            Input.FinancialYearStart,
            cancellationToken);

        Input.Kind = originalExists ? ArppIssueKind.Addendum : ArppIssueKind.Original;
        var suggestedSequence = await _readService.GetSuggestedIssueSequenceAsync(
            Input.FinancialYearStart,
            Input.Kind.Value,
            cancellationToken);
        Input.IssueSequence = suggestedSequence;
        ViewData["SuggestedAddendumSequence"] = suggestedSequence;
    }

    /// <summary>
    /// Suggests the issue type and next number for a financial year. When <paramref name="kind"/>
    /// is given, the number is for that kind (addenda and amendment letters are numbered separately);
    /// otherwise the kind itself is suggested from whether an original already exists.
    /// </summary>
    public async Task<IActionResult> OnGetSuggestionAsync(
        int financialYearStart,
        ArppIssueKind? kind,
        CancellationToken cancellationToken)
    {
        if (financialYearStart is < FinancialYearHelper.MinimumSupportedStartYear or > FinancialYearHelper.MaximumSupportedStartYear)
        {
            return BadRequest(new { message = "Select a valid financial year." });
        }

        if (kind.HasValue && !Enum.IsDefined(kind.Value))
        {
            return BadRequest(new { message = "Select a valid issue type." });
        }

        var originalExists = await _readService.HasOriginalIssueAsync(
            financialYearStart,
            cancellationToken);
        var suggestedKind = kind ?? (originalExists ? ArppIssueKind.Addendum : ArppIssueKind.Original);
        var suggestedSequence = await _readService.GetSuggestedIssueSequenceAsync(
            financialYearStart,
            suggestedKind,
            cancellationToken);

        return new JsonResult(new
        {
            originalExists,
            suggestedKind = (int)suggestedKind,
            suggestedSequence
        });
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        BuildFinancialYearOptions(Input.FinancialYearStart);

        if (!ModelState.IsValid)
        {
            ViewData["SuggestedAddendumSequence"] = await _readService.GetSuggestedIssueSequenceAsync(
                Input.FinancialYearStart,
                Input.Kind ?? ArppIssueKind.Addendum,
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
                Input.Kind ?? ArppIssueKind.Addendum,
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

    private void BuildFinancialYearOptions(int selectedYear)
    {
        var todayIst = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);
        var current = FinancialYearHelper.GetStartYear(todayIst);
        var safeSelected = Math.Clamp(
            selectedYear,
            FinancialYearHelper.MinimumSupportedStartYear,
            FinancialYearHelper.MaximumSupportedStartYear);
        var start = Math.Max(
            FinancialYearHelper.MinimumSupportedStartYear,
            Math.Min(current - 5, safeSelected - 2));
        var end = Math.Min(
            FinancialYearHelper.MaximumSupportedStartYear,
            Math.Max(current + 3, safeSelected + 2));

        FinancialYearOptions = Enumerable.Range(start, end - start + 1)
            .OrderByDescending(year => year)
            .ToArray();
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
