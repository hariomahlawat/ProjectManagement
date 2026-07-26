using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;

namespace ProjectManagement.Services.Arpp;

public sealed class ArppReadService : IArppReadService
{
    private readonly ApplicationDbContext _db;

    public ArppReadService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<ArppRegisterResult> GetRegisterAsync(
        int? financialYearStart,
        string? query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query?.Trim();
        var issues = _db.ArppIssues.AsNoTracking();

        if (financialYearStart.HasValue)
        {
            issues = issues.Where(issue => issue.FinancialYearStart == financialYearStart.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            var pattern = $"%{normalizedQuery}%";
            issues = issues.Where(issue =>
                EF.Functions.ILike(issue.Name, pattern) ||
                issue.Entries.Any(entry =>
                    EF.Functions.ILike(entry.ProjectReference, pattern) ||
                    EF.Functions.ILike(entry.SerialNumber, pattern) ||
                    (entry.Project != null &&
                        (EF.Functions.ILike(entry.Project.Name, pattern) ||
                         (entry.Project.CaseFileNumber != null && EF.Functions.ILike(entry.Project.CaseFileNumber, pattern))))));
        }

        var rows = await issues
            .OrderByDescending(issue => issue.FinancialYearStart)
            .ThenBy(issue => issue.IssueSequence)
            .Select(issue => new ArppIssueListItem(
                issue.Id,
                issue.FinancialYearStart,
                issue.Kind,
                issue.IssueSequence,
                issue.Name,
                issue.IssueDate,
                issue.Entries.Count,
                issue.Entries.Sum(entry => (decimal?)entry.IpaCost) ?? 0m,
                issue.Entries.Count(entry => entry.Category == ArppCategory.New),
                issue.Entries.Count(entry => entry.Category == ArppCategory.CommittedLiability),
                issue.Entries.Count(entry => entry.Category == ArppCategory.CarryForward),
                issue.Entries.Count(entry => entry.Category == ArppCategory.Delisted),
                issue.Entries.Count(entry => entry.ProjectId != null),
                issue.Entries.Count(entry => entry.ProjectId == null),
                issue.UpdatedAtUtc,
                issue.Attachment != null))
            .ToListAsync(cancellationToken);

        var availableFinancialYears = await _db.ArppIssues
            .AsNoTracking()
            .Select(issue => issue.FinancialYearStart)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(cancellationToken);

        var groups = rows
            .GroupBy(row => row.FinancialYearStart)
            .OrderByDescending(group => group.Key)
            .Select(group => new ArppFinancialYearGroup(
                group.Key,
                group.OrderBy(row => row.IssueSequence).ToArray(),
                group.Sum(row => row.EntryCount),
                group.Sum(row => row.TotalIpaCost)))
            .ToArray();

        return new ArppRegisterResult(
            groups,
            availableFinancialYears,
            rows.Count,
            rows.Sum(row => row.EntryCount),
            rows.Sum(row => row.TotalIpaCost),
            rows.Sum(row => row.LinkedCount),
            rows.Sum(row => row.UnlinkedCount));
    }

    public async Task<ArppIssueDetails?> GetIssueAsync(
        long issueId,
        CancellationToken cancellationToken = default)
    {
        if (issueId <= 0)
        {
            return null;
        }

        var issue = await _db.ArppIssues
            .AsNoTracking()
            .Include(candidate => candidate.Entries)
                .ThenInclude(entry => entry.Project)
            .Include(candidate => candidate.Attachment)
            .SingleOrDefaultAsync(candidate => candidate.Id == issueId, cancellationToken);

        if (issue is null)
        {
            return null;
        }

        var entries = issue.Entries
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.Id)
            .Select(entry => new ArppEntryDetails(
                entry.Id,
                entry.SortOrder,
                entry.SerialNumber,
                entry.ProjectReference,
                entry.ProjectId,
                entry.Project?.Name,
                entry.Project?.CaseFileNumber,
                entry.Project is null ? null : GetProjectStatus(entry.Project),
                entry.Category,
                entry.IpaCost,
                entry.Cfa,
                entry.Fund,
                entry.DfpdsSchedule,
                Convert.ToBase64String(entry.RowVersion)))
            .ToArray();

        var categorySummary = entries
            .GroupBy(entry => entry.Category)
            .ToDictionary(
                group => group.Key,
                group => new ArppCategorySummary(group.Key, group.Count(), group.Sum(entry => entry.IpaCost)));

        foreach (var category in Enum.GetValues<ArppCategory>())
        {
            categorySummary.TryAdd(category, new ArppCategorySummary(category, 0, 0m));
        }

        return new ArppIssueDetails(
            issue.Id,
            issue.FinancialYearStart,
            issue.Kind,
            issue.IssueSequence,
            issue.Name,
            issue.IssueDate,
            Convert.ToBase64String(issue.RowVersion),
            entries,
            entries.Sum(entry => entry.IpaCost),
            categorySummary,
            entries.Count(entry => entry.ProjectId.HasValue),
            entries.Count(entry => !entry.ProjectId.HasValue),
            issue.CreatedAtUtc,
            issue.UpdatedAtUtc,
            issue.Attachment is null
                ? null
                : new ArppAttachmentDetails(
                    issue.Attachment.Id,
                    issue.Attachment.OriginalFileName,
                    issue.Attachment.ContentType,
                    issue.Attachment.SizeBytes,
                    issue.Attachment.Sha256,
                    issue.Attachment.UploadedByUserId,
                    issue.Attachment.UploadedAtUtc,
                    Convert.ToBase64String(issue.Attachment.RowVersion)));
    }

