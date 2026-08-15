using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Pages.Projects;

public partial class OverviewModel
{
    private static readonly HashSet<string> ValidContentTabs = new(StringComparer.OrdinalIgnoreCase)
    {
        "brief", "capabilities", "technical", "description"
    };

    public IReadOnlyList<ProjectCapabilityStatement> CapabilityStatements { get; private set; }
        = Array.Empty<ProjectCapabilityStatement>();

    public IReadOnlyList<ProjectTechnicalSpecificationItem> TechnicalSpecificationItems { get; private set; }
        = Array.Empty<ProjectTechnicalSpecificationItem>();

    public string? ProjectBriefHtml { get; private set; }
    public int ProjectBriefWordCount { get; private set; }
    public int DescriptionWordCount { get; private set; }
    public bool DescriptionShouldCollapse { get; private set; }
    public ProjectBriefReadiness BriefReadiness { get; private set; }
    public ProjectCapabilityReadiness CapabilityReadiness { get; private set; }
    public string ProjectContentRowVersion { get; private set; } = string.Empty;
    public bool CanEditProjectContent => Roles.IsAdmin || Roles.IsHoD;

    [BindProperty(SupportsGet = true, Name = "content")]
    public string? ContentTab { get; set; }

    [BindProperty]
    public ProjectBriefContentInput ContentBriefInput { get; set; } = new();

    [BindProperty]
    public ProjectCapabilitiesContentInput CapabilityInput { get; set; } = new();

    [BindProperty]
    public ProjectTechnicalSpecificationsContentInput TechnicalSpecificationInput { get; set; } = new();

    [BindProperty]
    public ProjectDescriptionContentInput ContentDescriptionInput { get; set; } = new();

    public sealed class ProjectBriefContentInput
    {
        public int ProjectId { get; set; }

        [MaxLength(ProjectFieldLimits.ProjectBriefMaxLength)]
        public string? Brief { get; set; }

