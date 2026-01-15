using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Approvals;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Approvals.Pending;

[Authorize(Roles = "Admin,HoD")]
[ValidateAntiForgeryToken]
public class DecideModel : PageModel
{
    // SECTION: Dependencies
    private readonly ApprovalDecisionService _decisionService;

    public DecideModel(ApprovalDecisionService decisionService)
    {
        _decisionService = decisionService ?? throw new ArgumentNullException(nameof(decisionService));
    }

    // SECTION: Request input
    [BindProperty]
    public DecisionInput Input { get; set; } = new();

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!TryParseDecision(Input.Decision, out var action))
        {
            TempData["Error"] = "Decision must be Approve or Reject.";
            return RedirectToDetails();
        }

        var request = new ApprovalDecisionRequest(
            Input.ApprovalType,
            Input.RequestId,
            action,
            Input.Remarks,
            Input.RowVersion);

        var result = await _decisionService.DecideAsync(request, User, cancellationToken);

        switch (result.Outcome)
        {
            case ApprovalDecisionOutcome.Success:
                return RedirectToSuccessDestination(action);
            case ApprovalDecisionOutcome.AlreadyDecided:
                TempData["Error"] = result.Message ?? "This request has already been decided.";
                return RedirectToDetails(alreadyDecided: true);
            case ApprovalDecisionOutcome.ValidationFailed:
            case ApprovalDecisionOutcome.NotFound:
                TempData["Error"] = result.Message ?? "Unable to process the decision.";
                return RedirectToDetails();
            case ApprovalDecisionOutcome.Forbidden:
                return Forbid();
            default:
                TempData["Error"] = result.Message ?? "Unable to process the decision.";
                return RedirectToDetails();
        }
    }

    // SECTION: Decision input model
    public sealed class DecisionInput
    {
        public ApprovalQueueType ApprovalType { get; set; }
        public string RequestId { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty;
        public string? Remarks { get; set; }
        public string? RowVersion { get; set; }
        public string? ReturnUrl { get; set; }
    }

    // SECTION: Helpers
    // SECTION: Redirect helpers
    private IActionResult RedirectToSuccessDestination(ApprovalDecisionAction action)
    {
        if (!string.IsNullOrWhiteSpace(Input.ReturnUrl) && Url.IsLocalUrl(Input.ReturnUrl))
        {
            return Redirect(Input.ReturnUrl);
        }

        var success = action == ApprovalDecisionAction.Approve ? "approved" : "rejected";
        return RedirectToPage("/Approvals/Pending/Index", new { success });
    }

    private IActionResult RedirectToDetails(bool alreadyDecided = false)
    {
        return RedirectToPage(
            "/Approvals/Pending/Details",
            new
            {
                type = Input.ApprovalType.ToString(),
                id = Input.RequestId,
                alreadyDecided = alreadyDecided ? 1 : (int?)null,
                returnUrl = Input.ReturnUrl
            });
    }

    private static bool TryParseDecision(string? value, out ApprovalDecisionAction action)
    {
        if (string.Equals(value, "Approve", StringComparison.OrdinalIgnoreCase))
        {
            action = ApprovalDecisionAction.Approve;
            return true;
        }

        if (string.Equals(value, "Reject", StringComparison.OrdinalIgnoreCase))
        {
            action = ApprovalDecisionAction.Reject;
            return true;
        }

        action = default;
        return false;
    }
}
