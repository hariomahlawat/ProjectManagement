using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

public class ProjectIdeaReadService
{
    private readonly ApplicationDbContext _db;
    public ProjectIdeaReadService(ApplicationDbContext db) => _db = db;

    // SECTION: Board queries
    public async Task<IReadOnlyList<ProjectIdea>> GetBoardIdeasAsync(string status, string? query, bool myIdeas, string? userId, bool canViewAll)
    {
        status = ProjectIdeaStatuses.All.Contains(status) ? status : ProjectIdeaStatuses.Active;

        var ideas = _db.ProjectIdeas.AsNoTracking()
            .Include(x => x.AssignedProjectOfficerUser).Include(x => x.AssignedHodUser).Include(x => x.CreatedByUser)
            .Include(x => x.Comments.Where(c => !c.IsDeleted)).Include(x => x.Notes.Where(n => !n.IsDeleted)).Include(x => x.Documents.Where(d => !d.IsDeleted))
            .Where(x => !x.IsDeleted && x.Status == status);

        // SECTION: Board visibility permissions
        if (!canViewAll)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Array.Empty<ProjectIdea>();
            }

            ideas = ideas.Where(x => x.CreatedByUserId == userId || x.AssignedProjectOfficerUserId == userId || x.AssignedHodUserId == userId);
        }

        if (myIdeas && !string.IsNullOrWhiteSpace(userId))
        {
            ideas = ideas.Where(x => x.CreatedByUserId == userId || x.AssignedProjectOfficerUserId == userId || x.AssignedHodUserId == userId);
        }

        // SECTION: Null-safe PostgreSQL search
        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            ideas = ideas.Where(x =>
                EF.Functions.ILike(x.Title, pattern) ||
                EF.Functions.ILike(x.Description, pattern) ||
                (x.AssignedProjectOfficerUser != null &&
                    ((x.AssignedProjectOfficerUser.FullName != null && EF.Functions.ILike(x.AssignedProjectOfficerUser.FullName, pattern)) ||
                     (x.AssignedProjectOfficerUser.UserName != null && EF.Functions.ILike(x.AssignedProjectOfficerUser.UserName, pattern)) ||
                     (x.AssignedProjectOfficerUser.Email != null && EF.Functions.ILike(x.AssignedProjectOfficerUser.Email, pattern)))) ||
                (x.AssignedHodUser != null &&
                    ((x.AssignedHodUser.FullName != null && EF.Functions.ILike(x.AssignedHodUser.FullName, pattern)) ||
                     (x.AssignedHodUser.UserName != null && EF.Functions.ILike(x.AssignedHodUser.UserName, pattern)) ||
                     (x.AssignedHodUser.Email != null && EF.Functions.ILike(x.AssignedHodUser.Email, pattern)))) ||
                (x.CreatedByUser != null &&
                    ((x.CreatedByUser.FullName != null && EF.Functions.ILike(x.CreatedByUser.FullName, pattern)) ||
                     (x.CreatedByUser.UserName != null && EF.Functions.ILike(x.CreatedByUser.UserName, pattern)) ||
                     (x.CreatedByUser.Email != null && EF.Functions.ILike(x.CreatedByUser.Email, pattern)))));
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
