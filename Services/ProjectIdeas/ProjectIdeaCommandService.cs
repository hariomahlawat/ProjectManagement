using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.ProjectIdeas;
using ProjectManagement.Services;

namespace ProjectManagement.Services.ProjectIdeas;

public class ProjectIdeaCommandService : IProjectIdeaCommandService
{
    public const string ConcurrencyConflictMessage = "This record was changed by someone else. Refresh and review the latest version before trying again.";
    public const string RowVersionRequiredMessage = "The record version is required for this operation.";

    private readonly ApplicationDbContext _db;
    private readonly IAuditService? _audit;
    private readonly IClock _clock;

    public ProjectIdeaCommandService(
        ApplicationDbContext db,
        IAuditService? audit = null,
        IClock? clock = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit;
        _clock = clock ?? new SystemClock();
    }

    // SECTION: Idea lifecycle commands
    public async Task<ProjectIdea> CreateAsync(
        ProjectIdea idea,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(idea);

        var now = UtcNow();
        idea.CreatedAt = idea.UpdatedAt = now;
        idea.IsDeleted = false;
        idea.DeletedAt = null;
        idea.DeletedByUserId = null;
        idea.DeleteReason = null;

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);

        _db.ProjectIdeas.Add(idea);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync(
            "ProjectIdeas.IdeaCreated",
            idea.CreatedByUserId,
            new Dictionary<string, string?>
            {
                ["IdeaId"] = idea.Id.ToString(),
                ["Title"] = idea.Title,
                ["Status"] = idea.Status,
                ["AssignedProjectOfficerUserId"] = idea.AssignedProjectOfficerUserId,
                ["AssignedHodUserId"] = idea.AssignedHodUserId
            });

