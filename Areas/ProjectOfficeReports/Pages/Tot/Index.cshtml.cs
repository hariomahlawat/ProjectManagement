using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
    private readonly ProjectTotUpdateService _totUpdateService;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(
        ProjectTotTrackerReadService trackerService,
        ProjectTotService totService,
        ProjectTotUpdateService totUpdateService,
        IAuthorizationService authorizationService,
        UserManager<ApplicationUser> userManager)
    {
        _trackerService = trackerService ?? throw new ArgumentNullException(nameof(trackerService));
        _totService = totService ?? throw new ArgumentNullException(nameof(totService));
        _totUpdateService = totUpdateService ?? throw new ArgumentNullException(nameof(totUpdateService));
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

    public IReadOnlyList<ProjectTotProgressUpdateView> TotUpdates { get; private set; } = Array.Empty<ProjectTotProgressUpdateView>();

    public bool CanSubmit { get; private set; }

    public bool CanApprove { get; private set; }

    public bool CanSubmitUpdatesForSelectedProject { get; private set; }

    [BindProperty]
    public SubmitRequestInput SubmitInput { get; set; } = new();

    [BindProperty]
    public DecideRequestInput DecideInput { get; set; } = new();

    [BindProperty]
    public SubmitUpdateInput SubmitUpdateInput { get; set; } = new();

    [BindProperty]
    public DecideUpdateInput DecideUpdateInput { get; set; } = new();

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

    public sealed class SubmitUpdateInput
    {
        [HiddenInput]
        public int ProjectId { get; set; }

        [Required]
        [StringLength(2000)]
        public string Body { get; set; } = string.Empty;

        public DateOnly? EventDate { get; set; }
    }

    public sealed class DecideUpdateInput
    {
        [HiddenInput]
        public int ProjectId { get; set; }

        [HiddenInput]
        public int UpdateId { get; set; }

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

    public async Task<IActionResult> OnPostSubmitUpdateAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!CanSubmit)
        {
            return Forbid();
        }

        SelectedProjectId = SubmitUpdateInput.ProjectId;

        var postedBody = SubmitUpdateInput.Body;
        var postedEventDate = SubmitUpdateInput.EventDate;

        await PopulateAsync(cancellationToken);

        var selected = SelectedProject;
        if (selected is null)
        {
            return NotFound();
        }

        SubmitUpdateInput.Body = postedBody;
        SubmitUpdateInput.EventDate = postedEventDate;
        SubmitUpdateInput.ProjectId = selected.ProjectId;

        if (!IsAuthorizedProjectOfficer(selected))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _totUpdateService.SubmitAsync(
            selected.ProjectId,
            SubmitUpdateInput.Body,
            SubmitUpdateInput.EventDate,
            User,
            cancellationToken);

        if (result.Status == ProjectTotProgressUpdateActionStatus.NotFound)
        {
            return NotFound();
        }

        if (result.Status == ProjectTotProgressUpdateActionStatus.Forbidden)
        {
            return Forbid();
        }

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to submit the Transfer of Technology update.");
            await PopulateAsync(cancellationToken);
            SubmitUpdateInput.Body = postedBody;
            SubmitUpdateInput.EventDate = postedEventDate;
            SubmitUpdateInput.ProjectId = selected.ProjectId;
            return Page();
        }

        var toast = User.IsInRole("Admin") || User.IsInRole("HoD")
            ? "Transfer of Technology update published."
            : "Transfer of Technology update submitted for approval.";
        TempData["Toast"] = toast;

        return RedirectToPage(new
        {
            TotStatusFilter,
            RequestStateFilter,
            OnlyPending,
            SelectedProjectId = selected.ProjectId
        });
    }

    public async Task<IActionResult> OnPostDecideUpdateAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!CanApprove)
        {
            return Forbid();
        }

        SelectedProjectId = DecideUpdateInput.ProjectId;

        var postedRemarks = DecideUpdateInput.Remarks;
        var postedUpdateId = DecideUpdateInput.UpdateId;
        var postedApprove = DecideUpdateInput.Approve;
        var postedRowVersion = DecideUpdateInput.RowVersion;

        await PopulateAsync(cancellationToken);

        var selected = SelectedProject;
        if (selected is null)
        {
            return NotFound();
        }

        DecideUpdateInput.ProjectId = selected.ProjectId;
        DecideUpdateInput.UpdateId = postedUpdateId;
        DecideUpdateInput.Approve = postedApprove;
        DecideUpdateInput.RowVersion = postedRowVersion;
        DecideUpdateInput.Remarks = postedRemarks;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        byte[]? rowVersion = null;
        if (!string.IsNullOrEmpty(DecideUpdateInput.RowVersion))
        {
            try
            {
                rowVersion = Convert.FromBase64String(DecideUpdateInput.RowVersion);
            }
            catch (FormatException)
            {
                ModelState.AddModelError($"DecideUpdateInput.{postedUpdateId}", "The update could not be processed because the version token was invalid.");
                return Page();
            }
        }

        var result = await _totUpdateService.DecideAsync(
            selected.ProjectId,
            DecideUpdateInput.UpdateId,
            DecideUpdateInput.Approve,
            DecideUpdateInput.Remarks,
            rowVersion,
            User,
            cancellationToken);

        if (result.Status == ProjectTotProgressUpdateActionStatus.NotFound)
        {
            return NotFound();
        }

        if (result.Status == ProjectTotProgressUpdateActionStatus.Forbidden)
        {
            return Forbid();
        }

        if (!result.IsSuccess)
        {
            ModelState.AddModelError($"DecideUpdateInput.{postedUpdateId}", result.ErrorMessage ?? "Unable to complete the Transfer of Technology decision.");
            await PopulateAsync(cancellationToken);
            DecideUpdateInput.ProjectId = selected.ProjectId;
            DecideUpdateInput.UpdateId = postedUpdateId;
            DecideUpdateInput.Approve = postedApprove;
            DecideUpdateInput.RowVersion = postedRowVersion;
            DecideUpdateInput.Remarks = postedRemarks;
            return Page();
        }

        TempData["Toast"] = DecideUpdateInput.Approve
            ? "Transfer of Technology update approved."
            : "Transfer of Technology update rejected.";

        return RedirectToPage(new
        {
            TotStatusFilter,
            RequestStateFilter,
            OnlyPending,
            SelectedProjectId = selected.ProjectId
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
            await LoadUpdatesAsync(selected.ProjectId, cancellationToken);

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

            SubmitUpdateInput = new SubmitUpdateInput
            {
                ProjectId = selected.ProjectId
            };

            DecideUpdateInput = new DecideUpdateInput
            {
                ProjectId = selected.ProjectId
            };

            CanSubmitUpdatesForSelectedProject = CanSubmit && IsAuthorizedProjectOfficer(selected);
        }
        else
        {
            SubmitInput = new SubmitRequestInput();
            DecideInput = new DecideRequestInput();
            TotUpdates = Array.Empty<ProjectTotProgressUpdateView>();
            SubmitUpdateInput = new SubmitUpdateInput();
            DecideUpdateInput = new DecideUpdateInput();
            CanSubmitUpdatesForSelectedProject = false;
        }
    }

    private bool IsAuthorizedProjectOfficer(ProjectTotTrackerRow project)
    {
        var isProjectOfficer = User.IsInRole("Project Officer") || User.IsInRole("ProjectOfficer");
        if (!isProjectOfficer)
        {
            return true;
        }

        var currentUserId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return false;
        }

        if (string.IsNullOrEmpty(project.LeadProjectOfficerUserId))
        {
            return false;
        }

        return string.Equals(project.LeadProjectOfficerUserId, currentUserId, StringComparison.Ordinal);
    }

    private async Task LoadUpdatesAsync(int projectId, CancellationToken cancellationToken)
    {
        var result = await _totUpdateService.GetUpdatesAsync(projectId, cancellationToken);
        TotUpdates = result.IsSuccess ? result.Updates : Array.Empty<ProjectTotProgressUpdateView>();
    }

    private async Task PopulatePermissionsAsync()
    {
        var submitResult = await _authorizationService.AuthorizeAsync(User, null, ProjectOfficeReportsPolicies.ManageTotTracker);
        CanSubmit = submitResult.Succeeded;

        var approveResult = await _authorizationService.AuthorizeAsync(User, null, ProjectOfficeReportsPolicies.ApproveTotTracker);
        CanApprove = approveResult.Succeeded;
    }
}
