using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Tot;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewTotTracker)]
public sealed class IndexModel : PageModel
{
    private readonly ProjectTotTrackerReadService _trackerService;
    private readonly ProjectTotService _totService;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(
        ProjectTotTrackerReadService trackerService,
        ProjectTotService totService,
        IAuthorizationService authorizationService,
        UserManager<ApplicationUser> userManager)
    {
        _trackerService = trackerService ?? throw new ArgumentNullException(nameof(trackerService));
        _totService = totService ?? throw new ArgumentNullException(nameof(totService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    [BindProperty(SupportsGet = true)]
    public ProjectTotStatus? TotStatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public ProjectTotRequestDecisionState? RequestStateFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyPending { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SelectedProjectId { get; set; }

    public IReadOnlyList<ProjectTotTrackerRow> Projects { get; private set; } = Array.Empty<ProjectTotTrackerRow>();

    public ProjectTotTrackerRow? SelectedProject { get; private set; }

    public bool CanSubmit { get; private set; }

    public bool CanApprove { get; private set; }

    [BindProperty]
    public SubmitRequestInput SubmitInput { get; set; } = new();

    [BindProperty]
    public DecideRequestInput DecideInput { get; set; } = new();

    public sealed class SubmitRequestInput
    {
        [HiddenInput]
        public int ProjectId { get; set; }

        public ProjectTotStatus Status { get; set; } = ProjectTotStatus.NotStarted;

        public DateOnly? StartedOn { get; set; }

        public DateOnly? CompletedOn { get; set; }

        public string? Remarks { get; set; }
    }

    public sealed class DecideRequestInput
    {
        [HiddenInput]
        public int ProjectId { get; set; }

        public bool Approve { get; set; }

        public string? Remarks { get; set; }

        public string? RowVersion { get; set; }
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PopulateAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSubmitAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!CanSubmit)
        {
            return Forbid();
        }

        SelectedProjectId = SubmitInput.ProjectId;

        if (!ModelState.IsValid)
        {
            await PopulateAsync(cancellationToken);
            return Page();
        }

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        var request = new ProjectTotUpdateRequest(
            SubmitInput.Status,
            SubmitInput.StartedOn,
            SubmitInput.CompletedOn,
            SubmitInput.Remarks);

        var result = await _totService.SubmitRequestAsync(
            SubmitInput.ProjectId,
            request,
            currentUserId,
            cancellationToken);

        if (result.Status == ProjectTotRequestActionStatus.NotFound)
        {
            return NotFound();
        }

        if (!result.IsSuccess)
        {
            var projectId = SubmitInput.ProjectId;
            var status = request.Status;
            var started = request.StartedOn;
            var completed = request.CompletedOn;
            var remarks = request.Remarks;
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to submit the Transfer of Technology update.");
            await PopulateAsync(cancellationToken);
            SubmitInput.ProjectId = projectId;
            SubmitInput.Status = status;
            SubmitInput.StartedOn = started;
            SubmitInput.CompletedOn = completed;
            SubmitInput.Remarks = remarks;
            return Page();
        }

        TempData["Toast"] = "Transfer of Technology update submitted for approval.";
        return RedirectToPage(new
        {
            TotStatusFilter,
            RequestStateFilter,
            OnlyPending,
            SelectedProjectId = SubmitInput.ProjectId
        });
    }

    public async Task<IActionResult> OnPostDecideAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!CanApprove)
        {
            return Forbid();
        }

        SelectedProjectId = DecideInput.ProjectId;

        if (!ModelState.IsValid)
        {
            await PopulateAsync(cancellationToken);
            return Page();
        }

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Challenge();
        }

        byte[]? rowVersion = null;
        if (!string.IsNullOrEmpty(DecideInput.RowVersion))
        {
            try
            {
                rowVersion = Convert.FromBase64String(DecideInput.RowVersion);
            }
            catch (FormatException)
            {
                ModelState.AddModelError(string.Empty, "The approval request could not be processed because the version token was invalid.");
                await PopulateAsync(cancellationToken);
                return Page();
            }
        }

        var result = await _totService.DecideRequestAsync(
            DecideInput.ProjectId,
            DecideInput.Approve,
            currentUserId,
            DecideInput.Remarks,
            rowVersion,
            cancellationToken);

        if (result.Status == ProjectTotRequestActionStatus.NotFound)
        {
            return NotFound();
        }

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to complete the Transfer of Technology decision.");
            var approveChoice = DecideInput.Approve;
            var remarks = DecideInput.Remarks;
            await PopulateAsync(cancellationToken);
            DecideInput.Approve = approveChoice;
            DecideInput.Remarks = remarks;
            return Page();
        }

        TempData["Toast"] = DecideInput.Approve
            ? "Transfer of Technology update approved."
            : "Transfer of Technology update rejected.";

        return RedirectToPage(new
        {
            TotStatusFilter,
            RequestStateFilter,
            OnlyPending,
            SelectedProjectId = DecideInput.ProjectId
        });
    }

    private async Task PopulateAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();

        var filter = new ProjectTotTrackerFilter
        {
            TotStatus = TotStatusFilter,
            RequestState = OnlyPending ? null : RequestStateFilter,
            OnlyPendingRequests = OnlyPending
        };

        Projects = await _trackerService.GetAsync(filter, cancellationToken);

        SelectedProject = SelectedProjectId.HasValue
            ? Projects.FirstOrDefault(p => p.ProjectId == SelectedProjectId.Value)
            : null;

        if (SelectedProject is { } selected)
        {
            SubmitInput = new SubmitRequestInput
            {
                ProjectId = selected.ProjectId,
                Status = selected.RequestedStatus ?? selected.TotStatus ?? ProjectTotStatus.NotStarted,
                StartedOn = selected.RequestedStartedOn ?? selected.TotStartedOn,
                CompletedOn = selected.RequestedCompletedOn ?? selected.TotCompletedOn,
                Remarks = selected.RequestedRemarks ?? selected.TotRemarks
            };

            DecideInput = new DecideRequestInput
            {
                ProjectId = selected.ProjectId,
                Approve = true,
                RowVersion = selected.RequestRowVersion is null
                    ? null
                    : Convert.ToBase64String(selected.RequestRowVersion)
            };
        }
        else
        {
            SubmitInput = new SubmitRequestInput();
            DecideInput = new DecideRequestInput();
        }
    }

    private async Task PopulatePermissionsAsync()
    {
        var submitResult = await _authorizationService.AuthorizeAsync(User, null, ProjectOfficeReportsPolicies.ManageTotTracker);
        CanSubmit = submitResult.Succeeded;

        var approveResult = await _authorizationService.AuthorizeAsync(User, null, ProjectOfficeReportsPolicies.ApproveTotTracker);
        CanApprove = approveResult.Succeeded;
    }
}
