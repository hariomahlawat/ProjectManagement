using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.ARPP;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageArpp)]
public sealed class ManageModel : PageModel
{
    private readonly IArppReadService _readService;
    private readonly IArppCommandService _commandService;

    public ManageModel(
        IArppReadService readService,
        IArppCommandService commandService)
    {
        _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
    }

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public ArppWorkspaceInputModel Input { get; set; } = new();

    public ArppIssueDetails Issue { get; private set; } = default!;

    public IReadOnlyList<ArppCategory> Categories { get; } = Enum.GetValues<ArppCategory>();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var issue = await _readService.GetIssueAsync(Id, cancellationToken);
        if (issue is null)
        {
            return NotFound();
        }

        Issue = issue;
        Input = Map(issue);
        EnsureAtLeastOneEntry();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var currentIssue = await _readService.GetIssueAsync(Id, cancellationToken);
        if (currentIssue is null)
        {
            return NotFound();
        }

        Issue = currentIssue;

        if (!ModelState.IsValid)
        {
            EnsureAtLeastOneEntry();
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
                    entry.Cfa,
                    entry.Fund,
                    entry.DfpdsSchedule)).ToArray(),
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                User.Identity?.Name),
            cancellationToken);

        if (!result.Success)
        {
            ApplyErrors(result);
            EnsureAtLeastOneEntry();
            return Page();
        }

        TempData["StatusMessage"] = result.Message;
        if (result.Warnings.Count > 0)
        {
            TempData["ArppWarningMessage"] = string.Join(" ", result.Warnings);
        }

        return RedirectToPage("/ARPP/Manage", new { area = "ProjectOfficeReports", id = Id });
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
                Cfa = entry.Cfa,
                Fund = entry.Fund,
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

    private void EnsureAtLeastOneEntry()
    {
        if (Input.Entries.Count == 0)
        {
            Input.Entries.Add(new ArppEntryInputModel
            {
                Category = ArppCategory.New
            });
        }
    }

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
