using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;

namespace ProjectManagement.Services.Arpp;

public sealed class ArppLibraryService : IArppLibraryService
{
    private readonly ApplicationDbContext _db;
    private readonly IArppAttachmentStorage _storage;

    public ArppLibraryService(
        ApplicationDbContext db,
        IArppAttachmentStorage storage)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<ArppLibraryNavigation> GetNavigationAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = ArppLibrarySearch.Normalize(query);
        var snapshots = _db.ArppPublishedIssues.AsNoTracking();

        if (normalizedQuery is not null)
        {
            var search = normalizedQuery.ToLowerInvariant();
            var matchingCategories = ArppLibrarySearch.ResolveCategories(normalizedQuery).ToArray();

            snapshots = snapshots.Where(snapshot =>
                snapshot.Name.ToLower().Contains(search) ||
                snapshot.Entries.Any(entry =>
                    (entry.SerialNumber != null && entry.SerialNumber.ToLower().Contains(search)) ||
                    (entry.PppNumber != null && entry.PppNumber.ToLower().Contains(search)) ||
                    entry.ProjectReference.ToLower().Contains(search) ||
                    entry.Cfa.ToLower().Contains(search) ||
                    entry.Fund.ToLower().Contains(search) ||
                    entry.DfpdsSchedule.ToLower().Contains(search) ||
                    matchingCategories.Contains(entry.Category) ||
                    (entry.Project != null && entry.Project.Name.ToLower().Contains(search))));
        }

        var documents = await snapshots
            .OrderByDescending(snapshot => snapshot.FinancialYearStart)
            .ThenBy(snapshot => snapshot.IssueSequence)
            .Select(snapshot => new ArppLibraryDocumentLink(
                snapshot.ArppIssueId,
                snapshot.FinancialYearStart,
                snapshot.Kind,
                snapshot.IssueSequence,
                snapshot.Name,
                snapshot.IssueDate,
                snapshot.Entries.Count,
                snapshot.Entries.Sum(entry => (decimal?)(
                    entry.Category == ArppCategory.New ||
                    entry.Category == ArppCategory.CommittedLiability ||
                    entry.Category == ArppCategory.CarryForward
                        ? entry.IpaCost
                        : 0m)) ?? 0m,
                snapshot.Entries.Sum(entry => (decimal?)(
                    entry.Category == ArppCategory.Delisted
                        ? entry.IpaCost
                        : 0m)) ?? 0m))
            .ToListAsync(cancellationToken);

        var financialYears = documents
            .GroupBy(document => document.FinancialYearStart)
            .OrderByDescending(group => group.Key)
            .Select(group => new ArppLibraryFinancialYear(
                group.Key,
                group.OrderBy(document => document.IssueSequence).ToArray()))
            .ToArray();

