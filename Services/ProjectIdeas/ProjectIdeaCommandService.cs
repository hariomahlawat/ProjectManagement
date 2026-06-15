using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

public class ProjectIdeaCommandService
{
    private readonly ApplicationDbContext _db;
    public ProjectIdeaCommandService(ApplicationDbContext db) => _db = db;

    // SECTION: Idea lifecycle commands
    public async Task<ProjectIdea> CreateAsync(ProjectIdea idea) { idea.CreatedAt = idea.UpdatedAt = DateTime.UtcNow; _db.ProjectIdeas.Add(idea); await _db.SaveChangesAsync(); return idea; }
    public async Task UpdateAsync(ProjectIdea idea) { idea.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
    public async Task ArchiveAsync(ProjectIdea idea, string? reason) { idea.Status = ProjectIdeaStatuses.Archived; idea.ArchivedAt = DateTime.UtcNow; idea.ArchiveReason = reason; idea.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
    public async Task RestoreAsync(ProjectIdea idea) { idea.Status = ProjectIdeaStatuses.Active; idea.ArchivedAt = null; idea.ArchiveReason = null; idea.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }

    // SECTION: Collaboration commands
    public async Task AddCommentAsync(ProjectIdea idea, string text, string userId) { _db.ProjectIdeaComments.Add(new ProjectIdeaComment { ProjectIdeaId = idea.Id, CommentText = text, CreatedByUserId = userId }); idea.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
    public async Task AddNoteAsync(ProjectIdea idea, string title, string body, bool pinned, string userId) { _db.ProjectIdeaNotes.Add(new ProjectIdeaNote { ProjectIdeaId = idea.Id, Title = title, Body = body, IsPinned = pinned, CreatedByUserId = userId }); idea.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
    public async Task SoftDeleteNoteAsync(int noteId) { var note = await _db.ProjectIdeaNotes.FirstOrDefaultAsync(x => x.Id == noteId); if (note is not null) { note.IsDeleted = true; await _db.SaveChangesAsync(); } }
}
