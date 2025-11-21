using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models;
using ProjectManagement.ViewModels.Dashboard;

namespace ProjectManagement.Services.Dashboard;

// SECTION: Search and OCR health service contract
public interface ISearchHealthService
{
    Task<SearchHealthVm> GetAsync(CancellationToken cancellationToken);
}

// SECTION: Search and OCR health service
public sealed class SearchHealthService : ISearchHealthService
{
    private static readonly TimeSpan WorkerActivityWindow = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _db;

    public SearchHealthService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<SearchHealthVm> GetAsync(CancellationToken cancellationToken)
    {
        // SECTION: Prepare base queries
        var docRepoBaseQuery = _db.Documents
            .AsNoTracking()
            .Where(document => !document.IsDeleted && !document.IsExternal && document.IsActive);

        var projectDocBaseQuery = _db.ProjectDocuments
            .AsNoTracking()
            .Where(document => document.Status == ProjectDocumentStatus.Published && !document.IsArchived);
        // END SECTION

        // SECTION: Searchable corpus aggregation
        var docRepoSearchableTask = docRepoBaseQuery
            .Where(document => document.SearchVector != null)
            .LongCountAsync(cancellationToken);

        var projectDocsSearchableTask = projectDocBaseQuery
            .Where(document => document.SearchVector != null)
            .LongCountAsync(cancellationToken);

        var projectReportsSearchable = 0L;
        // END SECTION

        // SECTION: OCR snapshot aggregation
        var docRepoSucceededTask = docRepoBaseQuery
            .Where(document => document.OcrStatus == DocOcrStatus.Succeeded)
            .LongCountAsync(cancellationToken);
        var docRepoPendingTask = docRepoBaseQuery
            .Where(document => document.OcrStatus == DocOcrStatus.Pending)
            .LongCountAsync(cancellationToken);
        var docRepoFailedTask = docRepoBaseQuery
            .Where(document => document.OcrStatus == DocOcrStatus.Failed)
            .LongCountAsync(cancellationToken);

        var projectDocsSucceededTask = projectDocBaseQuery
            .Where(document => document.OcrStatus == ProjectDocumentOcrStatus.Succeeded)
            .LongCountAsync(cancellationToken);
        var projectDocsPendingTask = projectDocBaseQuery
            .Where(document => document.OcrStatus == ProjectDocumentOcrStatus.Pending)
            .LongCountAsync(cancellationToken);
        var projectDocsFailedTask = projectDocBaseQuery
            .Where(document => document.OcrStatus == ProjectDocumentOcrStatus.Failed)
            .LongCountAsync(cancellationToken);
        // END SECTION

        // SECTION: OCR trend aggregation
        var trendStartDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-6));
        var trendStartUtc = trendStartDate.ToDateTime(TimeOnly.MinValue);

        var docRepoTrendTask = docRepoBaseQuery
            .Where(document => document.OcrStatus == DocOcrStatus.Succeeded && document.OcrLastTriedUtc.HasValue)
            .Where(document => document.OcrLastTriedUtc!.Value.UtcDateTime >= trendStartUtc)
            .GroupBy(document => DateOnly.FromDateTime(document.OcrLastTriedUtc!.Value.UtcDateTime.Date))
            .Select(group => new KeyValuePair<DateOnly, int>(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        var projectDocsTrendTask = projectDocBaseQuery
            .Where(document => document.OcrStatus == ProjectDocumentOcrStatus.Succeeded && document.OcrLastTriedUtc.HasValue)
            .Where(document => document.OcrLastTriedUtc!.Value.UtcDateTime >= trendStartUtc)
            .GroupBy(document => DateOnly.FromDateTime(document.OcrLastTriedUtc!.Value.UtcDateTime.Date))
            .Select(group => new KeyValuePair<DateOnly, int>(group.Key, group.Count()))
            .ToListAsync(cancellationToken);
        // END SECTION

        // SECTION: Pending age + worker activity aggregation
        var docRepoPendingOldestTask = docRepoBaseQuery
            .Where(document => document.OcrStatus == DocOcrStatus.Pending)
            .Select(document => (DateTimeOffset?)DateTime.SpecifyKind(document.CreatedAtUtc, DateTimeKind.Utc))
            .OrderBy(value => value)
            .FirstOrDefaultAsync(cancellationToken);

        var projectDocPendingOldestTask = projectDocBaseQuery
            .Where(document => document.OcrStatus == ProjectDocumentOcrStatus.Pending)
            .Select(document => (DateTimeOffset?)document.UploadedAtUtc)
            .OrderBy(value => value)
            .FirstOrDefaultAsync(cancellationToken);

        var docRepoLastAttemptTask = docRepoBaseQuery
            .Where(document => document.OcrLastTriedUtc.HasValue)
            .Select(document => document.OcrLastTriedUtc)
            .OrderByDescending(value => value)
            .FirstOrDefaultAsync(cancellationToken);

        var projectDocLastAttemptTask = projectDocBaseQuery
            .Where(document => document.OcrLastTriedUtc.HasValue)
            .Select(document => document.OcrLastTriedUtc)
            .OrderByDescending(value => value)
            .FirstOrDefaultAsync(cancellationToken);
        // END SECTION

        await Task.WhenAll(
            docRepoSearchableTask,
            projectDocsSearchableTask,
            docRepoSucceededTask,
            docRepoPendingTask,
            docRepoFailedTask,
            projectDocsSucceededTask,
            projectDocsPendingTask,
            projectDocsFailedTask,
            docRepoTrendTask,
            projectDocsTrendTask,
            docRepoPendingOldestTask,
            projectDocPendingOldestTask,
            docRepoLastAttemptTask,
            projectDocLastAttemptTask
        ).ConfigureAwait(false);

        // SECTION: Combine results
        var docRepoSearchable = docRepoSearchableTask.Result;
        var projectDocsSearchable = projectDocsSearchableTask.Result;

        var ocrSnapshot = new SearchHealthOcrSnapshot
        {
            Succeeded = docRepoSucceededTask.Result + projectDocsSucceededTask.Result,
            Pending = docRepoPendingTask.Result + projectDocsPendingTask.Result,
            Failed = docRepoFailedTask.Result + projectDocsFailedTask.Result,
            DocRepoSucceeded = docRepoSucceededTask.Result,
            DocRepoPending = docRepoPendingTask.Result,
            DocRepoFailed = docRepoFailedTask.Result,
            ProjectDocumentsSucceeded = projectDocsSucceededTask.Result,
            ProjectDocumentsPending = projectDocsPendingTask.Result,
            ProjectDocumentsFailed = projectDocsFailedTask.Result
        };

        var totalSearchable = docRepoSearchable + projectDocsSearchable + projectReportsSearchable;

        var trendValues = BuildTrend(trendStartDate, docRepoTrendTask.Result, projectDocsTrendTask.Result);
        var oldestPending = MinNonNull(docRepoPendingOldestTask.Result, projectDocPendingOldestTask.Result);
        var lastAttempt = MaxNonNull(docRepoLastAttemptTask.Result, projectDocLastAttemptTask.Result);
        // END SECTION

        return new SearchHealthVm
        {
            TotalSearchable = totalSearchable,
            DocRepoSearchable = docRepoSearchable,
            ProjectDocumentsSearchable = projectDocsSearchable,
            ProjectReportsSearchable = projectReportsSearchable,
            IncludeProjectReports = projectReportsSearchable > 0,
            Ocr = ocrSnapshot,
            OcrCompletionsTrend = trendValues,
            OldestPendingLabel = FormatPendingAge(oldestPending),
            WorkerActive = IsWorkerActive(lastAttempt)
        };
    }

    // SECTION: Helpers
    private static List<int> BuildTrend(
        DateOnly startDate,
        IReadOnlyCollection<KeyValuePair<DateOnly, int>> docRepoTrend,
        IReadOnlyCollection<KeyValuePair<DateOnly, int>> projectDocsTrend)
    {
        var window = Enumerable.Range(0, 7).Select(offset => startDate.AddDays(offset)).ToArray();
        var merged = new List<int>(window.Length);

        foreach (var day in window)
        {
            var docRepo = docRepoTrend.Where(pair => pair.Key == day).Select(pair => pair.Value).FirstOrDefault();
            var projectDocs = projectDocsTrend.Where(pair => pair.Key == day).Select(pair => pair.Value).FirstOrDefault();
            merged.Add(docRepo + projectDocs);
        }

        return merged;
    }

    private static DateTimeOffset? MinNonNull(params DateTimeOffset?[] values)
    {
        var filtered = values.Where(value => value.HasValue).ToArray();
        return filtered.Length == 0 ? null : filtered.Min();
    }

    private static DateTimeOffset? MaxNonNull(params DateTimeOffset?[] values)
    {
        var filtered = values.Where(value => value.HasValue).ToArray();
        return filtered.Length == 0 ? null : filtered.Max();
    }

    private static string? FormatPendingAge(DateTimeOffset? oldest)
    {
        if (!oldest.HasValue)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (oldest.Value > now)
        {
            return null;
        }

        var span = now - oldest.Value;

        if (span.TotalMinutes < 1)
        {
            return "Just now";
        }

        var parts = new List<string>();
        if (span.Days > 0)
        {
            parts.Add($"{span.Days}d");
        }

        if (span.Hours > 0)
        {
            parts.Add($"{span.Hours}h");
        }

        if (parts.Count < 2 && span.Minutes > 0)
        {
            parts.Add($"{span.Minutes}m");
        }

        return parts.Count == 0 ? "< 1m" : string.Join(' ', parts);
    }

    private static bool IsWorkerActive(DateTimeOffset? lastAttempt)
    {
        if (!lastAttempt.HasValue)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        return now - lastAttempt.Value <= WorkerActivityWindow;
    }
    // END SECTION
}
