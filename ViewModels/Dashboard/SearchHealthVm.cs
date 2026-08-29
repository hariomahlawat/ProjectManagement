using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.ViewModels.Dashboard;

// SECTION: Search and OCR health view model contract
public sealed class SearchHealthVm
{
    // SECTION: Searchable corpus metrics
    public long TotalSearchable { get; init; }
    public long ProjectDocumentsSearchable { get; init; }
    public long DocRepoSearchable { get; init; }
    public long ProjectReportsSearchable { get; init; }
    public bool IncludeProjectReports { get; init; }
    // END SECTION

    // SECTION: Search V2 index health
    public SearchIndexHealthSnapshot SearchIndex { get; init; } = new();
    // END SECTION

    // SECTION: OCR snapshot metrics
    public SearchHealthOcrSnapshot Ocr { get; init; } = new();
    // END SECTION

    // SECTION: Trend and queue metrics
    public IReadOnlyList<int> OcrCompletionsTrend { get; init; } = Array.Empty<int>();
    public string? OldestPendingLabel { get; init; }
    public bool WorkerActive { get; init; }
    // END SECTION

    // SECTION: Convenience flags
    public bool HasSearchableCorpus => TotalSearchable > 0 || SearchIndex.EntryCount > 0;
    public bool HasOcrActivity => OcrCompletionsTrend.Count > 0 && OcrCompletionsTrend.Any(value => value > 0);
    // END SECTION
}

// SECTION: OCR snapshot breakdown
public sealed class SearchHealthOcrSnapshot
{
    public long Succeeded { get; init; }
    public long Pending { get; init; }
    public long Failed { get; init; }

    public long ProjectDocumentsSucceeded { get; init; }
    public long ProjectDocumentsPending { get; init; }
    public long ProjectDocumentsFailed { get; init; }

    public long DocRepoSucceeded { get; init; }
    public long DocRepoPending { get; init; }
    public long DocRepoFailed { get; init; }
}


// SECTION: Search V2 index snapshot
public sealed class SearchIndexHealthSnapshot
{
    public bool IsReady { get; init; }
    public long ActiveGeneration { get; init; }
    public int IndexVersion { get; init; }
    public long EntryCount { get; init; }
    public long PendingItems { get; init; }
    public long FailedItems { get; init; }
    public DateTimeOffset? LastFullRebuildUtc { get; init; }
    public DateTimeOffset? LastReconciliationUtc { get; init; }
    public long QueryCount24Hours { get; init; }
    public double? P50LatencyMs { get; init; }
    public double? P95LatencyMs { get; init; }
    public double? EngineP50LatencyMs { get; init; }
    public double? EngineP95LatencyMs { get; init; }
    public double? SuggestionP50LatencyMs { get; init; }
    public double? SuggestionP95LatencyMs { get; init; }
    public double? ZeroResultRate { get; init; }
    public string? LastError { get; init; }
}
