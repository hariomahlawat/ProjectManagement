using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects.Photos;

[Authorize(Roles = "Admin,Project Officer,HoD")]
[AutoValidateAntiforgeryToken]
public class ReorderModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IUserContext _userContext;
    private readonly IProjectPhotoService _photoService;
    private readonly ILogger<ReorderModel> _logger;

    public ReorderModel(ApplicationDbContext db,
                        IUserContext userContext,
                        IProjectPhotoService photoService,
                        ILogger<ReorderModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty]
    public ReorderInput Input { get; set; } = new();

    public Project Project { get; private set; } = null!;

    public IReadOnlyList<ProjectPhoto> Photos { get; private set; } = Array.Empty<ProjectPhoto>();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .Include(p => p.Photos)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        if (!UserCanManageProject(project, userId))
        {
            return Forbid();
        }

        Project = project;
        Photos = project.Photos
            .OrderBy(p => p.Ordinal)
            .ThenBy(p => p.Id)
            .ToList();

        Input.ProjectId = project.Id;
        Input.RowVersion = Convert.ToBase64String(project.RowVersion);
        Input.Items = Photos
            .Select(p => new ReorderItem { PhotoId = p.Id, Ordinal = p.Ordinal })
            .ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (id != Input.ProjectId)
        {
            return BadRequest();
        }

        Input.Items ??= new List<ReorderItem>();

        if (Input.Items.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "No photos to reorder.");
        }

        byte[]? rowVersionBytes = ParseRowVersion(Input.RowVersion);
        if (rowVersionBytes is null)
        {
            ModelState.AddModelError(string.Empty, "The form has expired. Please reload and try again.");
        }

        var userId = _userContext.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return Forbid();
        }

        var project = await _db.Projects
            .Include(p => p.Photos)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        if (!UserCanManageProject(project, userId))
        {
            return Forbid();
        }

        Project = project;
        Photos = project.Photos
            .OrderBy(p => p.Ordinal)
            .ThenBy(p => p.Id)
            .ToList();
        Input.RowVersion = Convert.ToBase64String(project.RowVersion);

        if (rowVersionBytes is not null && !project.RowVersion.SequenceEqual(rowVersionBytes))
        {
            ModelState.AddModelError(string.Empty, "The project was updated by someone else. Please reload and try again.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var expectedIds = Photos.Select(p => p.Id).OrderBy(i => i).ToArray();
        var suppliedIds = Input.Items.Select(i => i.PhotoId).OrderBy(i => i).ToArray();
        if (!expectedIds.SequenceEqual(suppliedIds))
        {
            ModelState.AddModelError(string.Empty, "All project photos must be included in the new order.");
            return Page();
        }

        if (Input.Items.Select(i => i.Ordinal).Distinct().Count() != Input.Items.Count)
        {
            ModelState.AddModelError(string.Empty, "Each photo must have a unique position.");
            return Page();
        }

        if (Input.Items.Any(i => i.Ordinal <= 0))
        {
            ModelState.AddModelError(string.Empty, "Ordinals must be positive numbers.");
            return Page();
        }

        var orderedPhotoIds = Input.Items
            .OrderBy(i => i.Ordinal)
            .ThenBy(i => i.PhotoId)
            .Select(i => i.PhotoId)
            .ToList();

        try
        {
            await _photoService.ReorderAsync(project.Id, orderedPhotoIds, userId, cancellationToken);
            TempData["Flash"] = "Photos reordered.";
            return RedirectToPage("./Index", new { id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering photos for project {ProjectId}", id);
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    private bool UserCanManageProject(Project project, string userId)
    {
        var principal = _userContext.User;
        var isAdmin = principal.IsInRole("Admin");
        if (isAdmin)
        {
            return true;
        }

        var isHoD = principal.IsInRole("HoD");
        if (isHoD && string.Equals(project.HodUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[]? ParseRowVersion(string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public class ReorderInput
    {
        public int ProjectId { get; set; }

        public string RowVersion { get; set; } = string.Empty;

        public List<ReorderItem> Items { get; set; } = new();
    }

    public class ReorderItem
    {
        public int PhotoId { get; set; }

        public int Ordinal { get; set; }
    }
}