        await transaction.CommitAsync(cancellationToken);
        return idea;
    }

    public Task UpdateAsync(ProjectIdea idea)
    {
        ArgumentNullException.ThrowIfNull(idea);
        return UpdateAsync(idea, idea.RowVersion);
    }

    public async Task UpdateAsync(
        ProjectIdea idea,
        byte[] rowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(idea);
        EnsureIdeaWritable(idea);
        ApplyRowVersion(idea, rowVersion);
        idea.UpdatedAt = UtcNow();
        await SaveWithFriendlyConcurrencyAsync(cancellationToken);
    }

    public Task ArchiveAsync(ProjectIdea idea, string? reason)
    {
        ArgumentNullException.ThrowIfNull(idea);
        return ArchiveAsync(idea, reason, idea.RowVersion);
    }

    public async Task ArchiveAsync(
        ProjectIdea idea,
        string? reason,
        byte[] rowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(idea);
        EnsureIdeaWritable(idea);
        ApplyRowVersion(idea, rowVersion);

        var now = UtcNow();
        idea.Status = ProjectIdeaStatuses.Archived;
        idea.ArchivedAt = now;
        idea.ArchiveReason = reason?.Trim();
        idea.UpdatedAt = now;
        await SaveWithFriendlyConcurrencyAsync(cancellationToken);
    }

    public Task RestoreAsync(ProjectIdea idea)
    {
        ArgumentNullException.ThrowIfNull(idea);
        return RestoreAsync(idea, idea.RowVersion);
    }

    public async Task RestoreAsync(
        ProjectIdea idea,
        byte[] rowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(idea);
        if (idea.IsDeleted)
        {
            throw new InvalidOperationException(ProjectIdeaGovernancePolicy.DeletedIdeaMessage);
        }

        ApplyRowVersion(idea, rowVersion);
        idea.Status = ProjectIdeaStatuses.Active;
        idea.ArchivedAt = null;
        idea.ArchiveReason = null;
        idea.UpdatedAt = UtcNow();
        await SaveWithFriendlyConcurrencyAsync(cancellationToken);
    }

    public async Task<bool> SoftDeleteIdeaAsync(
        int ideaId,
        string? reason,
        byte[] rowVersion,
        ProjectIdeaActorContext actor,
        CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);
        if (!ProjectIdeaGovernancePolicy.CanDeleteAnyIdea(actor.Roles))
        {
            throw new InvalidOperationException(ProjectIdeaGovernancePolicy.PermissionDeniedMessage);
        }

        var deleteReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(deleteReason))
        {
            throw new InvalidOperationException("Please enter a reason for deleting the idea.");
        }
        if (deleteReason.Length > 1000)
        {
            throw new InvalidOperationException("The deletion reason cannot exceed 1,000 characters.");
        }

        var idea = await _db.ProjectIdeas
            .FirstOrDefaultAsync(candidate => candidate.Id == ideaId, cancellationToken);
        if (idea is null || idea.IsDeleted)
        {
            return false;
        }

        ApplyRowVersion(idea, rowVersion);
        var now = UtcNow();
        idea.IsDeleted = true;
        idea.DeletedAt = now;
        idea.DeletedByUserId = actor.UserId;
        idea.DeleteReason = deleteReason;
        idea.UpdatedAt = now;

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(ConcurrencyConflictMessage, exception);
        }

        await AuditAsync(
            "ProjectIdeas.IdeaDeleted",
            actor.UserId,
            new Dictionary<string, string?>
            {
                ["IdeaId"] = idea.Id.ToString(),
                ["Title"] = idea.Title,
                ["Status"] = idea.Status,
                ["Reason"] = deleteReason
            });
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RestoreDeletedIdeaAsync(
        int ideaId,
        byte[] rowVersion,
        ProjectIdeaActorContext actor,
        CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);
        if (!ProjectIdeaGovernancePolicy.CanDeleteAnyIdea(actor.Roles))
        {
            throw new InvalidOperationException(ProjectIdeaGovernancePolicy.PermissionDeniedMessage);
        }

        var idea = await _db.ProjectIdeas
            .FirstOrDefaultAsync(candidate => candidate.Id == ideaId, cancellationToken);
        if (idea is null || !idea.IsDeleted)
        {
            return false;
        }

        var previousDeleteReason = idea.DeleteReason;
        var previousDeletedAt = idea.DeletedAt;
        var previousDeletedBy = idea.DeletedByUserId;

        ApplyRowVersion(idea, rowVersion);
        var now = UtcNow();
        idea.IsDeleted = false;
        idea.DeletedAt = null;
        idea.DeletedByUserId = null;
        idea.DeleteReason = null;
        idea.UpdatedAt = now;

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(ConcurrencyConflictMessage, exception);
        }

        await AuditAsync(
            "ProjectIdeas.IdeaRestoredFromDeletion",
            actor.UserId,
            new Dictionary<string, string?>
            {
                ["IdeaId"] = idea.Id.ToString(),
                ["Title"] = idea.Title,
                ["Status"] = idea.Status,
                ["PreviousDeleteReason"] = previousDeleteReason,
                ["PreviousDeletedAt"] = previousDeletedAt?.ToString("O"),
                ["PreviousDeletedByUserId"] = previousDeletedBy
            });
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // SECTION: Collaboration commands
    public async Task AddCommentAsync(ProjectIdea idea, string text, string userId)
    {
        await AddCommentCoreAsync(
            idea,
            text,
            userId,
            actorRole: null,
            ProjectIdeaCommentTypes.General,
            CancellationToken.None);
    }

    public Task<ProjectIdeaComment> AddConferenceCommentAsync(
        ProjectIdea idea,
        string text,
        string userId,
        string actorRole,
        CancellationToken cancellationToken = default)
    {
        if (!Policies.ConferenceRemarks.ManageAllowedRoles.Contains(
                actorRole,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only Comdt or HoD may add conference remarks.");
        }

        return AddCommentCoreAsync(
            idea,
            text,
            userId,
            actorRole,
            ProjectIdeaCommentTypes.Conference,
            cancellationToken);
    }

    public async Task<ProjectIdeaComment?> EditCommentAsync(
        int ideaId,
        int commentId,
        string? text,
        byte[] rowVersion,
        ProjectIdeaActorContext actor,
        CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);
        var body = ValidateCommentText(text);

        var comment = await _db.ProjectIdeaComments
            .Include(candidate => candidate.ProjectIdea)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == commentId && candidate.ProjectIdeaId == ideaId,
                cancellationToken);
        if (comment is null || comment.IsDeleted || comment.ProjectIdea is null)
        {
            return null;
        }

        var permission = ProjectIdeaGovernancePolicy.EvaluateCommentMutation(
            comment.ProjectIdea,
            comment,
            actor,
            UtcNow(),
            isDelete: false);
        if (!permission.IsAllowed)
        {
            throw new InvalidOperationException(permission.Message ?? ProjectIdeaGovernancePolicy.PermissionDeniedMessage);
        }

        ApplyRowVersion(comment, rowVersion);
        var previousText = comment.CommentText;
        var now = UtcNow();
        comment.CommentText = body;
        comment.EditedAt = now;
        comment.EditedByUserId = actor.UserId;
        comment.ProjectIdea.UpdatedAt = now;

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(ConcurrencyConflictMessage, exception);
        }

        await AuditAsync(
            "ProjectIdeas.CommentEdited",
            actor.UserId,
            new Dictionary<string, string?>
            {
                ["IdeaId"] = ideaId.ToString(),
                ["CommentId"] = comment.Id.ToString(),
                ["CommentType"] = comment.CommentType,
                ["OriginalAuthorUserId"] = comment.CreatedByUserId,
                ["OriginalCreatedAt"] = comment.CreatedAt.ToUniversalTime().ToString("O"),
                ["StatusSnapshot"] = comment.StatusSnapshot,
                ["PreviousText"] = previousText,
                ["NewText"] = body
            });
        await transaction.CommitAsync(cancellationToken);
        return comment;
    }

    public async Task<bool> SoftDeleteCommentAsync(
        int ideaId,
        int commentId,
        byte[] rowVersion,
        ProjectIdeaActorContext actor,
        CancellationToken cancellationToken = default)
    {
        EnsureActor(actor);

        var comment = await _db.ProjectIdeaComments
            .Include(candidate => candidate.ProjectIdea)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == commentId && candidate.ProjectIdeaId == ideaId,
                cancellationToken);
        if (comment is null || comment.IsDeleted || comment.ProjectIdea is null)
        {
            return false;
        }

        var permission = ProjectIdeaGovernancePolicy.EvaluateCommentMutation(
            comment.ProjectIdea,
            comment,
            actor,
            UtcNow(),
            isDelete: true);
        if (!permission.IsAllowed)
        {
            throw new InvalidOperationException(permission.Message ?? ProjectIdeaGovernancePolicy.PermissionDeniedMessage);
        }

        ApplyRowVersion(comment, rowVersion);
        var now = UtcNow();
        comment.IsDeleted = true;
        comment.DeletedAt = now;
        comment.DeletedByUserId = actor.UserId;
        comment.ProjectIdea.UpdatedAt = now;

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(ConcurrencyConflictMessage, exception);
        }

        await AuditAsync(
            "ProjectIdeas.CommentDeleted",
            actor.UserId,
            new Dictionary<string, string?>
            {
                ["IdeaId"] = ideaId.ToString(),
                ["CommentId"] = comment.Id.ToString(),
                ["CommentType"] = comment.CommentType,
                ["OriginalAuthorUserId"] = comment.CreatedByUserId,
                ["OriginalCreatedAt"] = comment.CreatedAt.ToUniversalTime().ToString("O"),
                ["StatusSnapshot"] = comment.StatusSnapshot,
                ["Text"] = comment.CommentText
            });
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task AddNoteAsync(ProjectIdea idea, string title, string body, bool pinned, string userId)
    {
        ArgumentNullException.ThrowIfNull(idea);
        EnsureIdeaWritable(idea);

        if (pinned)
        {
            var existingPinnedNotes = await _db.ProjectIdeaNotes
                .Where(n => n.ProjectIdeaId == idea.Id && !n.IsDeleted && n.IsPinned)
                .ToListAsync();

            foreach (var note in existingPinnedNotes)
            {
                note.IsPinned = false;
                note.UpdatedAt = UtcNow();
            }
        }

        _db.ProjectIdeaNotes.Add(new ProjectIdeaNote
        {
            ProjectIdeaId = idea.Id,
            Title = title,
            Body = body,
            IsPinned = pinned,
            CreatedByUserId = userId,
            UpdatedAt = UtcNow()
        });
        idea.UpdatedAt = UtcNow();
        await SaveWithFriendlyConcurrencyAsync();
    }

    public async Task SoftDeleteNoteAsync(int noteId)
    {
        var note = await _db.ProjectIdeaNotes.FirstOrDefaultAsync(x => x.Id == noteId);
        if (note is null)
        {
            return;
        }

        note.IsDeleted = true;
        note.UpdatedAt = UtcNow();

        var idea = await _db.ProjectIdeas.FirstOrDefaultAsync(i => i.Id == note.ProjectIdeaId);
        if (idea is not null)
        {
            idea.UpdatedAt = UtcNow();
        }

        await SaveWithFriendlyConcurrencyAsync();
    }

    private async Task<ProjectIdeaComment> AddCommentCoreAsync(
        ProjectIdea idea,
        string text,
        string userId,
        string? actorRole,
        string commentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idea);
        EnsureIdeaWritable(idea);

        var body = ValidateCommentText(text);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("Comment author is required.");
        }

        var now = UtcNow();
        var comment = new ProjectIdeaComment
        {
            ProjectIdeaId = idea.Id,
            CommentText = body,
            CommentType = commentType,
            CreatedByUserId = userId,
            CreatedByRole = NormalizeRoleSnapshot(actorRole),
            StatusSnapshot = idea.Status,
            CreatedAt = now,
            IsDeleted = false
        };

        await using var transaction = await RelationalTransactionScope.CreateAsync(_db.Database, cancellationToken);
        _db.ProjectIdeaComments.Add(comment);
        idea.UpdatedAt = now;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(ConcurrencyConflictMessage, exception);
        }
        await AuditAsync(
            string.Equals(commentType, ProjectIdeaCommentTypes.Conference, StringComparison.Ordinal)
                ? "ProjectIdeas.ConferenceCommentAdded"
                : "ProjectIdeas.CommentAdded",
            userId,
            new Dictionary<string, string?>
            {
                ["IdeaId"] = idea.Id.ToString(),
                ["CommentId"] = comment.Id.ToString(),
                ["CommentType"] = comment.CommentType,
                ["StatusSnapshot"] = comment.StatusSnapshot
            });
        await transaction.CommitAsync(cancellationToken);
        return comment;
    }

    private void ApplyRowVersion(ProjectIdea idea, byte[]? rowVersion)
    {
        EnsureRowVersion(rowVersion);
        _db.Entry(idea).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion!;
    }

    private void ApplyRowVersion(ProjectIdeaComment comment, byte[]? rowVersion)
    {
        EnsureRowVersion(rowVersion);
        _db.Entry(comment).Property(candidate => candidate.RowVersion).OriginalValue = rowVersion!;
    }

    private static void EnsureRowVersion(byte[]? rowVersion)
    {
        if (rowVersion is null || rowVersion.Length == 0)
        {
            throw new InvalidOperationException(RowVersionRequiredMessage);
        }
    }

    private static string ValidateCommentText(string? text)
    {
        var body = text?.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("Comment cannot be empty.");
        }
        if (body.Length > 4000)
        {
            throw new InvalidOperationException("Comment cannot exceed 4,000 characters.");
        }
        return body;
    }

    private static string? NormalizeRoleSnapshot(string? role)
    {
        var normalized = role?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void EnsureActor(ProjectIdeaActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(actor.UserId))
        {
            throw new InvalidOperationException("The current user could not be resolved.");
        }
    }

    private static void EnsureIdeaWritable(ProjectIdea idea)
    {
        if (idea.IsDeleted)
        {
            throw new InvalidOperationException(ProjectIdeaGovernancePolicy.DeletedIdeaMessage);
        }
        if (string.Equals(idea.Status, ProjectIdeaStatuses.Archived, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(ProjectIdeaGovernancePolicy.ArchivedIdeaMessage);
        }
    }

    private async Task SaveWithFriendlyConcurrencyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new InvalidOperationException(ConcurrencyConflictMessage, exception);
        }
    }

    private async Task AuditAsync(
        string action,
        string userId,
        IDictionary<string, string?> data)
    {
        if (_audit is null)
        {
            return;
        }

        await _audit.LogAsync(action, userId: userId, data: data);
    }

    private DateTime UtcNow() => _clock.UtcNow.UtcDateTime;
}
