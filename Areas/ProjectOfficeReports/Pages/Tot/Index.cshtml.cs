using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
using ProjectManagement.ViewModels;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Tot;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewTotTracker)]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ProjectTotTrackerReadService _trackerService;
    private readonly ProjectTotService _totService;
    private readonly IProjectTotExportService _exportService;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IRemarkService _remarkService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext db,
        ProjectTotTrackerReadService trackerService,
        ProjectTotService totService,
        IProjectTotExportService exportService,
        IAuthorizationService authorizationService,
        UserManager<ApplicationUser> userManager,
        IRemarkService remarkService,
        ILogger<IndexModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _trackerService = trackerService ?? throw new ArgumentNullException(nameof(trackerService));
        _totService = totService ?? throw new ArgumentNullException(nameof(totService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
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

    [BindProperty(SupportsGet = true)]
    public ViewModeOption ViewMode { get; set; } = ViewModeOption.Cards;

    private string? _searchTerm;

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm
    {
        get => _searchTerm;
        set => _searchTerm = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public IReadOnlyList<ProjectTotTrackerRow> Projects { get; private set; } = Array.Empty<ProjectTotTrackerRow>();

    public TotTrackerSummary Summary { get; private set; } = TotTrackerSummary.Empty;

    public ProjectTotTrackerRow? SelectedProject { get; private set; }

    public bool CanSubmit { get; private set; }

    public bool CanApprove { get; private set; }

    public bool ShowSubmitModal { get; private set; }

    public bool HighlightDecisionCard { get; private set; }

    public string? DecisionAlertMessage { get; private set; }

    [BindProperty]
    public SubmitRequestInput SubmitInput { get; set; } = new();

    [BindProperty]
    public DecideRequestInput DecideInput { get; set; } = new();

    [BindProperty]
    public string? SubmitContextBody { get; set; }

    [BindProperty]
    public string? DecideContextBody { get; set; }

    [BindProperty]
    public ExportRequestInput Export { get; set; } = new();

    public sealed class SubmitRequestInput : ITotStatusMilestoneFields
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

    public sealed class ExportRequestInput
    {
        public ProjectTotStatus? TotStatus { get; set; }

        public DateOnly? StartedFrom { get; set; }

        public DateOnly? StartedTo { get; set; }

        public DateOnly? CompletedFrom { get; set; }

        public DateOnly? CompletedTo { get; set; }
    }

    public sealed class TotTrackerSummary
    {
        public static TotTrackerSummary Empty { get; } = new();

        public int TotalProjects { get; init; }

        public int TotNotRequired { get; init; }

        public int TotNotStarted { get; init; }

        public int TotInProgress { get; init; }

        public int TotCompleted { get; init; }

        public int PendingApprovals { get; init; }

        public int ApprovedRequests { get; init; }

        public int RejectedRequests { get; init; }

        public int ProjectsRequiringTot { get; init; }

        public int ProjectsWithMetCompleted { get; init; }

        public int ProjectsWithFirstProductionModel { get; init; }

        public static TotTrackerSummary FromProjects(IReadOnlyList<ProjectTotTrackerRow>? projects)
        {
            var items = projects ?? Array.Empty<ProjectTotTrackerRow>();

            var total = items.Count;
            var notRequired = items.Count(p => p.TotStatus == ProjectTotStatus.NotRequired);
            var notStarted = items.Count(p => p.TotStatus == ProjectTotStatus.NotStarted);
            var inProgress = items.Count(p => p.TotStatus == ProjectTotStatus.InProgress);
            var completed = items.Count(p => p.TotStatus == ProjectTotStatus.Completed);
            var pending = items.Count(p => p.RequestState == ProjectTotRequestDecisionState.Pending);
            var approved = items.Count(p => p.RequestState == ProjectTotRequestDecisionState.Approved);
            var rejected = items.Count(p => p.RequestState == ProjectTotRequestDecisionState.Rejected);
            var metCompleted = items.Count(p => p.TotMetCompletedOn.HasValue);
            var firstProduction = items.Count(p => p.TotFirstProductionModelManufactured == true);

            return new TotTrackerSummary
            {
                TotalProjects = total,
                TotNotRequired = notRequired,
                TotNotStarted = notStarted,
                TotInProgress = inProgress,
                TotCompleted = completed,
                PendingApprovals = pending,
                ApprovedRequests = approved,
                RejectedRequests = rejected,
                ProjectsRequiringTot = total - notRequired,
                ProjectsWithMetCompleted = metCompleted,
                ProjectsWithFirstProductionModel = firstProduction
            };
        }
    }

    public enum ViewModeOption
    {
        Cards,
        List
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PopulateAsync(cancellationToken);
        Export = new ExportRequestInput
        {
            TotStatus = TotStatusFilter
        };
    }

    public async Task<IActionResult> OnPostSubmitAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!CanSubmit)
        {
            SelectedProjectId = SubmitInput.ProjectId;
            return PermissionDenied(
                "submit a Transfer of Technology update",
                ProjectOfficeReportsPolicies.ManageTotTracker,
                SubmitInput.ProjectId);
        }

        SelectedProjectId = SubmitInput.ProjectId;

        var submitContext = NormalizeRemarkBody(SubmitContextBody);
        if (!ValidateRemarkBody(submitContext, nameof(SubmitContextBody)))
        {
            await PopulateAsync(cancellationToken);
            SubmitContextBody = submitContext ?? SubmitContextBody;
            ShowSubmitModal = true;
            return Page();
        }

        if (!ModelState.IsValid)
        {
            await PopulateAsync(cancellationToken);
            SubmitContextBody = submitContext;
            ShowSubmitModal = true;
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

        if (result.Status == ProjectTotRequestActionStatus.Conflict)
        {
            var message = result.ErrorMessage ?? "An update is already pending approval for this project.";
            TempData["ToastError"] = message;

            await PopulateAsync(cancellationToken);

            ShowSubmitModal = false;
            HighlightDecisionCard = true;
            DecisionAlertMessage = message;

            _logger.LogInformation(
                "Blocked duplicate ToT submit for project {ProjectId} by user {UserId}.",
                SubmitInput.ProjectId,
                currentUserId);

            return Page();
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
            var message = result.ErrorMessage ?? "Unable to submit the Transfer of Technology update.";
            ModelState.AddModelError(string.Empty, message);
            TempData["ToastError"] = message;
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
            ShowSubmitModal = true;
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
            SelectedProjectId = SubmitInput.ProjectId,
            ViewMode
        });
    }

    public async Task<IActionResult> OnPostExportAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!CanSubmit)
        {
            return PermissionDenied(
                "export Transfer of Technology data",
                ProjectOfficeReportsPolicies.ManageTotTracker,
                SelectedProjectId);
        }

        if (!ModelState.IsValid)
        {
            await PopulateAsync(cancellationToken);
            ViewData["ShowTotExportModal"] = true;
            return Page();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var request = new ProjectTotExportRequest(
            Export.TotStatus,
            Export.StartedFrom,
            Export.StartedTo,
            Export.CompletedFrom,
            Export.CompletedTo,
            SearchTerm,
            userId);

        var result = await _exportService.ExportAsync(request, cancellationToken);
        if (!result.Success || result.File is null)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            if (result.Errors.Count > 0)
            {
                TempData["ToastError"] = result.Errors[0];
            }

            await PopulateAsync(cancellationToken);
            ViewData["ShowTotExportModal"] = true;
            return Page();
        }

        return File(result.File.Content, result.File.ContentType, result.File.FileName);
    }

    public async Task<IActionResult> OnPostDecideAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!CanApprove)
        {
            SelectedProjectId = DecideInput.ProjectId;
            return PermissionDenied(
                "record a Transfer of Technology decision",
                ProjectOfficeReportsPolicies.ApproveTotTracker,
                DecideInput.ProjectId);
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
        var hasRowVersionField = Request is { HasFormContentType: true } && Request.Form.ContainsKey("DecideInput.RowVersion");

        if (!hasRowVersionField)
        {
            ModelState.AddModelError(string.Empty, "Select a Transfer of Technology request before approving or rejecting.");
            TempData["ToastError"] = "Select the project again to refresh the approval form.";
            await PopulateAsync(cancellationToken);
            HighlightDecisionCard = true;
            DecisionAlertMessage = "Select the project again to refresh the approval form.";
            DecideContextBody = decisionContext;
            return Page();
        }

        if (!string.IsNullOrEmpty(DecideInput.RowVersion))
        {
            try
            {
                rowVersion = Convert.FromBase64String(DecideInput.RowVersion);
            }
            catch (FormatException)
            {
                ModelState.AddModelError(string.Empty, "The approval request could not be processed because the version token was invalid.");
                TempData["ToastError"] = "Select the project again to refresh the approval form.";
                await PopulateAsync(cancellationToken);
                HighlightDecisionCard = true;
                DecisionAlertMessage = "Select the project again to refresh the approval form.";
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
            var message = result.ErrorMessage ?? "Unable to complete the Transfer of Technology decision.";
            ModelState.AddModelError(string.Empty, message);
            TempData["ToastError"] = message;
            var approveChoice = DecideInput.Approve;
            await PopulateAsync(cancellationToken);
            DecideInput.Approve = approveChoice;
            DecideContextBody = decisionContext;

            if (result.Status is ProjectTotRequestActionStatus.Conflict or ProjectTotRequestActionStatus.ValidationFailed)
            {
                HighlightDecisionCard = true;
                DecisionAlertMessage = message;
            }

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
            SelectedProjectId = DecideInput.ProjectId,
            ViewMode
        });
    }

    private async Task PopulateAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();

        HighlightDecisionCard = false;
        DecisionAlertMessage = null;

        var filter = new ProjectTotTrackerFilter
        {
            TotStatus = TotStatusFilter,
            RequestState = OnlyPending ? null : RequestStateFilter,
            OnlyPendingRequests = OnlyPending,
            SearchTerm = SearchTerm
        };

        Projects = await _trackerService.GetAsync(filter, cancellationToken);
        Summary = TotTrackerSummary.FromProjects(Projects);

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
                MetDetails = selected.RequestedMetDetails ?? selected.TotMetDetails,
                MetCompletedOn = selected.RequestedMetCompletedOn ?? selected.TotMetCompletedOn,
                FirstProductionModelManufactured = selected.RequestedFirstProductionModelManufactured ?? selected.TotFirstProductionModelManufactured,
                FirstProductionModelManufacturedOn = selected.RequestedFirstProductionModelManufacturedOn ?? selected.TotFirstProductionModelManufacturedOn
            };

            DecideInput = new DecideRequestInput
            {
                ProjectId = selected.ProjectId,
                Approve = true,
                RowVersion = selected.RequestRowVersion is { Length: > 0 }
                    ? Convert.ToBase64String(selected.RequestRowVersion)
                    : null
            };

            SubmitContextBody = null;
            DecideContextBody = null;
        }
        else
        {
            SubmitInput = new SubmitRequestInput();
            DecideInput = new DecideRequestInput();
            SubmitContextBody = null;
            DecideContextBody = null;
        }
    }

    private IActionResult PermissionDenied(string actionDescription, string policy, int? projectId)
    {
        var userId = _userManager.GetUserId(User) ?? "anonymous";
        var roleClaims = User?.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToArray() ?? Array.Empty<string>();

        _logger.LogWarning(
            "User {UserId} ({UserName}) lacks policy {Policy} when attempting to {Action}. Roles: {Roles}",
            userId,
            User?.Identity?.Name ?? "unknown",
            policy,
            actionDescription,
            roleClaims.Length == 0 ? "(none)" : string.Join(", ", roleClaims));

        var roleDisplay = roleClaims.Length == 0 ? "(none)" : string.Join(", ", roleClaims);
        var toastMessage = $"You do not have permission to perform this action. Policy: {policy}. Roles: {roleDisplay}.";

        TempData["ToastError"] = toastMessage;

        return RedirectToPage(new
        {
            TotStatusFilter,
            RequestStateFilter,
            SearchTerm,
            OnlyPending,
            SelectedProjectId = projectId ?? SelectedProjectId,
            ViewMode
        });
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
