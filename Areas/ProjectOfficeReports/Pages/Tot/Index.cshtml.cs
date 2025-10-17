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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Remarks;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Tot;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewTotTracker)]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ProjectTotTrackerReadService _trackerService;
    private readonly ProjectTotService _totService;
    private readonly ProjectTotUpdateService _totUpdateService;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRemarkService _remarkService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext db,
        ProjectTotTrackerReadService trackerService,
        ProjectTotService totService,
        ProjectTotUpdateService totUpdateService,
        IAuthorizationService authorizationService,
        UserManager<ApplicationUser> userManager,
        IRemarkService remarkService,
        ILogger<IndexModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _trackerService = trackerService ?? throw new ArgumentNullException(nameof(trackerService));
        _totService = totService ?? throw new ArgumentNullException(nameof(totService));
        _totUpdateService = totUpdateService ?? throw new ArgumentNullException(nameof(totUpdateService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _remarkService = remarkService ?? throw new ArgumentNullException(nameof(remarkService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public ProjectTotStatus? TotStatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public ProjectTotRequestDecisionState? RequestStateFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyPending { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SelectedProjectId { get; set; }

    private string? _searchTerm;

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm
    {
        get => _searchTerm;
        set => _searchTerm = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

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
    public SubmitUpdateInput SubmitUpdate { get; set; } = new();

    [BindProperty]
    public DecideUpdateInput DecideUpdate { get; set; } = new();

    [BindProperty]
    public string? SubmitContextBody { get; set; }

    [BindProperty]
    public string? DecideContextBody { get; set; }

    public sealed class SubmitRequestInput
    {
        [HiddenInput]
        public int ProjectId { get; set; }

        public ProjectTotStatus Status { get; set; } = ProjectTotStatus.NotStarted;

        public DateOnly? StartedOn { get; set; }

        public DateOnly? CompletedOn { get; set; }

        public string? MetDetails { get; set; }

        public DateOnly? MetCompletedOn { get; set; }

        public bool? FirstProductionModelManufactured { get; set; }

        public DateOnly? FirstProductionModelManufacturedOn { get; set; }
    }

    public sealed class DecideRequestInput
    {
        [HiddenInput]
        public int ProjectId { get; set; }

        public bool Approve { get; set; }

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

        var submitContext = NormalizeRemarkBody(SubmitContextBody);
        if (!ValidateRemarkBody(submitContext, nameof(SubmitContextBody)))
        {
            await PopulateAsync(cancellationToken);
            SubmitContextBody = submitContext ?? SubmitContextBody;
            return Page();
        }

        if (!ModelState.IsValid)
        {
            await PopulateAsync(cancellationToken);
            SubmitContextBody = submitContext;
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
            SubmitInput.MetDetails,
            SubmitInput.MetCompletedOn,
            SubmitInput.FirstProductionModelManufactured,
            SubmitInput.FirstProductionModelManufacturedOn);

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
            var metDetails = request.MetDetails;
            var metCompletedOn = request.MetCompletedOn;
            var firstProductionModelManufactured = request.FirstProductionModelManufactured;
            var firstProductionModelManufacturedOn = request.FirstProductionModelManufacturedOn;
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to submit the Transfer of Technology update.");
            await PopulateAsync(cancellationToken);
            SubmitInput.ProjectId = projectId;
            SubmitInput.Status = status;
            SubmitInput.StartedOn = started;
            SubmitInput.CompletedOn = completed;
            SubmitInput.MetDetails = metDetails;
            SubmitInput.MetCompletedOn = metCompletedOn;
            SubmitInput.FirstProductionModelManufactured = firstProductionModelManufactured;
            SubmitInput.FirstProductionModelManufacturedOn = firstProductionModelManufacturedOn;
            SubmitContextBody = submitContext;
            return Page();
        }

        var (remarkSuccess, remarkError) = await TryCreateTotRemarkAsync(SubmitInput.ProjectId, submitContext, cancellationToken);
        var toast = "Transfer of Technology update submitted for approval.";
        if (!remarkSuccess)
        {
            toast += remarkError is { Length: > 0 }
                ? $" However, the remark could not be saved: {remarkError}"
                : " However, the remark could not be saved.";
        }

        TempData["Toast"] = toast;
        return RedirectToPage(new
        {
            TotStatusFilter,
            RequestStateFilter,
            SearchTerm,
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

        var decisionContext = NormalizeRemarkBody(DecideContextBody);
        if (!ValidateRemarkBody(decisionContext, nameof(DecideContextBody)))
        {
            await PopulateAsync(cancellationToken);
            DecideContextBody = decisionContext ?? DecideContextBody;
            return Page();
        }

        if (!ModelState.IsValid)
        {
            await PopulateAsync(cancellationToken);
            DecideContextBody = decisionContext;
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
                DecideContextBody = decisionContext;
                return Page();
            }
        }

        var result = await _totService.DecideRequestAsync(
            DecideInput.ProjectId,
            DecideInput.Approve,
            currentUserId,
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
            await PopulateAsync(cancellationToken);
            DecideInput.Approve = approveChoice;
            DecideContextBody = decisionContext;
            return Page();
        }

        var (remarkSuccess, remarkError) = await TryCreateTotRemarkAsync(DecideInput.ProjectId, decisionContext, cancellationToken);
        var toast = DecideInput.Approve
            ? "Transfer of Technology update approved."
            : "Transfer of Technology update rejected.";

        if (!remarkSuccess)
        {
            toast += remarkError is { Length: > 0 }
                ? $" However, the remark could not be saved: {remarkError}"
                : " However, the remark could not be saved.";
        }

        TempData["Toast"] = toast;

        return RedirectToPage(new
        {
            TotStatusFilter,
            RequestStateFilter,
            SearchTerm,
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

        SelectedProjectId = SubmitUpdate.ProjectId;

        var postedBody = SubmitUpdate.Body;
        var postedEventDate = SubmitUpdate.EventDate;

        await PopulateAsync(cancellationToken);

        var selected = SelectedProject;
        if (selected is null)
        {
            return NotFound();
        }

        SubmitUpdate.Body = postedBody;
        SubmitUpdate.EventDate = postedEventDate;
        SubmitUpdate.ProjectId = selected.ProjectId;

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
            SubmitUpdate.Body,
            SubmitUpdate.EventDate,
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
            SubmitUpdate.Body = postedBody;
            SubmitUpdate.EventDate = postedEventDate;
            SubmitUpdate.ProjectId = selected.ProjectId;
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
            SearchTerm,
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

        SelectedProjectId = DecideUpdate.ProjectId;

        var postedRemarks = DecideUpdate.Remarks;
        var postedUpdateId = DecideUpdate.UpdateId;
        var postedApprove = DecideUpdate.Approve;
        var postedRowVersion = DecideUpdate.RowVersion;

        await PopulateAsync(cancellationToken);

        var selected = SelectedProject;
        if (selected is null)
        {
            return NotFound();
        }

        DecideUpdate.ProjectId = selected.ProjectId;
        DecideUpdate.UpdateId = postedUpdateId;
        DecideUpdate.Approve = postedApprove;
        DecideUpdate.RowVersion = postedRowVersion;
        DecideUpdate.Remarks = postedRemarks;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        byte[]? rowVersion = null;
        if (!string.IsNullOrEmpty(DecideUpdate.RowVersion))
        {
            try
            {
                rowVersion = Convert.FromBase64String(DecideUpdate.RowVersion);
            }
            catch (FormatException)
            {
                ModelState.AddModelError($"DecideUpdate.{postedUpdateId}", "The update could not be processed because the version token was invalid.");
                return Page();
            }
        }

        var result = await _totUpdateService.DecideAsync(
            selected.ProjectId,
            DecideUpdate.UpdateId,
            DecideUpdate.Approve,
            DecideUpdate.Remarks,
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
            ModelState.AddModelError($"DecideUpdate.{postedUpdateId}", result.ErrorMessage ?? "Unable to complete the Transfer of Technology decision.");
            await PopulateAsync(cancellationToken);
            DecideUpdate.ProjectId = selected.ProjectId;
            DecideUpdate.UpdateId = postedUpdateId;
            DecideUpdate.Approve = postedApprove;
            DecideUpdate.RowVersion = postedRowVersion;
            DecideUpdate.Remarks = postedRemarks;
            return Page();
        }

        TempData["Toast"] = DecideUpdate.Approve
            ? "Transfer of Technology update approved."
            : "Transfer of Technology update rejected.";

        return RedirectToPage(new
        {
            TotStatusFilter,
            RequestStateFilter,
            SearchTerm,
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
            OnlyPendingRequests = OnlyPending,
            SearchTerm = SearchTerm
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
                MetDetails = selected.RequestedMetDetails ?? selected.TotMetDetails,
                MetCompletedOn = selected.RequestedMetCompletedOn ?? selected.TotMetCompletedOn,
                FirstProductionModelManufactured = selected.RequestedFirstProductionModelManufactured ?? selected.TotFirstProductionModelManufactured,
                FirstProductionModelManufacturedOn = selected.RequestedFirstProductionModelManufacturedOn ?? selected.TotFirstProductionModelManufacturedOn
            };

            DecideInput = new DecideRequestInput
            {
                ProjectId = selected.ProjectId,
                Approve = true,
                RowVersion = selected.RequestRowVersion is null
                    ? null
                    : Convert.ToBase64String(selected.RequestRowVersion)
            };

            SubmitUpdate = new SubmitUpdateInput
            {
                ProjectId = selected.ProjectId
            };

            DecideUpdate = new DecideUpdateInput
            {
                ProjectId = selected.ProjectId
            };

            SubmitContextBody = null;
            DecideContextBody = null;

            CanSubmitUpdatesForSelectedProject = CanSubmit && IsAuthorizedProjectOfficer(selected);
        }
        else
        {
            SubmitInput = new SubmitRequestInput();
            DecideInput = new DecideRequestInput();
            TotUpdates = Array.Empty<ProjectTotProgressUpdateView>();
            SubmitUpdate = new SubmitUpdateInput();
            DecideUpdate = new DecideUpdateInput();
            CanSubmitUpdatesForSelectedProject = false;
            SubmitContextBody = null;
            DecideContextBody = null;
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

    private async Task<(bool Success, string? ErrorMessage)> TryCreateTotRemarkAsync(int projectId, string? body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(body))
        {
            return (true, null);
        }

        var (actor, remarkType, errorMessage) = await BuildRemarkActorContextAsync(projectId, cancellationToken);
        if (actor is null || remarkType is null)
        {
            return (false, errorMessage);
        }

        var request = new CreateRemarkRequest(
            projectId,
            actor,
            remarkType.Value,
            RemarkScope.TransferOfTechnology,
            body,
            DateOnly.FromDateTime(IstClock.ToIst(DateTime.UtcNow)),
            null,
            null,
            null);

        try
        {
            await _remarkService.CreateRemarkAsync(request, cancellationToken);
            return (true, null);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to create ToT remark for project {ProjectId}.", projectId);
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while creating ToT remark for project {ProjectId}.", projectId);
            return (false, "Unable to save the Transfer of Technology remark.");
        }
    }

    private async Task<(RemarkActorContext? Actor, RemarkType? Type, string? ErrorMessage)> BuildRemarkActorContextAsync(int projectId, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return (null, null, "User context is required to add a remark.");
        }

        var project = await LoadProjectAssignmentsAsync(projectId, cancellationToken);
        if (project is null)
        {
            return (null, null, "Project not found.");
        }

        var roleNames = await _userManager.GetRolesAsync(user);
        var remarkRoles = roleNames
            .Select(name => RemarkActorRoleExtensions.TryParse(name, out var parsed) ? parsed : RemarkActorRole.Unknown)
            .Where(role => role != RemarkActorRole.Unknown)
            .ToHashSet();

        if (!string.IsNullOrEmpty(project.LeadProjectOfficerUserId)
            && string.Equals(project.LeadProjectOfficerUserId, user.Id, StringComparison.Ordinal))
        {
            remarkRoles.Add(RemarkActorRole.ProjectOfficer);
        }

        if (!string.IsNullOrEmpty(project.HodUserId)
            && string.Equals(project.HodUserId, user.Id, StringComparison.Ordinal))
        {
            remarkRoles.Add(RemarkActorRole.HeadOfDepartment);
        }

        if (remarkRoles.Count == 0)
        {
            return (null, null, "You do not have permission to add remarks.");
        }

        var primaryRole = SelectPrimaryRole(remarkRoles);
        var actor = new RemarkActorContext(user.Id, primaryRole, remarkRoles.ToList());
        var remarkType = ResolveRemarkType(primaryRole);
        return (actor, remarkType, null);
    }

    private async Task<ProjectAssignmentInfo?> LoadProjectAssignmentsAsync(int projectId, CancellationToken cancellationToken)
        => await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new ProjectAssignmentInfo(p.Id, p.LeadPoUserId, p.HodUserId))
            .FirstOrDefaultAsync(cancellationToken);

    private static string? NormalizeRemarkBody(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private bool ValidateRemarkBody(string? body, string modelStateKey)
    {
        if (string.IsNullOrEmpty(body))
        {
            return true;
        }

        if (body.Length < 4)
        {
            ModelState.AddModelError(modelStateKey, "Remarks must be at least 4 characters long.");
            return false;
        }

        if (body.Length > 2000)
        {
            ModelState.AddModelError(modelStateKey, "Remarks must be 2000 characters or fewer.");
            return false;
        }

        return true;
    }

    private static RemarkActorRole SelectPrimaryRole(IReadOnlyCollection<RemarkActorRole> roles)
    {
        foreach (var role in new[]
                 {
                     RemarkActorRole.Administrator,
                     RemarkActorRole.HeadOfDepartment,
                     RemarkActorRole.ProjectOffice,
                     RemarkActorRole.MainOffice,
                     RemarkActorRole.ProjectOfficer
                 })
        {
            if (roles.Contains(role))
            {
                return role;
            }
        }

        return roles.First();
    }

    private static RemarkType ResolveRemarkType(RemarkActorRole role)
        => role is RemarkActorRole.ProjectOffice or RemarkActorRole.MainOffice
            ? RemarkType.External
            : RemarkType.Internal;

    private sealed record ProjectAssignmentInfo(int ProjectId, string? LeadProjectOfficerUserId, string? HodUserId);
}
