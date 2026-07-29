using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Arpp;

/// <summary>
/// Published ARPP rows are the sole authority for whether the IPA lifecycle
/// milestone is externally managed. The first HQ-issued document containing the
/// project establishes the historical IPA completion date.
/// </summary>
public sealed class ArppIpaStageAuthorityService : IArppIpaStageAuthorityService
{
    private readonly ApplicationDbContext _db;

    public ArppIpaStageAuthorityService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<ArppIpaStageAuthority?> ResolveAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return null;
        }

        var authorities = await ResolveManyAsync([projectId], cancellationToken);
        return authorities.GetValueOrDefault(projectId);
    }

    public async Task<IReadOnlyDictionary<int, ArppIpaStageAuthority>> ResolveManyAsync(
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
            return new Dictionary<int, ArppIpaStageAuthority>();
        }

        var rows = await _db.ArppPublishedEntries
            .AsNoTracking()
            .Where(entry => entry.ProjectId.HasValue && ids.Contains(entry.ProjectId.Value))
            .Select(entry => new
            {
                ProjectId = entry.ProjectId!.Value,
                EntryId = entry.Id,
                entry.SerialNumber,
                entry.PppNumber,
                IssueId = entry.ArppIssueId,
                entry.PublishedIssue.FinancialYearStart,
                IssueKind = entry.PublishedIssue.Kind,
                entry.PublishedIssue.IssueSequence,
                IssueName = entry.PublishedIssue.Name,
                entry.PublishedIssue.IssueDate
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.ProjectId)
            .Select(group => group
                .OrderBy(row => row.IssueDate)
                .ThenBy(row => row.FinancialYearStart)
                .ThenBy(row => row.IssueSequence)
                .ThenBy(row => row.IssueId)
                .ThenBy(row => row.EntryId)
                .First())
            .ToDictionary(
                row => row.ProjectId,
                row => new ArppIpaStageAuthority(
                    row.ProjectId,
                    row.FinancialYearStart,
                    row.IssueId,
                    row.IssueKind,
                    row.IssueSequence,
                    row.IssueName,
                    row.IssueDate,
                    row.EntryId,
                    row.SerialNumber,
                    row.PppNumber));
    }

    public Task<bool> IsManagedAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return Task.FromResult(false);
        }

        return _db.ArppPublishedEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.ProjectId == projectId, cancellationToken);
    }

    public async Task EnsureManualLifecycleMutationAllowedAsync(
        int projectId,
        string stageCode,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(stageCode?.Trim(), StageCodes.IPA, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (await IsManagedAsync(projectId, cancellationToken))
        {
            throw new ArppManagedIpaStageException(projectId);
        }
    }
}
