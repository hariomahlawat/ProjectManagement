using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

public class ProjectIdeaReadService
{
    private readonly ApplicationDbContext _db;

    public ProjectIdeaReadService(ApplicationDbContext db) => _db = db;

    // SECTION: Board queries
    public async Task<IReadOnlyList<ProjectIdea>> GetBoardIdeasAsync(
        string status,
        string? query,
        bool myIdeas,
        string? userId,
        bool canViewAll,
        string? sort = null,
        string? projectOfficerUserId = null,
        string? assignment = null)
    {
        status = ProjectIdeaStatuses.All.Contains(status)
            ? status
            : ProjectIdeaStatuses.Active;

        IQueryable<ProjectIdea> ideas = ApplyBoardFilters(
                _db.ProjectIdeas.AsNoTracking(),
                query,
                myIdeas,
                userId,
                canViewAll,
                projectOfficerUserId,
                assignment)
            .Where(x => x.Status == status)
            .Include(x => x.AssignedProjectOfficerUser)
            .Include(x => x.AssignedHodUser)
            .Include(x => x.CreatedByUser)
            .Include(x => x.Comments.Where(c => !c.IsDeleted))
            .Include(x => x.Notes.Where(n => !n.IsDeleted))
            .Include(x => x.Documents.Where(d => !d.IsDeleted));

        if (_db.Database.IsRelational())
        {
            ideas = ideas.AsSplitQuery();
        }

        var list = await ideas.ToListAsync();
        return SortIdeas(list, ProjectIdeaSorts.Normalise(sort)).ToList();
    }

    public async Task<IReadOnlyDictionary<string, int>> GetBoardStatusCountsAsync(
        string? query,
        bool myIdeas,
        string? userId,
        bool canViewAll,
        string? projectOfficerUserId = null,
        string? assignment = null)
    {
        var visibleIdeas = ApplyBoardFilters(
            _db.ProjectIdeas.AsNoTracking(),
            query,
            myIdeas,
            userId,
            canViewAll,
            projectOfficerUserId,
            assignment);

        var groupedCounts = await visibleIdeas
            .Where(x => ProjectIdeaStatuses.All.Contains(x.Status))
            .GroupBy(x => x.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

        return ProjectIdeaStatuses.All.ToDictionary(
            status => status,
            status => groupedCounts.GetValueOrDefault(status));
    }

    public async Task<IReadOnlyList<ProjectIdeaOfficerOption>> GetBoardProjectOfficersAsync(
        string? userId,
        bool canViewAll)
    {
        var candidates = await ApplyBoardVisibility(
                _db.ProjectIdeas.AsNoTracking(),
                userId,
                canViewAll)
            .Where(x => x.AssignedProjectOfficerUserId != null && x.AssignedProjectOfficerUser != null)
            .Select(x => new
            {
                UserId = x.AssignedProjectOfficerUserId!,
                x.AssignedProjectOfficerUser!.FullName,
                x.AssignedProjectOfficerUser!.UserName,
                x.AssignedProjectOfficerUser!.Email
            })
            .Distinct()
            .ToListAsync();

        return candidates
            .GroupBy(x => x.UserId, StringComparer.Ordinal)
            .Select(group =>
            {
                var user = group.First();
                var displayName = user.FullName
                    ?? user.UserName
                    ?? user.Email
                    ?? user.UserId;

                return new ProjectIdeaOfficerOption(user.UserId, displayName);
            })
            .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.UserId, StringComparer.Ordinal)
            .ToList();
    }

    public Task<ProjectIdea?> GetDetailsAsync(int id)
    {
        IQueryable<ProjectIdea> query = _db.ProjectIdeas
            .Include(x => x.AssignedProjectOfficerUser)
            .Include(x => x.AssignedHodUser)
            .Include(x => x.CreatedByUser)
            .Include(x => x.Comments.Where(c => !c.IsDeleted)).ThenInclude(x => x.CreatedByUser)
            .Include(x => x.Notes.Where(n => !n.IsDeleted)).ThenInclude(x => x.CreatedByUser)
            .Include(x => x.Documents.Where(d => !d.IsDeleted)).ThenInclude(x => x.UploadedByUser);

        if (_db.Database.IsRelational())
        {
            query = query.AsSplitQuery();
        }

        return query.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    }

