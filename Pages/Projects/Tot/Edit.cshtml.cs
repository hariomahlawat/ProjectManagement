using System;
using System.Collections.Generic;
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
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Pages.Projects.Tot;

[Authorize]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ProjectTotService _totService;
    private readonly ILogger<EditModel> _logger;

    public EditModel(ApplicationDbContext db, UserManager<ApplicationUser> users, ProjectTotService totService, ILogger<EditModel> logger)
    {
        _db = db;
        _users = users;
        _totService = totService;
        _logger = logger;
    }

    public Project? Project { get; private set; }
    public ProjectRolesViewModel Roles { get; private set; } = ProjectRolesViewModel.Empty;
    public bool CanManageTot { get; private set; }
    public IReadOnlyList<SelectListItem> StatusOptions { get; private set; } = Array.Empty<SelectListItem>();

    [BindProperty]
    public UpdateTotInput Input { get; set; } = new();

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

        public string? Remarks { get; set; }
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
            return Forbid();
        }

        Input ??= new UpdateTotInput();
        Input.ProjectId = project.Id;
        Input.Status = project.Tot?.Status ?? ProjectTotStatus.NotStarted;
        Input.StartedOn = project.Tot?.StartedOn;
        Input.CompletedOn = project.Tot?.CompletedOn;
        Input.MetDetails = project.Tot?.MetDetails;
        Input.MetCompletedOn = project.Tot?.MetCompletedOn;
        Input.FirstProductionModelManufactured = project.Tot?.FirstProductionModelManufactured;
        Input.FirstProductionModelManufacturedOn = project.Tot?.FirstProductionModelManufacturedOn;
        Input.Remarks = project.Tot?.Remarks;

        StatusOptions = BuildStatusOptions();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        StatusOptions = BuildStatusOptions();

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
            return Forbid();
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
            Input.Remarks,
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
}
