using Microsoft.EntityFrameworkCore;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

public class ProjectIdeaCommandService
{
    private readonly ApplicationDbContext _db;

    public ProjectIdeaCommandService(ApplicationDbContext db)
        => _db = db ?? throw new ArgumentNullException(nameof(db));

    // SECTION: Idea lifecycle commands
    public async Task<ProjectIdea> CreateAsync(ProjectIdea idea)
    {
        idea.CreatedAt = idea.UpdatedAt = DateTime.UtcNow;
        _db.ProjectIdeas.Add(idea);
        await _db.SaveChangesAsync();
        return idea;
    }

    public async Task UpdateAsync(ProjectIdea idea)
    {
        idea.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task ArchiveAsync(ProjectIdea idea, string? reason)
    {
        idea.Status = ProjectIdeaStatuses.Archived;
        idea.ArchivedAt = DateTime.UtcNow;
        idea.ArchiveReason = reason;
        idea.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task RestoreAsync(ProjectIdea idea)
    {
        idea.Status = ProjectIdeaStatuses.Active;
        idea.ArchivedAt = null;
        idea.ArchiveReason = null;
        idea.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
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

    public async Task AddNoteAsync(ProjectIdea idea, string title, string body, bool pinned, string userId)
    {
        if (pinned)
        {
            var existingPinnedNotes = await _db.ProjectIdeaNotes
                .Where(n => n.ProjectIdeaId == idea.Id && !n.IsDeleted && n.IsPinned)
                .ToListAsync();

            foreach (var note in existingPinnedNotes)
            {
                note.IsPinned = false;
                note.UpdatedAt = DateTime.UtcNow;
            }
        }

        _db.ProjectIdeaNotes.Add(new ProjectIdeaNote
        {
            ProjectIdeaId = idea.Id,
            Title = title,
            Body = body,
            IsPinned = pinned,
            CreatedByUserId = userId,
            UpdatedAt = DateTime.UtcNow
        });
        idea.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task SoftDeleteNoteAsync(int noteId)
    {
        var note = await _db.ProjectIdeaNotes.FirstOrDefaultAsync(x => x.Id == noteId);
        if (note is null)
        {
            return;
        }

        note.IsDeleted = true;
        note.UpdatedAt = DateTime.UtcNow;

        var idea = await _db.ProjectIdeas.FirstOrDefaultAsync(i => i.Id == note.ProjectIdeaId);
        if (idea is not null)
        {
            idea.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
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

        if (idea.IsDeleted || string.Equals(
                idea.Status,
                ProjectIdeaStatuses.Archived,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Archived ideas cannot be updated.");
        }

        var body = text?.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("Comment cannot be empty.");
        }

        if (body.Length > 4000)
        {
            throw new InvalidOperationException("Comment cannot exceed 4,000 characters.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("Comment author is required.");
        }

        var now = DateTime.UtcNow;
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

        _db.ProjectIdeaComments.Add(comment);
        idea.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return comment;
    }

    private static string? NormalizeRoleSnapshot(string? role)
    {
        var normalized = role?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
