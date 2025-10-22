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
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Remarks;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Tot;

[Authorize]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ProjectTotService _totService;
    private readonly IRemarkService _remarkService;
    private readonly ILogger<EditModel> _logger;

    public EditModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ProjectTotService totService,
        IRemarkService remarkService,
        ILogger<EditModel> logger)
    {
        _db = db;
        _users = users;
        _totService = totService;
        _remarkService = remarkService;
        _logger = logger;
    }

    public Project? Project { get; private set; }
    public ProjectRolesViewModel Roles { get; private set; } = ProjectRolesViewModel.Empty;
    public bool CanManageTot { get; private set; }
    public IReadOnlyList<SelectListItem> StatusOptions { get; private set; } = Array.Empty<SelectListItem>();

    [BindProperty]
    public UpdateTotInput Input { get; set; } = new();

    [BindProperty]
    public TotRemarkInput RemarkInput { get; set; } = new();

    [TempData]
    public string? RemarkStatus { get; set; }

    public sealed class UpdateTotInput
    {
        public int ProjectId { get; set; }

        public ProjectTotStatus Status { get; set; } = ProjectTotStatus.NotStarted;

        public DateOnly? StartedOn { get; set; }

        public DateOnly? CompletedOn { get; set; }

        public string? MetDetails { get; set; }

        public DateOnly? MetCompletedOn { get; set; }

        public bool? FirstProductionModelManufactured { get; set; }

        public DateOnly? FirstProductionModelManufacturedOn { get; set; }
    }

    public sealed class TotRemarkInput
    {
        [HiddenInput]
        public int ProjectId { get; set; }

        [Required]
        [MinLength(4)]
        [MaxLength(2000)]
        public string Body { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var project = await LoadProjectAsync(id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (!CanManageTot)
        {
            return DenyProjectAccess(project.Id);
        }

        PopulateInputFromProject(project);
        RemarkInput.ProjectId = project.Id;
        RemarkInput.Body = string.Empty;

        StatusOptions = BuildStatusOptions();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        StatusOptions = BuildStatusOptions();
        RemarkInput.ProjectId = id;

        if (Input is null || Input.ProjectId != id)
        {
            return BadRequest();
        }

        var project = await LoadProjectAsync(id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (!CanManageTot)
        {
            return DenyProjectAccess(project.Id);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var actorUserId = _users.GetUserId(User);
        if (string.IsNullOrEmpty(actorUserId))
        {
            return Forbid();
        }

        var request = new ProjectTotUpdateRequest(
            Input.Status,
            Input.StartedOn,
            Input.CompletedOn,
            Input.MetDetails,
            Input.MetCompletedOn,
            Input.FirstProductionModelManufactured,
            Input.FirstProductionModelManufacturedOn);
        var result = await _totService.UpdateAsync(id, request, actorUserId, cancellationToken);

        if (result.Status == ProjectTotUpdateStatus.NotFound)
        {
            return NotFound();
        }

        if (!result.IsSuccess)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to update Transfer of Technology details.");
            return Page();
        }

        _logger.LogInformation("Project ToT updated. ProjectId={ProjectId}, UserId={UserId}, Status={Status}", id, actorUserId, Input.Status);
        TempData["Flash"] = "Transfer of Technology details updated.";
        return RedirectToPage("/Projects/Overview", new { id });
    }

    public async Task<IActionResult> OnPostAddRemarkAsync(int id, CancellationToken cancellationToken)
    {
        StatusOptions = BuildStatusOptions();

        if (RemarkInput is null || RemarkInput.ProjectId != id)
        {
            return BadRequest();
        }

        var project = await LoadProjectAsync(id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (!CanManageTot)
        {
            return DenyProjectAccess(project.Id);
        }

        PopulateInputFromProject(project);
        RemarkInput.ProjectId = project.Id;

        var normalizedBody = NormalizeRemarkBody(RemarkInput.Body);
        if (normalizedBody is null)
        {
            ModelState.AddModelError("RemarkInput.Body", "Remark text is required.");
            RemarkInput.Body = string.Empty;
            return Page();
        }

        if (normalizedBody.Length < 4)
        {
            ModelState.AddModelError("RemarkInput.Body", "Remarks must be at least 4 characters long.");
            RemarkInput.Body = normalizedBody;
            return Page();
        }

        if (normalizedBody.Length > 2000)
        {
            ModelState.AddModelError("RemarkInput.Body", "Remarks must be 2000 characters or fewer.");
            RemarkInput.Body = normalizedBody;
            return Page();
        }

        var actorContext = BuildRemarkActorContext();
        if (actorContext is null)
        {
            return DenyProjectAccess(project.Id);
        }

        var (actor, type) = actorContext.Value;
        var request = new CreateRemarkRequest(
            id,
            actor,
            type,
            RemarkScope.TransferOfTechnology,
            normalizedBody,
            DateOnly.FromDateTime(IstClock.ToIst(DateTime.UtcNow)),
            null,
            null,
            null);

        try
        {
            await _remarkService.CreateRemarkAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to create ToT remark for project {ProjectId}.", id);
            ModelState.AddModelError("RemarkInput.Body", ex.Message);
            RemarkInput.Body = normalizedBody;
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while creating ToT remark for project {ProjectId}.", id);
            ModelState.AddModelError("RemarkInput.Body", "Unable to save the remark. Please try again.");
            RemarkInput.Body = normalizedBody;
            return Page();
        }

        _logger.LogInformation("ToT remark created. ProjectId={ProjectId}, UserId={UserId}", id, actor.UserId);
        RemarkStatus = "Transfer of Technology remark added.";
        return RedirectToPage(new { id });
    }

    private async Task<Project?> LoadProjectAsync(int id, CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .Include(p => p.Tot)
            .Include(p => p.HodUser)
            .Include(p => p.LeadPoUser)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return null;
        }

        Project = project;

        var currentUserId = _users.GetUserId(User);
        var isAdmin = User.IsInRole("Admin");
        var isHoD = User.IsInRole("HoD");
        var isProjectOfficer = User.IsInRole("Project Officer");
        var isAssignedPo = isProjectOfficer && !string.IsNullOrEmpty(project.LeadPoUserId) &&
            string.Equals(project.LeadPoUserId, currentUserId, StringComparison.Ordinal);
        var isAssignedHoD = isHoD && !string.IsNullOrEmpty(project.HodUserId) &&
            string.Equals(project.HodUserId, currentUserId, StringComparison.Ordinal);

        Roles = new ProjectRolesViewModel
        {
            IsAdmin = isAdmin,
            IsHoD = isHoD,
            IsProjectOfficer = isProjectOfficer,
            IsAssignedProjectOfficer = isAssignedPo,
            IsAssignedHoD = isAssignedHoD
        };

        CanManageTot = isAdmin || isHoD || isAssignedPo || isAssignedHoD;

        return project;
    }

    private static IReadOnlyList<SelectListItem> BuildStatusOptions() => new List<SelectListItem>
    {
        new("Not required", ProjectTotStatus.NotRequired.ToString()),
        new("Not started", ProjectTotStatus.NotStarted.ToString()),
        new("In progress", ProjectTotStatus.InProgress.ToString()),
        new("Completed", ProjectTotStatus.Completed.ToString())
    };

    private void PopulateInputFromProject(Project project)
    {
        Input ??= new UpdateTotInput();
        Input.ProjectId = project.Id;
        Input.Status = project.Tot?.Status ?? ProjectTotStatus.NotStarted;
        Input.StartedOn = project.Tot?.StartedOn;
        Input.CompletedOn = project.Tot?.CompletedOn;
        Input.MetDetails = project.Tot?.MetDetails;
        Input.MetCompletedOn = project.Tot?.MetCompletedOn;
        Input.FirstProductionModelManufactured = project.Tot?.FirstProductionModelManufactured;
        Input.FirstProductionModelManufacturedOn = project.Tot?.FirstProductionModelManufacturedOn;
    }

    private (RemarkActorContext Actor, RemarkType Type)? BuildRemarkActorContext()
    {
        var userId = _users.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var roles = new List<RemarkActorRole>();
        if (Roles.IsAdmin)
        {
            roles.Add(RemarkActorRole.Administrator);
        }

        if (Roles.IsHoD || Roles.IsAssignedHoD)
        {
            roles.Add(RemarkActorRole.HeadOfDepartment);
        }

        if (Roles.IsAssignedProjectOfficer)
        {
            roles.Add(RemarkActorRole.ProjectOfficer);
        }

        if (roles.Count == 0)
        {
            return null;
        }

        var primaryRole = SelectPrimaryRole(roles);
        var actor = new RemarkActorContext(userId, primaryRole, roles);
        var type = ResolveRemarkType(primaryRole);
        return (actor, type);
    }

    private static RemarkActorRole SelectPrimaryRole(IReadOnlyCollection<RemarkActorRole> roles)
    {
        foreach (var role in new[]
                 {
                     RemarkActorRole.Administrator,
                     RemarkActorRole.HeadOfDepartment,
                     RemarkActorRole.ProjectOfficer,
                     RemarkActorRole.ProjectOffice,
                     RemarkActorRole.MainOffice
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

    private static string? NormalizeRemarkBody(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private IActionResult DenyProjectAccess(int projectId)
    {
        var userId = _users.GetUserId(User) ?? "anonymous";
        _logger.LogWarning(
            "User {UserId} ({UserName}) lacks permission to manage ToT for project {ProjectId}.",
            userId,
            User?.Identity?.Name ?? "unknown",
            projectId);

        TempData["Error"] = "You do not have permission to manage Transfer of Technology for this project.";
        return RedirectToPage("/Projects/Overview", new { id = projectId });
    }
}
