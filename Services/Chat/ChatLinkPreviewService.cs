using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;
using ProjectManagement.Services.ProjectIdeas;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Chat;

/// <summary>
/// Preview cards for links to PRISM records pasted into chat. A card is built for the person
/// asking, with that person's own permissions, applying the same rule as the record's page; a
/// record they cannot open gets no card, exactly like one that does not exist, so a preview never
/// reveals more than following the link would. The sender's permissions play no part.
/// </summary>
public sealed class ChatLinkPreviewService
{
    public const int MaxLinksPerRequest = 10;

    public const string ProjectKind = "project";
    public const string ProjectDocumentKind = "document";
    public const string ActionTaskKind = "task";
    public const string ProjectIdeaKind = "idea";
    public const string RepositoryDocumentKind = "repository-document";

    private static readonly Regex ProjectPath = new(@"^/Projects/Overview/(\d{1,9})/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ProjectDocumentPath = new(@"^/Projects/Documents/Preview/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ActionTaskPath = new(@"^/ActionTasks/Details/(\d{1,9})/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ProjectIdeaPath = new(@"^/ProjectIdeas/Details/(\d{1,9})/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RepositoryReaderPath = new(@"^/DocumentRepository/Documents/Reader/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RepositoryViewPath = new(@"^/DocumentRepository/Documents/View/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationService _authorization;
    private readonly ActionTaskPermissionService _taskPermissions;
    private readonly ProjectIdeaPermissionService _ideaPermissions;

    public ChatLinkPreviewService(
        ApplicationDbContext db,
        IAuthorizationService authorization,
        ActionTaskPermissionService taskPermissions,
        ProjectIdeaPermissionService ideaPermissions)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _taskPermissions = taskPermissions ?? throw new ArgumentNullException(nameof(taskPermissions));
        _ideaPermissions = ideaPermissions ?? throw new ArgumentNullException(nameof(ideaPermissions));
    }

    /// <summary>
    /// Cards for the links the viewer may open, in the order asked. <paramref name="links"/> are
    /// paths with their query (<c>/pm/Projects/Overview/12</c>), including the app's path base.
    /// </summary>
    public async Task<IReadOnlyList<ChatLinkPreviewDto>> PreviewAsync(
        ClaimsPrincipal viewer,
        IEnumerable<string?> links,
        PathString pathBase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (viewer.Identity?.IsAuthenticated != true)
        {
            return Array.Empty<ChatLinkPreviewDto>();
        }

        var previews = new List<ChatLinkPreviewDto>();
        foreach (var link in links.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.Ordinal).Take(MaxLinksPerRequest))
        {
            var target = Parse(link, pathBase);
            if (target is null)
            {
                continue;
            }

            var preview = await BuildAsync(viewer, target.Value, pathBase, cancellationToken);
            if (preview is not null)
            {
                previews.Add(preview with { Link = link! });
            }
        }

        return previews;
    }

    /// <summary>Which record a link points at, or null when it is not a previewable PRISM page.</summary>
    internal static (string Kind, string Key)? Parse(string? link, PathString pathBase)
    {
        if (string.IsNullOrWhiteSpace(link) || link.Length > 500 || !link.StartsWith('/') || link.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        var queryStart = link.IndexOf('?');
        var path = new PathString(queryStart >= 0 ? link[..queryStart] : link);
        var query = QueryHelpers.ParseQuery(queryStart >= 0 ? link[queryStart..] : string.Empty);

        // Under a path base (IIS sub-application, e.g. /pm) the record path follows it.
        if (pathBase.HasValue)
        {
            if (!path.StartsWithSegments(pathBase, StringComparison.OrdinalIgnoreCase, out var remaining))
            {
                return null;
            }

            path = remaining;
        }

        var value = path.Value ?? string.Empty;
        Match match;
        if ((match = ProjectPath.Match(value)).Success)
        {
            return (ProjectKind, match.Groups[1].Value);
        }

        if (ProjectDocumentPath.IsMatch(value)
            && query.TryGetValue("documentId", out var documentId)
            && int.TryParse(documentId.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var docId)
            && docId > 0)
        {
            return (ProjectDocumentKind, docId.ToString(CultureInfo.InvariantCulture));
        }

        if ((match = ActionTaskPath.Match(value)).Success)
        {
            return (ActionTaskKind, match.Groups[1].Value);
        }

        if ((match = ProjectIdeaPath.Match(value)).Success)
        {
            return (ProjectIdeaKind, match.Groups[1].Value);
        }

        if ((match = RepositoryReaderPath.Match(value)).Success)
        {
            return (RepositoryDocumentKind, match.Groups[1].Value.ToLowerInvariant());
        }

        if (RepositoryViewPath.IsMatch(value)
            && query.TryGetValue("id", out var repoId)
            && Guid.TryParse(repoId.ToString(), out var guid))
        {
            return (RepositoryDocumentKind, guid.ToString());
        }

        return null;
    }

    private Task<ChatLinkPreviewDto?> BuildAsync(ClaimsPrincipal viewer, (string Kind, string Key) target, PathString pathBase, CancellationToken cancellationToken)
        => target.Kind switch
        {
            ProjectKind => ProjectAsync(viewer, int.Parse(target.Key, CultureInfo.InvariantCulture), pathBase, cancellationToken),
            ProjectDocumentKind => ProjectDocumentAsync(viewer, int.Parse(target.Key, CultureInfo.InvariantCulture), cancellationToken),
            ActionTaskKind => ActionTaskAsync(viewer, int.Parse(target.Key, CultureInfo.InvariantCulture), cancellationToken),
            ProjectIdeaKind => ProjectIdeaAsync(viewer, int.Parse(target.Key, CultureInfo.InvariantCulture), cancellationToken),
            RepositoryDocumentKind => RepositoryDocumentAsync(viewer, Guid.Parse(target.Key), cancellationToken),
            _ => Task.FromResult<ChatLinkPreviewDto?>(null),
        };

    // Projects: /Projects/Overview/{id}. Any signed-in user may view project information
    // (ProjectAccessGuard); a deleted project gets no card.
    private async Task<ChatLinkPreviewDto?> ProjectAsync(ClaimsPrincipal viewer, int id, PathString pathBase, CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == id && !p.IsDeleted)
            .Select(p => new
            {
                Project = p,
                PoName = p.LeadPoUser != null ? p.LeadPoUser.FullName : null,
                PoUserName = p.LeadPoUser != null ? p.LeadPoUser.UserName : null,
                Cover = p.CoverPhotoId == null
                    ? null
                    : p.Photos.Where(photo => photo.Id == p.CoverPhotoId).Select(photo => new { photo.Id, photo.Version }).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null || !ProjectAccessGuard.CanViewProjectInformation(project.Project, viewer))
        {
            return null;
        }

        var po = !string.IsNullOrWhiteSpace(project.PoName) ? project.PoName!.Trim() : project.PoUserName?.Trim();
        var image = project.Cover is null
            ? null
            : $"{pathBase}/Projects/{id}/Photos/{project.Cover.Id}/View/xs?v={project.Cover.Version}";

        return new ChatLinkPreviewDto(
            string.Empty,
            ProjectKind,
            project.Project.Name,
            string.IsNullOrWhiteSpace(po) ? "No project officer assigned" : $"Project officer: {po}",
            image);
    }

    // Project documents: /Projects/Documents/Preview?documentId={id}. Published, not archived,
    // and its project viewable and not deleted.
    private async Task<ChatLinkPreviewDto?> ProjectDocumentAsync(ClaimsPrincipal viewer, int id, CancellationToken cancellationToken)
    {
        var document = await _db.ProjectDocuments
            .AsNoTracking()
            .Include(d => d.Project)
            .Where(d => d.Id == id)
            .SingleOrDefaultAsync(cancellationToken);

        if (document?.Project is null
            || document.Status != ProjectDocumentStatus.Published
            || document.IsArchived
            || document.Project.IsDeleted
            || !ProjectAccessGuard.CanViewProjectDocuments(document.Project, viewer))
        {
            return null;
        }

        return new ChatLinkPreviewDto(string.Empty, ProjectDocumentKind, document.Title, $"Document · {document.Project.Name}", null);
    }

    // Action tasks: /ActionTasks/Details/{id}. The page needs the Action Tracker policy; the task
    // itself only its assignee or a role that may view all tasks (ActionTaskPermissionService).
    private async Task<ChatLinkPreviewDto?> ActionTaskAsync(ClaimsPrincipal viewer, int id, CancellationToken cancellationToken)
    {
        if (!(await _authorization.AuthorizeAsync(viewer, Policies.ActionTracker.Access)).Succeeded)
        {
            return null;
        }

        var role = ActionTaskRoleResolver.Resolve(viewer);
        var userId = viewer.FindFirstValue(ClaimTypes.NameIdentifier);
        if (role is null || string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var task = await _db.ActionTasks
            .AsNoTracking()
            .Where(t => t.Id == id && !t.IsDeleted)
            .Select(t => new { t.Title, t.Status, t.DueDate, t.AssignedToUserId })
            .SingleOrDefaultAsync(cancellationToken);

        if (task is null || !_taskPermissions.CanViewTaskThread(role, userId, task.AssignedToUserId))
        {
            return null;
        }

        var due = task.DueDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        return new ChatLinkPreviewDto(string.Empty, ActionTaskKind, task.Title, $"Task · {task.Status} · due {due}", null);
    }

    // Project ideas: /ProjectIdeas/Details/{id}. ProjectIdeaPermissionService.CanViewIdea.
    private async Task<ChatLinkPreviewDto?> ProjectIdeaAsync(ClaimsPrincipal viewer, int id, CancellationToken cancellationToken)
    {
        var idea = await _db.ProjectIdeas.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (idea is null || !_ideaPermissions.CanViewIdea(viewer, idea))
        {
            return null;
        }

        return new ChatLinkPreviewDto(
            string.Empty,
            ProjectIdeaKind,
            idea.Title,
            $"Idea · {Models.ProjectIdeas.ProjectIdeaStatuses.ToDisplay(idea.Status)}",
            null);
    }

    // Document repository: /DocumentRepository/Documents/Reader/{id} (or View?id=). The
    // repository view policy, and not deleted. Read directly, never through the reader page,
    // which records a view for AOTS documents.
    private async Task<ChatLinkPreviewDto?> RepositoryDocumentAsync(ClaimsPrincipal viewer, Guid id, CancellationToken cancellationToken)
    {
        if (!(await _authorization.AuthorizeAsync(viewer, Policies.Documents.View)).Succeeded)
        {
            return null;
        }

        var document = await _db.Documents
            .AsNoTracking()
            .Where(d => d.Id == id && !d.IsDeleted)
            .Select(d => new { d.Subject, Office = d.OfficeCategory.Name, Category = d.DocumentCategory.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            return null;
        }

        var where = string.Join(" · ", new[] { document.Office, document.Category }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new ChatLinkPreviewDto(
            string.Empty,
            RepositoryDocumentKind,
            string.IsNullOrWhiteSpace(document.Subject) ? "Document" : document.Subject,
            string.IsNullOrEmpty(where) ? "Repository document" : $"Repository · {where}",
            null);
    }
}
