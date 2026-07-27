using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Utilities;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.ARPP;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageArpp)]
public sealed class ManageModel : PageModel
{
    private readonly IArppReadService _readService;
    private readonly IArppCommandService _commandService;
    private readonly IArppReferenceDataService _referenceDataService;
    private readonly IAuthorizationService _authorization;

    public ManageModel(
        IArppReadService readService,
        IArppCommandService commandService,
        IArppReferenceDataService referenceDataService,
        IAuthorizationService authorization)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        _referenceDataService = referenceDataService ?? throw new ArgumentNullException(nameof(referenceDataService));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public ArppWorkspaceInputModel Input { get; set; } = new();

    public ArppIssueDetails Issue { get; private set; } = default!;

    public IReadOnlyList<ArppCategory> Categories { get; } = Enum.GetValues<ArppCategory>();

    public IReadOnlyList<int> FinancialYearOptions { get; private set; } = [];

    public ArppReferenceDataSet ReferenceData { get; private set; } = new([], [], []);

    public bool CanManageReferenceData { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var issue = await _readService.GetIssueAsync(Id, cancellationToken);
        if (issue is null)
        {
            return NotFound();
        }

        if (issue.IsVerified)
        {
            TempData["ArppWarningMessage"] = "This ARPP issue is verified and locked. Unlock it from the issue page before making corrections.";
            return RedirectToPage("/ARPP/Details", new { area = "ProjectOfficeReports", id = Id });
        }

        SetIssue(issue, issue.FinancialYearStart);
        Input = Map(issue);
        await LoadReferenceDataAsync(Input.Entries, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var currentIssue = await _readService.GetIssueAsync(Id, cancellationToken);
        if (currentIssue is null)
        {
            return NotFound();
        }

        if (currentIssue.IsVerified)
        {
            TempData["ErrorMessage"] = "This ARPP issue is verified and locked. Unlock it before making corrections.";
            return RedirectToPage("/ARPP/Details", new { area = "ProjectOfficeReports", id = Id });
        }

        SetIssue(currentIssue, Input.FinancialYearStart);
        await LoadReferenceDataAsync(Input.Entries, cancellationToken);

        NormalizeCustomReferenceSelections();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _commandService.SaveWorkspaceAsync(
            new ArppWorkspaceSaveCommand(
                Id,
                Input.IssueRowVersion,
                Input.FinancialYearStart,
                Input.Kind!.Value,
                Input.IssueSequence,
                Input.Name,
                Input.IssueDate!.Value,
                Input.Entries.Select(entry => new ArppEntryInput(
                    entry.Id,
                    entry.RowVersion,
                    entry.SerialNumber,
                    entry.ProjectReference,
                    entry.ProjectId,
                    entry.Category,
                    entry.IpaCost,
                    NormalizeOptionId(entry.CfaOptionId),
                    entry.Cfa,
                    NormalizeOptionId(entry.FundOptionId),
                    entry.Fund,
                    NormalizeOptionId(entry.DfpdsScheduleId),
                    entry.DfpdsSchedule)).ToArray(),
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                User.Identity?.Name),
            cancellationToken);

        if (!result.Success)
        {
            ApplyErrors(result);
            return Page();
        }

        TempData["StatusMessage"] = result.Message;
        if (result.Warnings.Count > 0)
        {
            TempData["ArppWarningMessage"] = string.Join(" ", result.Warnings);
        }

        return RedirectToPage("/ARPP/Manage", new { area = "ProjectOfficeReports", id = Id });
    }

    private void SetIssue(ArppIssueDetails issue, int selectedFinancialYearStart)
    {
        Issue = issue;
        var selected = selectedFinancialYearStart is >= FinancialYearHelper.MinimumSupportedStartYear and <= FinancialYearHelper.MaximumSupportedStartYear
            ? selectedFinancialYearStart
            : issue.FinancialYearStart;
        var first = Math.Max(FinancialYearHelper.MinimumSupportedStartYear, Math.Min(issue.FinancialYearStart, selected) - 5);
        var last = Math.Min(FinancialYearHelper.MaximumSupportedStartYear, Math.Max(issue.FinancialYearStart, selected) + 4);
        FinancialYearOptions = Enumerable.Range(first, last - first + 1)
            .OrderByDescending(year => year)
            .ToArray();
    }

    private static ArppWorkspaceInputModel Map(ArppIssueDetails issue)
        => new()
        {
            IssueRowVersion = issue.RowVersion,
            FinancialYearStart = issue.FinancialYearStart,
            Kind = issue.Kind,
            IssueSequence = issue.IssueSequence,
            Name = issue.Name,
            IssueDate = issue.IssueDate,
            Entries = issue.Entries.Select(entry => new ArppEntryInputModel
            {
                Id = entry.Id,
                RowVersion = entry.RowVersion,
                SerialNumber = entry.SerialNumber,
                ProjectReference = entry.ProjectReference,
                ProjectId = entry.ProjectId,
                LinkedProjectName = entry.ProjectName,
                LinkedProjectMeta = BuildProjectMeta(entry),
                Category = entry.Category,
                IpaCost = entry.IpaCost,
                CfaOptionId = entry.CfaOptionId,
                Cfa = entry.Cfa,
                FundOptionId = entry.FundOptionId,
                Fund = entry.Fund,
                DfpdsScheduleId = entry.DfpdsScheduleId,
                DfpdsSchedule = entry.DfpdsSchedule
            }).ToList()
        };

    private static string? BuildProjectMeta(ArppEntryDetails entry)
    {
        if (!entry.ProjectId.HasValue)
        {
            return null;
        }

        return string.Join(" · ", new[] { entry.ProjectCaseFileNumber, entry.ProjectStatus }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private async Task LoadReferenceDataAsync(
        IReadOnlyCollection<ArppEntryInputModel> entries,
        CancellationToken cancellationToken)
    {
        ReferenceData = await _referenceDataService.GetWorkspaceOptionsAsync(
            entries.Where(item => item.CfaOptionId is > 0).Select(item => item.CfaOptionId!.Value).Distinct().ToArray(),
            entries.Where(item => item.FundOptionId is > 0).Select(item => item.FundOptionId!.Value).Distinct().ToArray(),
            entries.Where(item => item.DfpdsScheduleId is > 0).Select(item => item.DfpdsScheduleId!.Value).Distinct().ToArray(),
            cancellationToken);
        CanManageReferenceData = (await _authorization.AuthorizeAsync(User, AdminPolicies.MasterDataManage)).Succeeded;
    }

    private void NormalizeCustomReferenceSelections()
    {
        foreach (var entry in Input.Entries)
        {
            entry.CfaOptionId = NormalizeOptionId(entry.CfaOptionId);
            entry.FundOptionId = NormalizeOptionId(entry.FundOptionId);
            entry.DfpdsScheduleId = NormalizeOptionId(entry.DfpdsScheduleId);
        }
    }

    private static int? NormalizeOptionId(int? value) => value is > 0 ? value : null;

    private void ApplyErrors(ArppCommandResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            ModelState.AddModelError(string.Empty, result.Message);
        }

        foreach (var pair in result.FieldErrors)
        {
            var key = MapFieldKey(pair.Key);
            foreach (var message in pair.Value)
            {
                ModelState.AddModelError(key, message);
            }
        }
    }

    private static string MapFieldKey(string field)
        => field switch
        {
            "financialYearStart" or nameof(ArppWorkspaceSaveCommand.FinancialYearStart) => "Input.FinancialYearStart",
            "kind" or nameof(ArppWorkspaceSaveCommand.Kind) => "Input.Kind",
            "issueSequence" or nameof(ArppWorkspaceSaveCommand.IssueSequence) => "Input.IssueSequence",
            "name" or nameof(ArppWorkspaceSaveCommand.Name) => "Input.Name",
            "issueDate" or nameof(ArppWorkspaceSaveCommand.IssueDate) => "Input.IssueDate",
            "issueRowVersion" or nameof(ArppWorkspaceSaveCommand.IssueRowVersion) => "Input.IssueRowVersion",
            _ when field.StartsWith("Entries[", StringComparison.Ordinal) => $"Input.{field}",
            _ => string.Empty
        };
}