        [Required]
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class ProjectCapabilitiesContentInput
    {
        public int ProjectId { get; set; }
        public List<string?> Statements { get; set; } = new();

        [Required]
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class ProjectTechnicalSpecificationsContentInput
    {
        public int ProjectId { get; set; }
        public List<string?> Items { get; set; } = new();

        [Required]
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class ProjectDescriptionContentInput
    {
        public int ProjectId { get; set; }

        [MaxLength(ProjectFieldLimits.DescriptionMaxLength)]
        public string? Description { get; set; }

        [Required]
        public string RowVersion { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostSaveProjectBriefAsync(int id, CancellationToken ct)
    {
        if (!CanCurrentUserEditProjectContent())
        {
            return Forbid();
        }

        if (ContentBriefInput.ProjectId != id)
        {
            return BadRequest();
        }

        var (userId, userDisplay) = await GetContentEditorIdentityAsync();
        var result = await _projectContentService.SaveBriefAsync(
            id,
            ContentBriefInput.Brief,
            ContentBriefInput.RowVersion,
            userId,
            userDisplay,
            ct);

        return ProjectContentResult(result, id, "brief", "Project brief saved.");
    }

    public async Task<IActionResult> OnPostSaveProjectCapabilitiesAsync(int id, CancellationToken ct)
    {
        if (!CanCurrentUserEditProjectContent())
        {
            return Forbid();
        }

        if (CapabilityInput.ProjectId != id)
        {
            return BadRequest();
        }

        var (userId, userDisplay) = await GetContentEditorIdentityAsync();
        var result = await _projectContentService.SaveCapabilitiesAsync(
            id,
            CapabilityInput.Statements,
            CapabilityInput.RowVersion,
            userId,
            userDisplay,
            ct);

        return ProjectContentResult(result, id, "capabilities", "Capability overview saved.");
    }

    public async Task<IActionResult> OnPostSaveProjectTechnicalSpecificationsAsync(int id, CancellationToken ct)
    {
        if (!CanCurrentUserEditProjectContent())
        {
            return Forbid();
        }

        if (TechnicalSpecificationInput.ProjectId != id)
        {
            return BadRequest();
        }

        var (userId, userDisplay) = await GetContentEditorIdentityAsync();
        var result = await _projectContentService.SaveTechnicalSpecificationsAsync(
            id,
            TechnicalSpecificationInput.Items,
            TechnicalSpecificationInput.RowVersion,
            userId,
            userDisplay,
            ct);

        return ProjectContentResult(result, id, "technical", "Hardware / technical specification saved.");
    }

    public async Task<IActionResult> OnPostSaveProjectDescriptionAsync(int id, CancellationToken ct)
    {
        if (!CanCurrentUserEditProjectContent())
        {
            return Forbid();
        }

        if (ContentDescriptionInput.ProjectId != id)
        {
            return BadRequest();
        }

        var (userId, userDisplay) = await GetContentEditorIdentityAsync();
        var result = await _projectContentService.SaveDescriptionAsync(
            id,
            ContentDescriptionInput.Description,
            ContentDescriptionInput.RowVersion,
            userId,
            userDisplay,
            ct);

        return ProjectContentResult(result, id, "description", "Full project description saved.");
    }

    public IActionResult OnPostPreviewProjectDescription(int id)
    {
        if (!CanCurrentUserEditProjectContent())
        {
            return Forbid();
        }

        if (ContentDescriptionInput.ProjectId != id)
        {
            return BadRequest(new { ok = false, error = "The project reference is invalid." });
        }

        var description = ProjectContentRules.NormalizeNarrative(ContentDescriptionInput.Description);
        if (description?.Length > ProjectFieldLimits.DescriptionMaxLength)
        {
            return BadRequest(new
            {
                ok = false,
                error = $"Full project description cannot exceed {ProjectFieldLimits.DescriptionMaxLength:N0} characters."
            });
        }

        Response.Headers.CacheControl = "no-store, max-age=0";
        return new JsonResult(new
        {
            ok = true,
            html = _markdownRenderer.ToSafeHtml(description)
        });
    }

    private void InitializeProjectContentEditor(Project project)
    {
        ProjectBriefWordCount = ProjectContentRules.CountWords(project.ProjectBrief);
        DescriptionWordCount = ProjectContentRules.CountWords(project.Description);
        DescriptionShouldCollapse = DescriptionWordCount > ProjectFieldLimits.DescriptionPreviewCollapseWords;
        BriefReadiness = ProjectContentRules.GetBriefReadiness(ProjectBriefWordCount);
        CapabilityReadiness = ProjectContentRules.GetCapabilityReadiness(CapabilityStatements.Count);
        ProjectContentRowVersion = Convert.ToBase64String(project.RowVersion ?? Array.Empty<byte>());

        if (string.IsNullOrWhiteSpace(ContentTab) || !ValidContentTabs.Contains(ContentTab))
        {
            ContentTab = !string.IsNullOrWhiteSpace(project.ProjectBrief)
                ? "brief"
                : CapabilityStatements.Count > 0
                    ? "capabilities"
                    : TechnicalSpecificationItems.Count > 0
                        ? "technical"
                        : "description";
        }
        else
        {
            ContentTab = ContentTab.ToLowerInvariant();
        }

        ContentBriefInput = new ProjectBriefContentInput
        {
            ProjectId = project.Id,
            Brief = project.ProjectBrief,
            RowVersion = ProjectContentRowVersion
        };
        CapabilityInput = new ProjectCapabilitiesContentInput
        {
            ProjectId = project.Id,
            Statements = CapabilityStatements.Select(statement => (string?)statement.Statement).ToList(),
            RowVersion = ProjectContentRowVersion
        };
        TechnicalSpecificationInput = new ProjectTechnicalSpecificationsContentInput
        {
            ProjectId = project.Id,
            Items = TechnicalSpecificationItems.Select(item => (string?)item.Text).ToList(),
            RowVersion = ProjectContentRowVersion
        };
        ContentDescriptionInput = new ProjectDescriptionContentInput
        {
            ProjectId = project.Id,
            Description = project.Description,
            RowVersion = ProjectContentRowVersion
        };
    }

    private bool CanCurrentUserEditProjectContent() =>
        User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.HoD);

    private async Task<(string UserId, string UserDisplay)> GetContentEditorIdentityAsync()
    {
        var user = await _users.GetUserAsync(User);
        var userId = user?.Id ?? _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("The current user could not be resolved.");
        }

        var display = user?.FullName;
        if (string.IsNullOrWhiteSpace(display))
        {
            display = user?.UserName ?? User.Identity?.Name ?? userId;
        }

        return (userId, display);
    }

    private IActionResult ProjectContentResult(
        ProjectContentSaveResult result,
        int projectId,
        string tab,
        string successMessage)
    {
        if (result.NotFound)
        {
            return NotFound();
        }

        var isAjax = string.Equals(
            Request.Headers["X-Requested-With"].ToString(),
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);

        if (!result.Succeeded)
        {
            var message = result.Error ?? "The project content could not be saved.";
            if (isAjax)
            {
                Response.StatusCode = result.ConcurrencyConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
                return new JsonResult(new { ok = false, error = message });
            }

            TempData["ProjectContentError"] = message;
            return RedirectToPage("/Projects/Overview", null, new { id = projectId, content = tab }, $"content-{tab}");
        }

        TempData["ProjectContentFlash"] = successMessage;

        if (isAjax)
        {
            Response.Headers.CacheControl = "no-store, max-age=0";
            return new JsonResult(new
            {
                ok = true,
                message = successMessage,
                section = tab
            });
        }

        var pageUrl = Url.Page("/Projects/Overview", new { id = projectId, content = tab })
            ?? $"/Projects/Overview/{projectId}?content={tab}";
        return Redirect($"{pageUrl}#content-{tab}");
    }
}
