using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.ViewModels.Dashboard;

// SECTION: Search and OCR health view model contract
public sealed class SearchHealthVm
{
    // SECTION: Searchable corpus metrics
    public long TotalSearchable { get; set; }
    public long ProjectDocumentsSearchable { get; set; }
    public long DocRepoSearchable { get; set; }
    public long ProjectReportsSearchable { get; set; }
    public bool IncludeProjectReports { get; set; }
    // END SECTION

    // SECTION: OCR snapshot metrics
    public SearchHealthOcrSnapshot Ocr { get; set; } = new();
    // END SECTION

    // SECTION: Trend and queue metrics
    public IReadOnlyList<int> OcrCompletionsTrend { get; set; } = new List<int>();
    public string? OldestPendingLabel { get; set; }
    public bool WorkerActive { get; set; }
    // END SECTION

    // SECTION: Convenience flags
    public bool HasSearchableCorpus => TotalSearchable > 0;
    public bool HasOcrActivity => OcrCompletionsTrend.Count > 0 && OcrCompletionsTrend.Any(value => value > 0);
    // END SECTION
}

// SECTION: OCR snapshot breakdown
public sealed class SearchHealthOcrSnapshot
{
    public long Succeeded { get; set; }
    public long Pending { get; set; }
    public long Failed { get; set; }

    public long ProjectDocumentsSucceeded { get; set; }
    public long ProjectDocumentsPending { get; set; }
    public long ProjectDocumentsFailed { get; set; }

    public long DocRepoSucceeded { get; set; }
    public long DocRepoPending { get; set; }
    public long DocRepoFailed { get; set; }
}