    public static DateTime GetLastActivity(ProjectIdea idea)
    {
        var dates = new[]
        {
            idea.UpdatedAt,
            idea.ArchivedAt ?? DateTime.MinValue,
            idea.Comments.Select(c => c.CreatedAt).DefaultIfEmpty(DateTime.MinValue).Max(),
            idea.Notes.Select(n => n.UpdatedAt > n.CreatedAt ? n.UpdatedAt : n.CreatedAt).DefaultIfEmpty(DateTime.MinValue).Max(),
            idea.Documents.Select(d => d.UploadedAt).DefaultIfEmpty(DateTime.MinValue).Max()
        };

        return dates.Max();
    }

    private static IQueryable<ProjectIdea> ApplyBoardFilters(
        IQueryable<ProjectIdea> ideas,
        string? query,
        bool myIdeas,
        string? userId,
        bool canViewAll,
        string? projectOfficerUserId,
        string? assignment)
    {
        ideas = ApplyBoardVisibility(ideas, userId, canViewAll);

        if (myIdeas)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return ideas.Where(_ => false);
            }

            // "My Ideas" is deliberately assignment-based. Creating an idea or being
            // its HoD reviewer does not make it part of the current user's My Ideas view.
            // This mode takes precedence over the generic officer/assignment filters.
            ideas = ideas.Where(x => x.AssignedProjectOfficerUserId == userId);
        }
        else
        {
            var selectedOfficerId = NormaliseUserId(projectOfficerUserId);
            if (selectedOfficerId is not null)
            {
                ideas = ideas.Where(x => x.AssignedProjectOfficerUserId == selectedOfficerId);
            }
            else
            {
                ideas = ProjectIdeaAssignmentFilters.Normalise(assignment) switch
                {
                    ProjectIdeaAssignmentFilters.Assigned =>
                        ideas.Where(x => x.AssignedProjectOfficerUserId != null),
                    ProjectIdeaAssignmentFilters.Unassigned =>
                        ideas.Where(x => x.AssignedProjectOfficerUserId == null),
                    _ => ideas
                };
            }
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

        return ideas;
    }

    private static IQueryable<ProjectIdea> ApplyBoardVisibility(
        IQueryable<ProjectIdea> ideas,
        string? userId,
        bool canViewAll)
    {
        ideas = ideas.Where(x => !x.IsDeleted);

        if (canViewAll)
        {
            return ideas;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return ideas.Where(_ => false);
        }

        return ideas.Where(x =>
            x.CreatedByUserId == userId ||
            x.AssignedProjectOfficerUserId == userId ||
            x.AssignedHodUserId == userId);
    }

    private static IEnumerable<ProjectIdea> SortIdeas(IEnumerable<ProjectIdea> ideas, string sort)
    {
        return sort switch
        {
            ProjectIdeaSorts.NewestCreated => ideas
                .OrderByDescending(x => x.CreatedAt)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase),

            ProjectIdeaSorts.Title => ideas
                .OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase),

            ProjectIdeaSorts.ProjectOfficer => ideas
                .OrderBy(x => DisplayOfficerName(x), StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase),

            _ => ideas
                .OrderByDescending(GetLastActivity)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string DisplayOfficerName(ProjectIdea idea)
    {
        var user = idea.AssignedProjectOfficerUser;
        return user?.FullName
            ?? user?.UserName
            ?? user?.Email
            ?? "\uFFFF";
    }

    private static string? NormaliseUserId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 450 ? trimmed : trimmed[..450];
    }
}
