using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.ConferenceRemarks;
using ProjectManagement.ViewModels.Workspace;

namespace ProjectManagement.Services.Workspace;

public interface IProjectOfficerConferenceActionQuery
{
    Task<IReadOnlyList<WorkspaceConferenceDirectionActionVm>> GetPendingAsync(
        string userId,
        IReadOnlyDictionary<int, string> projects,
        IReadOnlyDictionary<int, string> ideas,
        IReadOnlyDictionary<int, string> tasks,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Finds conference directions that have not yet been followed by a Project Officer
/// update. No parallel status model is introduced: the native remark, idea-comment/
/// note and task-update histories remain the source of truth.
/// </summary>
public sealed partial class ProjectOfficerConferenceActionQuery : IProjectOfficerConferenceActionQuery
{
    private const int DirectionPreviewLength = 180;
    private readonly ApplicationDbContext _db;

    public ProjectOfficerConferenceActionQuery(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<WorkspaceConferenceDirectionActionVm>> GetPendingAsync(
        string userId,
        IReadOnlyDictionary<int, string> projects,
        IReadOnlyDictionary<int, string> ideas,
        IReadOnlyDictionary<int, string> tasks,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(ideas);
        ArgumentNullException.ThrowIfNull(tasks);

        var pending = new List<WorkspaceConferenceDirectionActionVm>();

        await AddProjectDirectionsAsync(userId, projects, pending, cancellationToken);
        await AddIdeaDirectionsAsync(userId, ideas, pending, cancellationToken);
        await AddTaskDirectionsAsync(userId, tasks, pending, cancellationToken);

        return pending
            .OrderByDescending(item => item.IssuedAtUtc)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task AddProjectDirectionsAsync(
        string userId,
        IReadOnlyDictionary<int, string> projects,
        ICollection<WorkspaceConferenceDirectionActionVm> pending,
        CancellationToken cancellationToken)
    {
        if (projects.Count == 0)
        {
            return;
        }

        var projectIds = projects.Keys.ToArray();
        var rows = await _db.Remarks
            .AsNoTracking()
            .Where(remark => projectIds.Contains(remark.ProjectId) && !remark.IsDeleted)
            .Select(remark => new
            {
                remark.Id,
                remark.ProjectId,
                remark.AuthorUserId,
                remark.Type,
                remark.Body,
                remark.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        foreach (var group in rows.GroupBy(row => row.ProjectId))
        {
            var direction = group
                .Where(row => row.Type == RemarkType.Conference)
                .OrderByDescending(row => row.CreatedAtUtc)
                .ThenByDescending(row => row.Id)
                .FirstOrDefault();

            if (direction is null)
            {
                continue;
            }

            var hasLaterProgress = group.Any(row =>
                row.Type != RemarkType.Conference
                && string.Equals(row.AuthorUserId, userId, StringComparison.Ordinal)
                && IsLater(row.CreatedAtUtc, row.Id, direction.CreatedAtUtc, direction.Id));

            if (hasLaterProgress)
            {
                continue;
            }

            pending.Add(new WorkspaceConferenceDirectionActionVm
            {
                Kind = ConferenceItemKind.Project,
                ItemId = direction.ProjectId,
                ProjectId = direction.ProjectId,
                Title = projects.GetValueOrDefault(direction.ProjectId, $"Project {direction.ProjectId}"),
                DirectionText = Preview(direction.Body),
                IssuedAtUtc = direction.CreatedAtUtc,
                ActionUrl = WorkspaceRouteHelper.ProjectRemarks(direction.ProjectId)
            });
        }
    }

    private async Task AddIdeaDirectionsAsync(
        string userId,
        IReadOnlyDictionary<int, string> ideas,
        ICollection<WorkspaceConferenceDirectionActionVm> pending,
        CancellationToken cancellationToken)
    {
        if (ideas.Count == 0)
        {
            return;
        }

        var ideaIds = ideas.Keys.ToArray();
        var comments = await _db.ProjectIdeaComments
            .AsNoTracking()
            .Where(comment => ideaIds.Contains(comment.ProjectIdeaId) && !comment.IsDeleted)
            .Select(comment => new
            {
                comment.Id,
                comment.ProjectIdeaId,
                comment.CreatedByUserId,
                comment.CommentType,
                comment.CommentText,
                comment.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var notes = await _db.ProjectIdeaNotes
            .AsNoTracking()
            .Where(note => ideaIds.Contains(note.ProjectIdeaId) && !note.IsDeleted && note.CreatedByUserId == userId)
            .Select(note => new
            {
                note.ProjectIdeaId,
                note.CreatedAt,
                note.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        foreach (var group in comments.GroupBy(row => row.ProjectIdeaId))
        {
            var direction = group
                .Where(row => row.CommentType == ProjectIdeaCommentTypes.Conference)
                .OrderByDescending(row => row.CreatedAt)
                .ThenByDescending(row => row.Id)
                .FirstOrDefault();

            if (direction is null)
            {
                continue;
            }

            var hasLaterComment = group.Any(row =>
                row.CommentType != ProjectIdeaCommentTypes.Conference
                && string.Equals(row.CreatedByUserId, userId, StringComparison.Ordinal)
                && IsLater(row.CreatedAt, row.Id, direction.CreatedAt, direction.Id));
            var hasLaterNote = notes.Any(note =>
                note.ProjectIdeaId == direction.ProjectIdeaId
                && LaterOf(note.CreatedAt, note.UpdatedAt) > direction.CreatedAt);

            if (hasLaterComment || hasLaterNote)
            {
                continue;
            }

            pending.Add(new WorkspaceConferenceDirectionActionVm
            {
                Kind = ConferenceItemKind.ProjectIdea,
                ItemId = direction.ProjectIdeaId,
                ProjectId = null,
                Title = ideas.GetValueOrDefault(direction.ProjectIdeaId, $"Idea {direction.ProjectIdeaId}"),
                DirectionText = Preview(direction.CommentText),
                IssuedAtUtc = direction.CreatedAt,
                ActionUrl = WorkspaceRouteHelper.ProjectIdea(direction.ProjectIdeaId)
            });
        }
    }

    private async Task AddTaskDirectionsAsync(
        string userId,
        IReadOnlyDictionary<int, string> tasks,
        ICollection<WorkspaceConferenceDirectionActionVm> pending,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return;
        }

        var taskIds = tasks.Keys.ToArray();
        var rows = await _db.ActionTaskUpdates
            .AsNoTracking()
            .Where(update => taskIds.Contains(update.TaskId) && !update.IsDeleted)
            .Select(update => new
            {
                update.Id,
                update.TaskId,
                update.CreatedByUserId,
                update.UpdateType,
                update.Body,
                update.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        foreach (var group in rows.GroupBy(row => row.TaskId))
        {
            var direction = group
                .Where(row => row.UpdateType == ActionTaskUpdateTypes.Conference)
                .OrderByDescending(row => row.CreatedAtUtc)
                .ThenByDescending(row => row.Id)
                .FirstOrDefault();

            if (direction is null)
            {
                continue;
            }

            var hasLaterProgress = group.Any(row =>
                row.UpdateType != ActionTaskUpdateTypes.Conference
                && string.Equals(row.CreatedByUserId, userId, StringComparison.Ordinal)
                && IsLater(row.CreatedAtUtc, row.Id, direction.CreatedAtUtc, direction.Id));

            if (hasLaterProgress)
            {
                continue;
            }

            pending.Add(new WorkspaceConferenceDirectionActionVm
            {
                Kind = ConferenceItemKind.ActionTask,
                ItemId = direction.TaskId,
                ProjectId = null,
                Title = tasks.GetValueOrDefault(direction.TaskId, $"Task {direction.TaskId}"),
                DirectionText = Preview(direction.Body),
                IssuedAtUtc = direction.CreatedAtUtc,
                ActionUrl = WorkspaceRouteHelper.ActionTask(direction.TaskId)
            });
        }
    }

    private static bool IsLater(DateTime candidateAt, int candidateId, DateTime baselineAt, int baselineId)
        => candidateAt > baselineAt || (candidateAt == baselineAt && candidateId > baselineId);

    private static DateTime LaterOf(DateTime first, DateTime second) => first >= second ? first : second;

    private static string Preview(string? value)
    {
        var plainText = ConferenceDirectionTextFormatter.ToDisplayText(value);
        var normalized = WhitespaceRegex().Replace(plainText, " ").Trim();

        if (normalized.Length <= DirectionPreviewLength)
        {
            return normalized;
        }

        return $"{normalized[..(DirectionPreviewLength - 1)].TrimEnd()}…";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
