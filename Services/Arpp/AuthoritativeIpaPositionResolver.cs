using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Arpp;

public sealed class AuthoritativeIpaPositionResolver : IAuthoritativeIpaPositionResolver
{
    private readonly ApplicationDbContext _db;

    public AuthoritativeIpaPositionResolver(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<AuthoritativeIpaPosition?> ResolveAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return null;
        }

        var positions = await ResolveManyAsync(new[] { projectId }, cancellationToken);
        return positions.GetValueOrDefault(projectId);
    }

    public async Task<IReadOnlyDictionary<int, AuthoritativeIpaPosition>> ResolveManyAsync(
        IReadOnlyCollection<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);

        var ids = projectIds
            .Where(projectId => projectId > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<int, AuthoritativeIpaPosition>();
        }

        // Only the latest verified/published snapshots are authoritative across PRISM.
        // A manager may unlock and correct the working issue without exposing those
        // unverified changes to project pages, dashboards or financial reporting.
        var arppRows = await _db.ArppPublishedEntries
            .AsNoTracking()
            .Where(entry => entry.ProjectId.HasValue && ids.Contains(entry.ProjectId.Value))
            .Select(entry => new
            {
                ProjectId = entry.ProjectId!.Value,
                entry.IpaCost,
                entry.Category,
                entry.SerialNumber,
                EntryId = entry.Id,
                IssueId = entry.ArppIssueId,
                entry.PublishedIssue.FinancialYearStart,
                entry.PublishedIssue.IssueSequence,
                entry.PublishedIssue.IssueDate,
                IssueName = entry.PublishedIssue.Name
            })
            .ToListAsync(cancellationToken);

        var result = arppRows
            .GroupBy(row => row.ProjectId)
            .Select(group => group
                .OrderByDescending(row => row.FinancialYearStart)
                .ThenByDescending(row => row.IssueSequence)
                .ThenByDescending(row => row.IssueDate)
                .ThenByDescending(row => row.IssueId)
                .ThenByDescending(row => row.EntryId)
                .First())
            .ToDictionary(
                row => row.ProjectId,
                row => new AuthoritativeIpaPosition(
                    row.ProjectId,
                    row.IpaCost,
                    IpaPositionSource.Arpp,
                    row.Category,
                    row.FinancialYearStart,
                    row.IssueId,
                    row.IssueName,
                    row.IssueDate,
                    row.IssueSequence,
                    row.EntryId,
                    row.SerialNumber));

        var unresolvedIds = ids
            .Where(projectId => !result.ContainsKey(projectId))
            .ToArray();

        if (unresolvedIds.Length == 0)
        {
            return result;
        }

        // Transitional fallback: preserve the existing project-level IPA record until
        // the project is linked to at least one ARPP entry.
        var legacyRows = await _db.ProjectIpaFacts
            .AsNoTracking()
            .Where(fact => unresolvedIds.Contains(fact.ProjectId))
            .Select(fact => new
            {
                fact.ProjectId,
                fact.IpaCost,
                fact.CreatedOnUtc,
                fact.Id
            })
            .ToListAsync(cancellationToken);

        foreach (var row in legacyRows
                     .GroupBy(fact => fact.ProjectId)
                     .Select(group => group
                         .OrderByDescending(fact => fact.CreatedOnUtc)
                         .ThenByDescending(fact => fact.Id)
                         .First()))
        {
            result[row.ProjectId] = new AuthoritativeIpaPosition(
                row.ProjectId,
                row.IpaCost,
                IpaPositionSource.LegacyProjectFact,
                Category: null,
                FinancialYearStart: null,
                IssueId: null,
                IssueName: null,
                IssueDate: null,
                IssueSequence: null,
                EntryId: null,
                SerialNumber: null);
        }

        return result;
    }
}