    public async Task<ArppProjectHistory?> GetProjectHistoryAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return null;
        }

        var project = await _db.Projects
            .AsNoTracking()
            .Where(candidate => candidate.Id == projectId && !candidate.IsDeleted)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Name,
                candidate.CaseFileNumber,
                Status = candidate.IsArchived
                    ? "Archived"
                    : candidate.LifecycleStatus == ProjectLifecycleStatus.Completed
                        ? "Completed"
                        : candidate.LifecycleStatus == ProjectLifecycleStatus.Cancelled
                            ? "Cancelled"
                            : "Ongoing"
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return null;
        }

        var rows = await _db.ArppEntries
            .AsNoTracking()
            .Where(entry => entry.ProjectId == projectId)
            .OrderByDescending(entry => entry.Issue.FinancialYearStart)
            .ThenByDescending(entry => entry.Issue.IssueSequence)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new
            {
                EntryId = entry.Id,
                IssueId = entry.ArppIssueId,
                entry.Issue.FinancialYearStart,
                entry.Issue.Kind,
                entry.Issue.IssueSequence,
                IssueName = entry.Issue.Name,
                entry.Issue.IssueDate,
                entry.SerialNumber,
                entry.Category,
                entry.IpaCost,
                entry.Cfa,
                entry.Fund,
                entry.DfpdsSchedule
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select((row, index) => new ArppProjectHistoryItem(
                row.EntryId,
                row.IssueId,
                row.FinancialYearStart,
                row.Kind,
                row.IssueSequence,
                row.IssueName,
                row.IssueDate,
                row.SerialNumber,
                row.Category,
                row.IpaCost,
                row.Cfa,
                row.Fund,
                row.DfpdsSchedule,
                IsAuthoritative: index == 0))
            .ToArray();

        return new ArppProjectHistory(
            project.Id,
            project.Name,
            project.CaseFileNumber,
            project.Status,
            items);
    }

    public async Task<int> GetSuggestedIssueSequenceAsync(
        int financialYearStart,
        CancellationToken cancellationToken = default)
    {
        var maximum = await _db.ArppIssues
            .AsNoTracking()
            .Where(issue => issue.FinancialYearStart == financialYearStart)
            .Select(issue => (int?)issue.IssueSequence)
            .MaxAsync(cancellationToken);

        return Math.Max(1, (maximum ?? 0) + 1);
    }

    public Task<bool> HasOriginalIssueAsync(
        int financialYearStart,
        CancellationToken cancellationToken = default)
        => _db.ArppIssues
            .AsNoTracking()
            .AnyAsync(
                issue => issue.FinancialYearStart == financialYearStart &&
                         issue.Kind == ArppIssueKind.Original,
                cancellationToken);

    private static string GetProjectStatus(Project project)
        => project.IsArchived
            ? "Archived"
            : project.LifecycleStatus == ProjectLifecycleStatus.Completed
                ? "Completed"
                : project.LifecycleStatus == ProjectLifecycleStatus.Cancelled
                    ? "Cancelled"
                    : "Ongoing";
}
