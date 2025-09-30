using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects.Meta;

[Authorize(Roles = "Admin,HoD")]
[AutoValidateAntiforgeryToken]
public sealed class DecideModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ProjectMetaChangeDecisionService _decisionService;
    private readonly IUserContext _userContext;

    public DecideModel(ApplicationDbContext db, ProjectMetaChangeDecisionService decisionService, IUserContext userContext)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _decisionService = decisionService ?? throw new ArgumentNullException(nameof(decisionService));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    }

    [BindProperty]
    public DecisionInput Input { get; set; } = new();

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Unable to process the decision. Please try again.";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var request = await _db.ProjectMetaChangeRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == Input.RequestId, cancellationToken);

        if (request is null || request.ProjectId != id)
        {
            TempData["Error"] = "Change request could not be found.";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        if (!TryParseAction(Input.Action, out var action))
        {
            TempData["Error"] = "Unsupported decision action.";
            return RedirectToPage("/Projects/Overview", new { id });
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Forbid();
        }

        var principal = _userContext.User;
        var decisionUser = new ProjectMetaDecisionUser(
            userId,
            principal.IsInRole("Admin"),
            principal.IsInRole("HoD"));

        var decisionInput = new ProjectMetaDecisionInput(
            Input.RequestId,
            action,
            string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note);

        var result = await _decisionService.DecideAsync(decisionInput, decisionUser, cancellationToken);

        switch (result.Outcome)
        {
            case ProjectMetaDecisionOutcome.Success:
                TempData["Flash"] = action == ProjectMetaDecisionAction.Approve
                    ? "Project details updated from change request."
                    : "Change request rejected.";
                break;
            case ProjectMetaDecisionOutcome.Forbidden:
                TempData["Error"] = "You are not authorised to decide on this request.";
                break;
            case ProjectMetaDecisionOutcome.RequestNotFound:
                TempData["Error"] = "Change request could not be found.";
                break;
            case ProjectMetaDecisionOutcome.AlreadyDecided:
                TempData["Flash"] = "This change request has already been processed.";
                break;
            case ProjectMetaDecisionOutcome.ValidationFailed:
                TempData["Error"] = result.Error ?? "Unable to apply the proposed changes.";
                break;
            default:
                TempData["Error"] = "Unable to process the decision.";
                break;
        }

        return RedirectToPage("/Projects/Overview", new { id });
    }

    private static bool TryParseAction(string value, out ProjectMetaDecisionAction action)
    {
        if (string.Equals(value, "Approve", StringComparison.OrdinalIgnoreCase))
        {
            action = ProjectMetaDecisionAction.Approve;
            return true;
        }

        if (string.Equals(value, "Reject", StringComparison.OrdinalIgnoreCase))
        {
            action = ProjectMetaDecisionAction.Reject;
            return true;
        }

        action = default;
        return false;
    }

    public sealed class DecisionInput
    {
        [Required]
        public int RequestId { get; set; }

        [Required]
        public string Action { get; set; } = string.Empty;

        [StringLength(1024)]
        public string? Note { get; set; }
    }
}
