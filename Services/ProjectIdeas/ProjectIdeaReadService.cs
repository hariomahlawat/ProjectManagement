using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

public class ProjectIdeaReadService
{
    private readonly ApplicationDbContext _db;
    public ProjectIdeaReadService(ApplicationDbContext db) => _db = db;

    // SECTION: Board queries
    public async Task<IReadOnlyList<ProjectIdea>> GetBoardIdeasAsync(string status, string? query, bool myIdeas, string? userId)
    {
        var ideas = _db.ProjectIdeas.AsNoTracking()
            .Include(x => x.AssignedProjectOfficerUser).Include(x => x.AssignedHodUser).Include(x => x.CreatedByUser)
            .Include(x => x.Comments.Where(c => !c.IsDeleted)).Include(x => x.Notes.Where(n => !n.IsDeleted)).Include(x => x.Documents.Where(d => !d.IsDeleted))
            .Where(x => !x.IsDeleted && x.Status == status);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLower();
            ideas = ideas.Where(x => x.Title.ToLower().Contains(q) || x.Description.ToLower().Contains(q)
                || (x.AssignedProjectOfficerUser != null && x.AssignedProjectOfficerUser.FullName.ToLower().Contains(q))
                || (x.AssignedHodUser != null && x.AssignedHodUser.FullName.ToLower().Contains(q))
                || (x.CreatedByUser != null && x.CreatedByUser.FullName.ToLower().Contains(q)));
        }
        if (myIdeas && !string.IsNullOrWhiteSpace(userId))
        {
            ideas = ideas.Where(x => x.CreatedByUserId == userId || x.AssignedProjectOfficerUserId == userId || x.AssignedHodUserId == userId);
        }
        var list = await ideas.ToListAsync();
        return list.OrderByDescending(GetLastActivity).ToList();
    }

    public Task<ProjectIdea?> GetDetailsAsync(int id) => _db.ProjectIdeas
        .Include(x => x.AssignedProjectOfficerUser).Include(x => x.AssignedHodUser).Include(x => x.CreatedByUser)
        .Include(x => x.Comments.Where(c => !c.IsDeleted)).ThenInclude(x => x.CreatedByUser)
        .Include(x => x.Notes.Where(n => !n.IsDeleted)).ThenInclude(x => x.CreatedByUser)
        .Include(x => x.Documents.Where(d => !d.IsDeleted)).ThenInclude(x => x.UploadedByUser)
        .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

    public static DateTime GetLastActivity(ProjectIdea idea)
    {
        var dates = new[] { idea.UpdatedAt, idea.ArchivedAt ?? DateTime.MinValue, idea.Comments.Select(c => c.CreatedAt).DefaultIfEmpty(DateTime.MinValue).Max(), idea.Notes.Select(n => n.UpdatedAt > n.CreatedAt ? n.UpdatedAt : n.CreatedAt).DefaultIfEmpty(DateTime.MinValue).Max(), idea.Documents.Select(d => d.UploadedAt).DefaultIfEmpty(DateTime.MinValue).Max() };
        return dates.Max();
    }
}
