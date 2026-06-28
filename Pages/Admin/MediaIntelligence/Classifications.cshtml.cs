using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Features.MediaLibrary.Options;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Pages.Admin.MediaIntelligence;

[Authorize(Roles = "Admin,HoD")]
public sealed class ClassificationsModel : PageModel
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaClassificationOverrideService _overrides;
    private readonly IFaceEligibilityPolicy _eligibility;
    private readonly MediaLibraryOptions _options;
    public ClassificationsModel(MediaLibraryDbContext db, IMediaClassificationOverrideService overrides, IFaceEligibilityPolicy eligibility, IOptions<MediaLibraryOptions> options)
    {
        _db = db;
        _overrides = overrides;
        _eligibility = eligibility;
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public MediaClassification? Classification { get; set; }
    [BindProperty(SupportsGet = true)] public string Mode { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    public int PageSize { get; } = 24;
    public int Total { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public IReadOnlyList<Row> Rows { get; private set; } = Array.Empty<Row>();
    [TempData] public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        P = Math.Max(1, P);
        var query = _db.Assets.AsNoTracking().Where(x => x.IsAvailable && !x.IsDeleted && !x.IsArchived && x.Kind == MediaAssetKind.Photo);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim().ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(q) || x.OriginalFileName.ToLower().Contains(q) || x.ContextTitle.ToLower().Contains(q));
        }
        if (Classification.HasValue) query = query.Where(x => x.Classification == Classification.Value);
        query = Mode switch
        {
            "manual" => query.Where(x => x.ClassificationIsManual),
            "low" => query.Where(x => !x.ClassificationIsManual && (x.ClassificationConfidence == null || x.ClassificationConfidence < _options.People.MinimumClassificationConfidence)),
            "unknown" => query.Where(x => !x.ClassificationIsManual && x.Classification == MediaClassification.Unknown),
            "nonphoto" => query.Where(x => x.ClassificationIsManual
                                            ? x.Classification != MediaClassification.Photograph
                                            : x.AnalysisStatus == MediaProcessingStatus.Ready
                                              && x.ClassifierVersion == MediaClassifier.ClassifierVersion
                                              && x.Classification != MediaClassification.Unknown
                                              && x.Classification != MediaClassification.Photograph),
            "stale" => query.Where(x => !x.ClassificationIsManual && x.ClassifierVersion != MediaClassifier.ClassifierVersion),
            "eligible" => query.Where(_eligibility.BuildEligiblePredicate()),
            _ => query
        };
        Total = await query.CountAsync(cancellationToken);
        if (P > TotalPages) P = TotalPages;
        var assets = await query.OrderByDescending(x => x.MediaDateUtc).ThenBy(x => x.Id)
            .Skip((P - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);
        Rows = assets.Select(x =>
        {
            var decision = _eligibility.Evaluate(x);
            return new Row(
                x.Id,
                x.Title,
                x.ContextTitle,
                x.OriginalFileName,
                x.Classification,
                x.ClassificationConfidence,
                x.ClassificationIsManual,
                x.ClassifierVersion,
                x.AnalysisSignalsJson,
                decision.IsEligible,
                decision.Code,
                decision.Reason,
                x.AnalysisStatus,
                x.MediaDateUtc);
        }).ToList();
    }

    public async Task<IActionResult> OnPostSetAsync(long assetId, MediaClassification classification, string? reason, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "unknown";
        await _overrides.SetManualAsync(assetId, classification, userId, reason, cancellationToken);
        StatusMessage = "Classification updated and audited.";
        return RedirectToPage(new { Q, Classification, Mode, P });
    }

    public async Task<IActionResult> OnPostSetBatchAsync(
        long[] assetIds,
        MediaClassification classification,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (assetIds is null || assetIds.Length == 0)
        {
            StatusMessage = "Select at least one image before applying a bulk classification.";
            return RedirectToPage(new { Q, Classification, Mode, P });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "unknown";
        var updated = await _overrides.SetManualBatchAsync(assetIds, classification, userId, reason, cancellationToken);
        StatusMessage = updated == 0
            ? "No eligible images were updated."
            : $"Updated and audited {updated} image classification(s).";
        return RedirectToPage(new { Q, Classification, Mode, P });
    }

    public async Task<IActionResult> OnPostResetAsync(long assetId, string? reason, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "unknown";
        await _overrides.ResetToAutomaticAsync(assetId, userId, reason, cancellationToken);
        StatusMessage = "Automatic classification has been queued.";
        return RedirectToPage(new { Q, Classification, Mode, P });
    }

    public async Task<IActionResult> OnPostReclassifyStaleAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var staleIds = await _db.Assets
            .Where(x => x.IsAvailable
                        && !x.IsDeleted
                        && !x.IsArchived
                        && x.Kind == MediaAssetKind.Photo
                        && !x.ClassificationIsManual
                        && x.ClassifierVersion != MediaClassifier.ClassifierVersion)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (staleIds.Count == 0)
        {
            StatusMessage = "All automatic classifications already use the current classifier version.";
            return RedirectToPage(new { Q, Classification, Mode, P });
        }

        await _db.Assets
            .Where(x => staleIds.Contains(x.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.AnalysisStatus, MediaProcessingStatus.Pending)
                .SetProperty(x => x.Classification, MediaClassification.Unknown)
                .SetProperty(x => x.ClassificationConfidence, (double?)null)
                .SetProperty(x => x.AnalysisVersion, (string?)null)
                .SetProperty(x => x.ClassifierVersion, (string?)null)
                .SetProperty(x => x.AnalysisSignalsJson, (string?)null)
                .SetProperty(x => x.AnalysedAtUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.ClassifiedAtUtc, (DateTimeOffset?)null), cancellationToken);

        var jobs = await _db.ProcessingJobs
            .Where(x => staleIds.Contains(x.MediaAssetId)
                        && x.JobType == MediaProcessingJobType.AnalyseAsset)
            .ToDictionaryAsync(x => x.MediaAssetId, cancellationToken);

        foreach (var assetId in staleIds)
        {
            if (!jobs.TryGetValue(assetId, out var job))
            {
                _db.ProcessingJobs.Add(new MediaProcessingJob
                {
                    MediaAssetId = assetId,
                    JobType = MediaProcessingJobType.AnalyseAsset,
                    Status = MediaProcessingJobStatus.Pending,
                    AvailableAfterUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    MaxAttempts = 5
                });
                continue;
            }

            if (job.Status != MediaProcessingJobStatus.Running)
            {
                job.Status = MediaProcessingJobStatus.Pending;
                job.AttemptCount = 0;
                job.AvailableAfterUtc = now;
                job.StartedAtUtc = null;
                job.CompletedAtUtc = null;
                job.LockedBy = null;
                job.LockExpiresAtUtc = null;
                job.FailureCode = null;
                job.FailureMessage = null;
                job.UpdatedAtUtc = now;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        StatusMessage = $"Queued {staleIds.Count} stale automatic classification(s) for the current classifier.";
        return RedirectToPage(new { Q, Classification, Mode = "stale", P = 1 });
    }

    public sealed record Row(long Id, string Title, string ContextTitle, string OriginalFileName, MediaClassification Classification, double? Confidence, bool IsManual, string? ClassifierVersion, string? SignalsJson, bool FaceEligible, string FaceEligibilityCode, string FaceEligibilityReason, MediaProcessingStatus AnalysisStatus, DateTimeOffset MediaDateUtc)
    {
        public string ReviewStatusLabel => FaceEligible
            ? "Face eligible"
            : FaceEligibilityCode switch
            {
                "classification-pending" => "Pending",
                "classifier-stale" => "Stale",
                "confidence-low" or "confidence-missing" => "Low confidence",
                "manual-exclusion" => "Confirmed non-photo",
                "not-photograph" when Classification != MediaClassification.Unknown => Classification.ToString(),
                _ => "Needs review"
            };

        public string ReviewStatusCss => FaceEligible
            ? "text-bg-success"
            : FaceEligibilityCode switch
            {
                "classification-pending" => "text-bg-info",
                "classifier-stale" => "text-bg-warning",
                "confidence-low" or "confidence-missing" => "text-bg-warning",
                "manual-exclusion" => "text-bg-secondary",
                "not-photograph" when Classification != MediaClassification.Unknown => "text-bg-secondary",
                _ => "text-bg-warning"
            };

        public IReadOnlyList<string> Signals
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SignalsJson)) return Array.Empty<string>();
                try { return JsonSerializer.Deserialize<string[]>(SignalsJson) ?? Array.Empty<string>(); } catch { return Array.Empty<string>(); }
            }
        }
    }
}
