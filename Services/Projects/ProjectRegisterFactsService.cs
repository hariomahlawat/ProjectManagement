using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// Reads the Projects register's facts for every active project (not archived, not deleted)
/// from <see cref="OngoingProjectsReadService"/>, adding each project's last update. Archived
/// active projects have no facts; the register shows them without timing.
/// </summary>
public sealed class ProjectRegisterFactsService
{
    private readonly ApplicationDbContext _db;
    private readonly OngoingProjectsReadService _ongoing;
    private readonly IClock _clock;

    public ProjectRegisterFactsService(ApplicationDbContext db, OngoingProjectsReadService ongoing, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _ongoing = ongoing ?? throw new ArgumentNullException(nameof(ongoing));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyDictionary<int, ProjectRegisterActiveFacts>> GetActiveFactsAsync(CancellationToken cancellationToken)
    {
        var rows = await _ongoing.GetAsync(
            Array.Empty<int>(),
            leadPoUserId: null,
            search: null,
            presentStageCode: null,
            Array.Empty<string>(),
            stageFlow: null,
            cancellationToken);

        if (rows.Count == 0)
        {
            return new Dictionary<int, ProjectRegisterActiveFacts>();
        }

        var today = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);
        var lastUpdated = await LoadLastUpdatedAsync(rows.Select(row => row.ProjectId).ToArray(), cancellationToken);

        return rows.ToDictionary(
            row => row.ProjectId,
            row => ProjectRegisterActiveFacts.From(
                row,
                lastUpdated.TryGetValue(row.ProjectId, out var updatedOn) ? updatedOn : today,
                today));
    }

    // SECTION: Last update = the latest remark written or edited, or content edit, else creation
    // (the same activity the repository's operational order uses), as an IST date.
    private async Task<IReadOnlyDictionary<int, DateOnly>> LoadLastUpdatedAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken)
    {
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.CreatedAt, p.ContentUpdatedAtUtc })
            .ToListAsync(cancellationToken);

        var latestRemarks = await _db.Remarks
            .AsNoTracking()
            .Where(r => projectIds.Contains(r.ProjectId) && !r.IsDeleted)
            .GroupBy(r => r.ProjectId)
            .Select(g => new { ProjectId = g.Key, LatestUtc = g.Max(r => r.LastEditedAtUtc ?? r.CreatedAtUtc) })
            .ToDictionaryAsync(x => x.ProjectId, x => x.LatestUtc, cancellationToken);

        var result = new Dictionary<int, DateOnly>(projects.Count);
        foreach (var project in projects)
        {
            var latest = DateTime.SpecifyKind(project.CreatedAt, DateTimeKind.Utc);
            if (project.ContentUpdatedAtUtc is { } content && content.UtcDateTime > latest)
            {
                latest = content.UtcDateTime;
            }

            if (latestRemarks.TryGetValue(project.Id, out var remarkUtc) && remarkUtc > latest)
            {
                latest = DateTime.SpecifyKind(remarkUtc, DateTimeKind.Utc);
            }

            result[project.Id] = DateOnly.FromDateTime(IstClock.ToIst(latest));
        }

        return result;
    }
}
