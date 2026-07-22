using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Remarks;

namespace ProjectManagement.Services.ProjectBriefings;

public sealed record ProjectBriefingExternalStatus(
    int ProjectId,
    int RemarkId,
    string Body,
    DateOnly EventDate,
    DateTime EffectiveAtUtc);

public interface IProjectBriefingExternalStatusService
{
    Task<IReadOnlyDictionary<int, ProjectBriefingExternalStatus>> GetLatestAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectBriefingExternalStatusService : IProjectBriefingExternalStatusService
{
    private readonly ApplicationDbContext _db;

    public ProjectBriefingExternalStatusService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyDictionary<int, ProjectBriefingExternalStatus>> GetLatestAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        var ids = projectIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<int, ProjectBriefingExternalStatus>();
        }

        var rows = await _db.Remarks
            .AsNoTracking()
            .Where(remark => ids.Contains(remark.ProjectId)
                && !remark.IsDeleted
                && remark.Type == RemarkType.External
                && remark.Scope == RemarkScope.General
                && remark.Body != null
                && remark.Body.Trim() != string.Empty)
            .Select(remark => new
            {
                remark.Id,
                remark.ProjectId,
                remark.Body,
                remark.EventDate,
                remark.CreatedAtUtc,
                remark.LastEditedAtUtc
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.ProjectId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group
                        .OrderByDescending(row => row.LastEditedAtUtc ?? row.CreatedAtUtc)
                        .ThenByDescending(row => row.Id)
                        .First();

                    return new ProjectBriefingExternalStatus(
                        latest.ProjectId,
                        latest.Id,
                        Normalize(latest.Body),
                        latest.EventDate,
                        latest.LastEditedAtUtc ?? latest.CreatedAtUtc);
                });
    }

    private static string Normalize(string value)
        => string.Join(" ", value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
