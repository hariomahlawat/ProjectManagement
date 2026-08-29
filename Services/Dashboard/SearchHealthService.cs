using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models;
using ProjectManagement.ViewModels.Dashboard;
using ProjectManagement.Services.SearchV2.Indexing;

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
    private readonly ISearchIndexStore _searchIndexStore;

    public SearchHealthService(ApplicationDbContext db, ISearchIndexStore searchIndexStore)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _searchIndexStore = searchIndexStore ?? throw new ArgumentNullException(nameof(searchIndexStore));
    }

    public async Task<SearchHealthVm> GetAsync(CancellationToken cancellationToken)
    {
        // SECTION: Prepare base queries
        var docRepoBaseQuery = _db.Documents
            .AsNoTracking()
            // Align with the global Document Repository search so counts match the searchable corpus users see there.
            .Where(document => !document.IsDeleted);

        var projectDocBaseQuery = _db.ProjectDocuments
            .AsNoTracking()
            .Where(document => document.Status == ProjectDocumentStatus.Published && !document.IsArchived);
        // END SECTION

        // SECTION: Searchable corpus aggregation
        var docRepoSearchable = await docRepoBaseQuery
            .Where(document => document.SearchVector != null)
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false);

        var projectDocsSearchable = await projectDocBaseQuery
            .Where(document => document.SearchVector != null)
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false);

        var projectReportsSearchable = 0L;
        // END SECTION

        // SECTION: OCR snapshot aggregation
        var docRepoSucceeded = await docRepoBaseQuery
            .Where(document => document.OcrStatus == DocOcrStatus.Succeeded)
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false);
        var docRepoPending = await docRepoBaseQuery
            .Where(document => document.OcrStatus == DocOcrStatus.Pending)
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false);
        var docRepoFailed = await docRepoBaseQuery
            .Where(document => document.OcrStatus == DocOcrStatus.Failed)
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false);

        var projectDocsSucceeded = await projectDocBaseQuery
            .Where(document => document.OcrStatus == ProjectDocumentOcrStatus.Succeeded)
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false);
        var projectDocsPending = await projectDocBaseQuery
            .Where(document => document.OcrStatus == ProjectDocumentOcrStatus.Pending)
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false);
        var projectDocsFailed = await projectDocBaseQuery
            .Where(document => document.OcrStatus == ProjectDocumentOcrStatus.Failed)
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false);
        // END SECTION

        // SECTION: OCR trend aggregation
        var trendStartDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-6));
        var trendStartUtc = trendStartDate.ToDateTime(TimeOnly.MinValue);

        var docRepoTrend = await docRepoBaseQuery
            .Where(document => document.OcrStatus == DocOcrStatus.Succeeded && document.OcrLastTriedUtc.HasValue)
            .Where(document => document.OcrLastTriedUtc!.Value.UtcDateTime >= trendStartUtc)
            .GroupBy(document => DateOnly.FromDateTime(document.OcrLastTriedUtc!.Value.UtcDateTime.Date))
            .Select(group => new KeyValuePair<DateOnly, int>(group.Key, group.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var projectDocsTrend = await projectDocBaseQuery
            .Where(document => document.OcrStatus == ProjectDocumentOcrStatus.Succeeded && document.OcrLastTriedUtc.HasValue)
            .Where(document => document.OcrLastTriedUtc!.Value.UtcDateTime >= trendStartUtc)
            .GroupBy(document => DateOnly.FromDateTime(document.OcrLastTriedUtc!.Value.UtcDateTime.Date))
            .Select(group => new KeyValuePair<DateOnly, int>(group.Key, group.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // END SECTION

        // SECTION: Pending age + worker activity aggregation
        var docRepoPendingOldest = await docRepoBaseQuery
            .Where(document => document.OcrStatus == DocOcrStatus.Pending)
            .Select(document => (DateTimeOffset?)DateTime.SpecifyKind(document.CreatedAtUtc, DateTimeKind.Utc))
            .OrderBy(value => value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var projectDocPendingOldest = await projectDocBaseQuery
            .Where(document => document.OcrStatus == ProjectDocumentOcrStatus.Pending)
            .Select(document => (DateTimeOffset?)document.UploadedAtUtc)
            .OrderBy(value => value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var docRepoLastAttempt = await docRepoBaseQuery
            .Where(document => document.OcrLastTriedUtc.HasValue)
            .Select(document => document.OcrLastTriedUtc)
            .OrderByDescending(value => value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var projectDocLastAttempt = await projectDocBaseQuery
            .Where(document => document.OcrLastTriedUtc.HasValue)
            .Select(document => document.OcrLastTriedUtc)
            .OrderByDescending(value => value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        // END SECTION

        // SECTION: Combine results
        var ocrSnapshot = new SearchHealthOcrSnapshot
        {
            Succeeded = docRepoSucceeded + projectDocsSucceeded,
            Pending = docRepoPending + projectDocsPending,
            Failed = docRepoFailed + projectDocsFailed,
            DocRepoSucceeded = docRepoSucceeded,
            DocRepoPending = docRepoPending,
            DocRepoFailed = docRepoFailed,
            ProjectDocumentsSucceeded = projectDocsSucceeded,
            ProjectDocumentsPending = projectDocsPending,
            ProjectDocumentsFailed = projectDocsFailed
        };

        var totalSearchable = docRepoSearchable + projectDocsSearchable + projectReportsSearchable;

        var trendValues = BuildTrend(trendStartDate, docRepoTrend, projectDocsTrend);
        var oldestPending = MinNonNull(docRepoPendingOldest, projectDocPendingOldest);
        var lastAttempt = MaxNonNull(docRepoLastAttempt, projectDocLastAttempt);
        var searchIndexHealth = await _searchIndexStore.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        var searchQueryHealth = await ReadPercentileContMetricsAsync("V2", cancellationToken).ConfigureAwait(false);
        var engineQueryHealth = await ReadPercentileContMetricsAsync("V2-Engine", cancellationToken).ConfigureAwait(false);
        var suggestionHealth = await ReadPercentileContMetricsAsync("V2-Suggest", cancellationToken).ConfigureAwait(false);
        // END SECTION

        return new SearchHealthVm
        {
            TotalSearchable = totalSearchable,
            DocRepoSearchable = docRepoSearchable,
            ProjectDocumentsSearchable = projectDocsSearchable,
            ProjectReportsSearchable = projectReportsSearchable,
            IncludeProjectReports = projectReportsSearchable > 0,
            SearchIndex = new SearchIndexHealthSnapshot
            {
                IsReady = searchIndexHealth.IsReady,
                ActiveGeneration = searchIndexHealth.ActiveGeneration,
                IndexVersion = searchIndexHealth.IndexVersion,
                EntryCount = searchIndexHealth.EntryCount,
                PendingItems = searchIndexHealth.PendingItems,
                FailedItems = searchIndexHealth.FailedItems,
                LastFullRebuildUtc = searchIndexHealth.LastFullRebuildUtc,
                LastReconciliationUtc = searchIndexHealth.LastReconciliationUtc,
                QueryCount24Hours = searchQueryHealth.QueryCount,
                P50LatencyMs = searchQueryHealth.P50LatencyMs,
                P95LatencyMs = searchQueryHealth.P95LatencyMs,
                EngineP50LatencyMs = engineQueryHealth.P50LatencyMs,
                EngineP95LatencyMs = engineQueryHealth.P95LatencyMs,
                SuggestionP50LatencyMs = suggestionHealth.P50LatencyMs,
                SuggestionP95LatencyMs = suggestionHealth.P95LatencyMs,
                ZeroResultRate = searchQueryHealth.ZeroResultRate,
                LastError = searchIndexHealth.LastError
            },
            Ocr = ocrSnapshot,
            OcrCompletionsTrend = trendValues,
            OldestPendingLabel = FormatPendingAge(oldestPending),
            WorkerActive = IsWorkerActive(lastAttempt)
        };
    }


    private async Task<SearchQueryHealthMetrics> ReadPercentileContMetricsAsync(string engine, CancellationToken cancellationToken)
    {
        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)::bigint,
                       (percentile_cont(0.50) WITHIN GROUP (ORDER BY "LatencyMs"))::double precision AS p50,
                       (percentile_cont(0.95) WITHIN GROUP (ORDER BY "LatencyMs"))::double precision AS p95,
                       AVG(CASE WHEN "ZeroResult" THEN 1.0 ELSE 0.0 END)::double precision AS zero_result_rate
                FROM "SearchQueryLogs"
                WHERE "Engine" = @engine
                  AND "SearchedAtUtc" >= NOW() - INTERVAL '24 hours';
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@engine";
            parameter.Value = engine;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return SearchQueryHealthMetrics.Empty;

            var count = reader.GetInt64(0);
            return new SearchQueryHealthMetrics(
                count,
                reader.IsDBNull(1) ? null : Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture),
                reader.IsDBNull(2) ? null : Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? null : Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture));
        }
        catch (DbException)
        {
            return SearchQueryHealthMetrics.Empty;
        }
        finally
        {
            try { await _db.Database.CloseConnectionAsync().ConfigureAwait(false); } catch { }
        }
    }

    private sealed record SearchQueryHealthMetrics(long QueryCount, double? P50LatencyMs, double? P95LatencyMs, double? ZeroResultRate)
    {
        public static SearchQueryHealthMetrics Empty { get; } = new(0, null, null, null);
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
