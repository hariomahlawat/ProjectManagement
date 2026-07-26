using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Services.Arpp;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.ARPP;

[Authorize(Policy = ProjectOfficeReportsPolicies.ManageArpp)]
public sealed class ReconcileModel : PageModel
{
    private readonly IArppReconciliationService _reconciliationService;

    public ReconcileModel(IArppReconciliationService reconciliationService)
    {
        _reconciliationService = reconciliationService ?? throw new ArgumentNullException(nameof(reconciliationService));
    }

    [BindProperty(SupportsGet = true, Name = "fy")]
    public int? FinancialYearStart { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty]
    public ArppReconciliationInputModel Input { get; set; } = new();

    public ArppReconciliationResult Queue { get; private set; } = default!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
        => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var links = Input.Links
            .Where(link => link.ProjectId.HasValue)
            .Select(link => new ArppReconciliationLinkInput(
                link.EntryId,
                link.EntryRowVersion,
                link.ProjectId!.Value))
            .ToArray();

        var result = await _reconciliationService.LinkAsync(
            new ArppReconciliationCommand(
                links,
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                User.Identity?.Name),
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "The selected rows could not be linked.");
            foreach (var pair in result.FieldErrors)
            {
                foreach (var message in pair.Value)
                {
                    ModelState.AddModelError(string.Empty, message);
                }
            }

            await LoadAsync(cancellationToken, preserveInput: true);
            return Page();
        }

        TempData["StatusMessage"] = result.Message;
        return RedirectToPage("/ARPP/Reconcile", new
        {
            area = "ProjectOfficeReports",
            fy = FinancialYearStart,
            q = Query
        });
    }

    private async Task LoadAsync(
        CancellationToken cancellationToken,
        bool preserveInput = false)
    {
        Queue = await _reconciliationService.GetQueueAsync(
            FinancialYearStart,
            Query,
            cancellationToken: cancellationToken);

        if (preserveInput)
        {
            var byEntry = Input.Links.ToDictionary(link => link.EntryId);
            Input.Links = Queue.Items.Select(item => byEntry.TryGetValue(item.EntryId, out var existing)
                ? existing
                : CreateInput(item)).ToList();
        }
        else
        {
            Input.Links = Queue.Items.Select(CreateInput).ToList();
        }
    }

    private static ArppReconciliationLinkInputModel CreateInput(ArppReconciliationItem item)
        => new()
        {
            EntryId = item.EntryId,
            EntryRowVersion = item.EntryRowVersion
        };
}
