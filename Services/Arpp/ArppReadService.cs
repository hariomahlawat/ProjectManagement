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
        var hasSearchQuery = !string.IsNullOrWhiteSpace(normalizedQuery);
        var visibleIssues = _db.ArppIssues.AsNoTracking();

        if (financialYearStart.HasValue)
        {
            visibleIssues = visibleIssues.Where(issue => issue.FinancialYearStart == financialYearStart.Value);
        }

        if (hasSearchQuery)
        {
            var searchText = normalizedQuery!.ToLowerInvariant();
            visibleIssues = visibleIssues.Where(issue =>
                issue.Name.ToLower().Contains(searchText) ||
                issue.Entries.Any(entry =>
                    entry.ProjectReference.ToLower().Contains(searchText) ||
                    entry.SerialNumber.ToLower().Contains(searchText) ||
                    (entry.Project != null &&
                        (entry.Project.Name.ToLower().Contains(searchText) ||
                         (entry.Project.CaseFileNumber != null && entry.Project.CaseFileNumber.ToLower().Contains(searchText))))));
        }

        var rows = await visibleIssues
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
                issue.Entries.Sum(entry => (decimal?)(
                    entry.Category == ArppCategory.New ||
                    entry.Category == ArppCategory.CommittedLiability ||
                    entry.Category == ArppCategory.CarryForward
                        ? entry.IpaCost
                        : 0m)) ?? 0m,
                issue.Entries.Sum(entry => (decimal?)(
                    entry.Category == ArppCategory.Delisted
                        ? entry.IpaCost
                        : 0m)) ?? 0m,
                issue.Entries.Count(entry => entry.Category == ArppCategory.New),
                issue.Entries.Count(entry => entry.Category == ArppCategory.CommittedLiability),
                issue.Entries.Count(entry => entry.Category == ArppCategory.CarryForward),
                issue.Entries.Count(entry => entry.Category == ArppCategory.Delisted),
                issue.Entries.Count(entry => entry.ProjectId != null),
                issue.Entries.Count(entry => entry.ProjectId == null),
                issue.UpdatedAtUtc,
                issue.Attachment != null,
                issue.IsVerified,
                issue.VerifiedAtUtc))
            .ToListAsync(cancellationToken);

        var visibleIssueIds = rows.Select(row => row.Id).ToArray();
        var displayedEntries = visibleIssueIds.Length == 0
            ? new List<ScopedEntry>()
            : await _db.ArppEntries
                .AsNoTracking()
                .Where(entry => visibleIssueIds.Contains(entry.ArppIssueId))
                .Select(entry => new ScopedEntry(
                    entry.Id,
                    entry.ArppIssueId,
                    entry.ProjectId,
                    entry.Category,
                    entry.IpaCost,
                    entry.Issue.FinancialYearStart,
                    entry.Issue.IssueSequence,
                    entry.Issue.IssueDate))
                .ToListAsync(cancellationToken);

        // A free-text search controls which documents are displayed, but it must not make
        // an older matching row authoritative. Resolve the latest position of every linked
        // project found in those documents from the complete applicable ARPP history.
        IReadOnlyList<ScopedEntry> authorityCandidates = displayedEntries;
        if (hasSearchQuery)
        {
            var relevantProjectIds = displayedEntries
                .Where(entry => entry.ProjectId.HasValue)
                .Select(entry => entry.ProjectId!.Value)
                .Distinct()
                .ToArray();

            if (relevantProjectIds.Length == 0)
            {
                authorityCandidates = [];
            }
            else
            {
                var candidates = _db.ArppEntries
                    .AsNoTracking()
                    .Where(entry => entry.ProjectId.HasValue && relevantProjectIds.Contains(entry.ProjectId.Value));

                if (financialYearStart.HasValue)
                {
                    candidates = candidates.Where(entry => entry.Issue.FinancialYearStart == financialYearStart.Value);
                }

                authorityCandidates = await candidates
                    .Select(entry => new ScopedEntry(
                        entry.Id,
                        entry.ArppIssueId,
                        entry.ProjectId,
                        entry.Category,
                        entry.IpaCost,
                        entry.Issue.FinancialYearStart,
                        entry.Issue.IssueSequence,
                        entry.Issue.IssueDate))
                    .ToListAsync(cancellationToken);
            }
        }

        var availableFinancialYears = await _db.ArppIssues
            .AsNoTracking()
            .Select(issue => issue.FinancialYearStart)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(cancellationToken);

        var groups = rows
            .GroupBy(row => row.FinancialYearStart)
            .OrderByDescending(group => group.Key)
            .Select(group =>
            {
                var groupIssueIds = group.Select(item => item.Id).ToHashSet();
                var groupDisplayedEntries = displayedEntries
                    .Where(entry => groupIssueIds.Contains(entry.IssueId))
                    .ToArray();
                var groupRelevantProjectIds = groupDisplayedEntries
                    .Where(entry => entry.ProjectId.HasValue)
                    .Select(entry => entry.ProjectId!.Value)
                    .ToHashSet();
                var groupAuthoritySource = hasSearchQuery
                    ? authorityCandidates
                        .Where(entry =>
                            entry.FinancialYearStart == group.Key &&
                            entry.ProjectId.HasValue &&
                            groupRelevantProjectIds.Contains(entry.ProjectId.Value))
                        .ToArray()
                    : groupDisplayedEntries;
                var groupAuthoritative = GetAuthoritativeLinkedEntries(groupAuthoritySource);
                var groupApproved = groupAuthoritative
                    .Where(entry => IsApprovedCategory(entry.Category))
                    .ToArray();
                var groupDelisted = groupAuthoritative
                    .Where(entry => entry.Category == ArppCategory.Delisted)
                    .ToArray();
                var groupUnlinked = groupDisplayedEntries.Where(entry => !entry.ProjectId.HasValue).ToArray();

                return new ArppFinancialYearGroup(
                    group.Key,
                    group.OrderBy(row => row.IssueSequence).ToArray(),
                    group.Sum(row => row.EntryCount),
                    groupApproved.Sum(entry => entry.IpaCost),
                    groupDelisted.Sum(entry => entry.IpaCost),
                    groupUnlinked.Sum(entry => entry.IpaCost),
                    groupApproved.Length,
                    groupDelisted.Length,
                    groupUnlinked.Length);
            })
            .ToArray();

        var authoritativeLinked = GetAuthoritativeLinkedEntries(authorityCandidates);
        var approvedLinked = authoritativeLinked
            .Where(entry => IsApprovedCategory(entry.Category))
            .ToArray();
        var delistedLinked = authoritativeLinked
            .Where(entry => entry.Category == ArppCategory.Delisted)
            .ToArray();
        var unlinkedEntries = displayedEntries.Where(entry => !entry.ProjectId.HasValue).ToArray();

        return new ArppRegisterResult(
            groups,
            availableFinancialYears,
            rows.Count,
            rows.Sum(row => row.EntryCount),
            approvedLinked.Sum(entry => entry.IpaCost),
            delistedLinked.Sum(entry => entry.IpaCost),
            unlinkedEntries.Sum(entry => entry.IpaCost),
            approvedLinked.Length,
            delistedLinked.Length,
            unlinkedEntries.Length,
            rows.Count(row => row.IsVerified));
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
            .Include(candidate => candidate.PublishedSnapshot)
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
                entry.CfaOptionId,
                entry.Cfa,
                entry.FundOptionId,
                entry.Fund,
                entry.DfpdsScheduleId,
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

        string? verifiedByDisplayName = null;
        if (!string.IsNullOrWhiteSpace(issue.VerifiedByUserId))
        {
            var verifier = await _db.Users
                .AsNoTracking()
                .Where(user => user.Id == issue.VerifiedByUserId)
                .Select(user => new
                {
                    user.Rank,
                    user.FullName,
                    user.UserName,
                    user.Email
                })
                .SingleOrDefaultAsync(cancellationToken);

            verifiedByDisplayName = verifier is null
                ? issue.VerifiedByUserId
                : BuildUserDisplayName(
                    verifier.Rank,
                    verifier.FullName,
                    verifier.UserName,
                    verifier.Email,
                    issue.VerifiedByUserId);
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
                    Convert.ToBase64String(issue.Attachment.RowVersion)),
            issue.IsVerified,
            issue.VerifiedAtUtc,
            issue.VerifiedByUserId,
            verifiedByDisplayName,
            issue.VerificationNote)
        {
            HasPublishedSnapshot = issue.PublishedSnapshot is not null,
            PublishedRevisionNumber = issue.PublishedSnapshot?.RevisionNumber,
            PublishedAtUtc = issue.PublishedSnapshot?.PublishedAtUtc
        };
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

    private static IReadOnlyList<ScopedEntry> GetAuthoritativeLinkedEntries(
        IEnumerable<ScopedEntry> entries)
        => entries
            .Where(entry => entry.ProjectId.HasValue)
            .GroupBy(entry => entry.ProjectId!.Value)
            .Select(group => group
                .OrderByDescending(entry => entry.FinancialYearStart)
                .ThenByDescending(entry => entry.IssueSequence)
                .ThenByDescending(entry => entry.IssueDate)
                .ThenByDescending(entry => entry.IssueId)
                .ThenByDescending(entry => entry.EntryId)
                .First())
            .ToArray();

    private static bool IsApprovedCategory(ArppCategory category)
        => category is ArppCategory.New
            or ArppCategory.CommittedLiability
            or ArppCategory.CarryForward;

    private static string GetProjectStatus(Project project)
        => project.IsArchived
            ? "Archived"
            : project.LifecycleStatus == ProjectLifecycleStatus.Completed
                ? "Completed"
                : project.LifecycleStatus == ProjectLifecycleStatus.Cancelled
                    ? "Cancelled"
                    : "Ongoing";

    private static string BuildUserDisplayName(
        string? rank,
        string? fullName,
        string? userName,
        string? email,
        string fallbackUserId)
    {
        var normalizedRank = rank?.Trim();
        var normalizedName = fullName?.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedRank) && !string.IsNullOrWhiteSpace(normalizedName))
        {
            return $"{normalizedRank} {normalizedName}";
        }

        if (!string.IsNullOrWhiteSpace(normalizedName))
        {
            return normalizedName;
        }

        var normalizedUserName = userName?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedUserName))
        {
            return normalizedUserName;
        }

        var normalizedEmail = email?.Trim();
        return string.IsNullOrWhiteSpace(normalizedEmail)
            ? fallbackUserId
            : normalizedEmail;
    }

    private sealed record ScopedEntry(
        long EntryId,
        long IssueId,
        int? ProjectId,
        ArppCategory Category,
        decimal IpaCost,
        int FinancialYearStart,
        int IssueSequence,
        DateOnly IssueDate);
}
