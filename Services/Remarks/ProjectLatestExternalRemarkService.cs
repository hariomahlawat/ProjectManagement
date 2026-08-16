using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Remarks;

namespace ProjectManagement.Services.Remarks;

public sealed record ProjectLatestExternalRemark(
    int ProjectId,
    int RemarkId,
    string Body,
    DateOnly EventDate,
    DateTime EffectiveAtUtc);

public interface IProjectLatestExternalRemarkService
{
    Task<IReadOnlyDictionary<int, ProjectLatestExternalRemark>> GetLatestAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Authoritative reader for the latest External / General project remark used
/// by formal outputs. "Latest" follows the existing briefing rule: the most
/// recently edited remark, otherwise the most recently created remark.
/// </summary>
public sealed class ProjectLatestExternalRemarkService : IProjectLatestExternalRemarkService
{
    private readonly ApplicationDbContext _db;

    public ProjectLatestExternalRemarkService(ApplicationDbContext db)
        => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<IReadOnlyDictionary<int, ProjectLatestExternalRemark>> GetLatestAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);

        var ids = projectIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<int, ProjectLatestExternalRemark>();
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

                    return new ProjectLatestExternalRemark(
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