        return new ArppLibraryNavigation(financialYears, documents.Count);
    }

    public async Task<ArppLibraryDocument?> GetDocumentAsync(
        long issueId,
        CancellationToken cancellationToken = default)
    {
        if (issueId <= 0)
        {
            return null;
        }

        var snapshot = await _db.ArppPublishedIssues
            .AsNoTracking()
            .Include(candidate => candidate.Entries)
                .ThenInclude(entry => entry.Project)
            .SingleOrDefaultAsync(candidate => candidate.ArppIssueId == issueId, cancellationToken);

        if (snapshot is null)
        {
            return null;
        }

        var rows = snapshot.Entries
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.Id)
            .Select(entry => new ArppLibraryRow(
                entry.Id,
                entry.SortOrder,
                entry.SerialNumber,
                entry.PppNumber,
                entry.ProjectReference,
                entry.ProjectId,
                entry.Project?.Name,
                entry.Project is null ? null : GetProjectStatus(entry.Project),
                entry.Category,
                entry.IpaCost,
                entry.Cfa,
                entry.Fund,
                entry.DfpdsSchedule))
            .ToArray();

        return new ArppLibraryDocument(
            snapshot.ArppIssueId,
            snapshot.RevisionNumber,
            snapshot.FinancialYearStart,
            snapshot.Kind,
            snapshot.IssueSequence,
            snapshot.Name,
            snapshot.IssueDate,
            snapshot.PublishedAtUtc,
            rows,
            new ArppLibraryAttachment(
                snapshot.AttachmentOriginalFileName,
                snapshot.AttachmentContentType,
                snapshot.AttachmentSizeBytes,
                snapshot.AttachmentSha256));
    }

    public async Task<ArppLibraryCurrentPosition?> GetCurrentPositionAsync(
        int financialYearStart,
        string? query,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.ArppPublishedEntries
            .AsNoTracking()
            .Where(entry => entry.PublishedIssue.FinancialYearStart == financialYearStart)
            .Include(entry => entry.Project)
            .Include(entry => entry.PublishedIssue)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        var linkedRows = rows
            .Where(entry =>
                entry.ProjectId.HasValue &&
                entry.Project is not null &&
                !entry.Project.IsDeleted)
            .ToArray();

        var current = linkedRows
            .GroupBy(entry => entry.ProjectId!.Value)
            .Select(group => group
                .OrderByDescending(entry => entry.PublishedIssue.IssueSequence)
                .ThenByDescending(entry => entry.PublishedIssue.IssueDate)
                .ThenByDescending(entry => entry.ArppIssueId)
                .ThenByDescending(entry => entry.Id)
                .First())
            .Select(entry => new ArppLibraryCurrentRow(
                entry.Id,
                entry.ProjectId!.Value,
                entry.ProjectReference,
                entry.Project!.Name,
                GetProjectStatus(entry.Project),
                entry.SerialNumber,
                entry.PppNumber,
                entry.Category,
                entry.IpaCost,
                entry.Cfa,
                entry.Fund,
                entry.DfpdsSchedule,
                entry.PublishedIssue.FinancialYearStart,
                entry.ArppIssueId,
                entry.PublishedIssue.Name,
                entry.PublishedIssue.Kind,
                entry.PublishedIssue.IssueSequence,
                entry.PublishedIssue.IssueDate))
            .ToArray();

        var allUnlinked = rows
            .Where(entry =>
                !entry.ProjectId.HasValue ||
                entry.Project is null ||
                entry.Project.IsDeleted)
            .OrderBy(entry => entry.PublishedIssue.IssueSequence)
            .ThenBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.Id)
            .Select(entry => new ArppLibraryUnlinkedRow(
                entry.Id,
                entry.ProjectReference,
                entry.SerialNumber,
                entry.PppNumber,
                entry.Category,
                entry.IpaCost,
                entry.Cfa,
                entry.Fund,
                entry.DfpdsSchedule,
                entry.PublishedIssue.FinancialYearStart,
                entry.ArppIssueId,
                entry.PublishedIssue.Name,
                entry.PublishedIssue.Kind,
                entry.PublishedIssue.IssueSequence,
                entry.PublishedIssue.IssueDate))
            .ToArray();

        var normalizedQuery = ArppLibrarySearch.Normalize(query);
        if (normalizedQuery is not null)
        {
            current = current
                .Where(row => ArppLibrarySearch.Matches(row, normalizedQuery))
                .ToArray();
        }

        var visibleUnlinked = normalizedQuery is null
            ? allUnlinked
            : allUnlinked
                .Where(row => ArppLibrarySearch.Matches(row, normalizedQuery))
                .ToArray();

        var approved = current
            .Where(row => row.Category is ArppCategory.New or ArppCategory.CommittedLiability or ArppCategory.CarryForward)
            .OrderBy(row => row.ProjectReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var delisted = current
            .Where(row => row.Category == ArppCategory.Delisted)
            .OrderBy(row => row.ProjectReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ArppLibraryCurrentPosition(
            financialYearStart,
            approved,
            delisted,
            visibleUnlinked,
            allUnlinked.Length);
    }

    public async Task<ArppLibraryProjectHistory?> GetProjectHistoryAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return null;
        }

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == projectId && !candidate.IsDeleted,
                cancellationToken);

        if (project is null)
        {
            return null;
        }

        var rows = await _db.ArppPublishedEntries
            .AsNoTracking()
            .Where(entry => entry.ProjectId == projectId)
            .OrderByDescending(entry => entry.PublishedIssue.FinancialYearStart)
            .ThenByDescending(entry => entry.PublishedIssue.IssueSequence)
            .ThenByDescending(entry => entry.PublishedIssue.IssueDate)
            .ThenByDescending(entry => entry.ArppIssueId)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new ArppLibraryProjectHistoryRow(
                entry.Id,
                entry.PublishedIssue.FinancialYearStart,
                entry.ArppIssueId,
                entry.PublishedIssue.Name,
                entry.PublishedIssue.Kind,
                entry.PublishedIssue.IssueSequence,
                entry.PublishedIssue.IssueDate,
                entry.SerialNumber,
                entry.PppNumber,
                entry.ProjectReference,
                entry.Category,
                entry.IpaCost,
                entry.Cfa,
                entry.Fund,
                entry.DfpdsSchedule))
            .ToListAsync(cancellationToken);

        return new ArppLibraryProjectHistory(
            project.Id,
            project.Name,
            project.CaseFileNumber,
            GetProjectStatus(project),
            rows);
    }

    public async Task<ArppLibraryAttachmentDownload?> OpenAttachmentAsync(
        long issueId,
        CancellationToken cancellationToken = default)
    {
        var attachment = await _db.ArppPublishedIssues
            .AsNoTracking()
            .Where(snapshot => snapshot.ArppIssueId == issueId)
            .Select(snapshot => new
            {
                snapshot.AttachmentStorageKey,
                snapshot.AttachmentContentType,
                snapshot.AttachmentOriginalFileName
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (attachment is null)
        {
            return null;
        }

        var stream = await _storage.OpenReadAsync(attachment.AttachmentStorageKey, cancellationToken);
        return stream is null
            ? null
            : new ArppLibraryAttachmentDownload(
                stream,
                attachment.AttachmentContentType,
                attachment.AttachmentOriginalFileName);
    }

    private static string GetProjectStatus(Project project)
        => project.IsArchived
            ? "Archived"
            : project.LifecycleStatus == ProjectLifecycleStatus.Completed
                ? "Completed"
                : project.LifecycleStatus == ProjectLifecycleStatus.Cancelled
                    ? "Cancelled"
                    : "Ongoing";
}
