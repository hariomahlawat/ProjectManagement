using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Admin.MediaIntelligence;

[Authorize(Roles = "Admin,HoD")]
public sealed class ClassificationsModel : PageModel
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaClassificationOverrideService _overrides;
    private readonly IFaceEligibilityPolicy _eligibility;
    private readonly MediaLibraryOptions _options;

    public ClassificationsModel(
        MediaLibraryDbContext db,
        IMediaClassificationOverrideService overrides,
        IFaceEligibilityPolicy eligibility,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public MediaClassification? Classification { get; set; }
    [BindProperty(SupportsGet = true)] public string Mode { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;

    public int PageSize { get; } = 24;
    public int Total { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public int StaleCount { get; private set; }
    public IReadOnlyList<Row> Rows { get; private set; } = Array.Empty<Row>();

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? WarningMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        P = Math.Max(1, P);
        var baseQuery = ReviewableAssets().AsNoTracking();
        StaleCount = await baseQuery.CountAsync(
            asset => !asset.ClassificationIsManual
                     && asset.ClassifierVersion != MediaClassifier.ClassifierVersion,
            cancellationToken);

        var query = baseQuery;
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var queryText = Q.Trim().ToLower();
            query = query.Where(asset => asset.Title.ToLower().Contains(queryText)
                                         || asset.OriginalFileName.ToLower().Contains(queryText)
                                         || asset.ContextTitle.ToLower().Contains(queryText));
        }
        if (Classification.HasValue)
        {
            query = query.Where(asset => asset.Classification == Classification.Value);
        }

        query = Mode switch
        {
            "manual" => query.Where(asset => asset.ClassificationIsManual),
            "low" => query.Where(asset => !asset.ClassificationIsManual
                                           && asset.AnalysisStatus == MediaProcessingStatus.Ready
                                           && asset.ClassificationConfidence != null
                                           && asset.ClassificationConfidence < _options.Classification.MinimumConfidence),
            "unknown" => query.Where(asset => !asset.ClassificationIsManual
                                               && asset.Classification == MediaClassification.Unknown),
            "nonphoto" => query.Where(asset => asset.ClassificationIsManual
                ? asset.Classification != MediaClassification.Photograph
                : asset.AnalysisStatus == MediaProcessingStatus.Ready
                  && asset.ClassifierVersion == MediaClassifier.ClassifierVersion
                  && asset.Classification != MediaClassification.Unknown
                  && asset.Classification != MediaClassification.Photograph),
            "stale" => query.Where(asset => !asset.ClassificationIsManual
                                             && asset.ClassifierVersion != MediaClassifier.ClassifierVersion),
            "eligible" => query.Where(_eligibility.BuildEligiblePredicate()),
            _ => query
        };

        Total = await query.CountAsync(cancellationToken);
        if (P > TotalPages) P = TotalPages;
        var assets = await query
            .OrderByDescending(asset => asset.MediaDateUtc)
            .ThenBy(asset => asset.Id)
            .Skip((P - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        Rows = assets.Select(asset =>
        {
            var decision = _eligibility.Evaluate(asset);
            return Row.Create(asset, decision);
        }).ToList();
    }

    public async Task<IActionResult> OnPostSetAsync(
        long assetId,
        Guid expectedConcurrencyToken,
        MediaClassification classification,
        string? reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await _overrides.SetManualAsync(
                assetId,
                expectedConcurrencyToken,
                classification,
                CurrentUserId(),
                reason,
                cancellationToken);
            StatusMessage = "Classification updated and audited.";
        }
        catch (MediaClassificationConcurrencyException exception)
        {
            WarningMessage = exception.Message;
        }
        catch (ArgumentException exception)
        {
            WarningMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToCurrent();
    }

    public async Task<IActionResult> OnPostSetBatchAsync(
        long[] assetIds,
        Guid[] expectedConcurrencyTokens,
        MediaClassification classification,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (assetIds is null || assetIds.Length == 0)
        {
            WarningMessage = "Select at least one image before applying a bulk classification.";
            return RedirectToCurrent();
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            WarningMessage = "A reason is required for bulk classification.";
            return RedirectToCurrent();
        }
        if (expectedConcurrencyTokens is null || expectedConcurrencyTokens.Length != assetIds.Length)
        {
            ErrorMessage = "The selected review state is incomplete. Reload the page and try again.";
            return RedirectToCurrent();
        }

        try
        {
            var items = assetIds
                .Select((id, index) => new ClassificationBatchItem(id, expectedConcurrencyTokens[index]))
                .ToArray();
            var updated = await _overrides.SetManualBatchAsync(
                items,
                classification,
                CurrentUserId(),
                reason,
                cancellationToken);
            StatusMessage = updated == 0
                ? "No eligible images were updated."
                : $"Updated and audited {updated} image classification(s).";
        }
        catch (MediaClassificationConcurrencyException exception)
        {
            WarningMessage = exception.Message;
        }
        catch (ArgumentException exception)
        {
            WarningMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToCurrent();
    }

    public async Task<IActionResult> OnPostResetAsync(
        long assetId,
        Guid expectedConcurrencyToken,
        string? reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await _overrides.ResetToAutomaticAsync(
                assetId,
                expectedConcurrencyToken,
                CurrentUserId(),
                reason,
                cancellationToken);
            StatusMessage = "Automatic classification has been queued.";
        }
        catch (MediaClassificationConcurrencyException exception)
        {
            WarningMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToCurrent();
    }

    public async Task<IActionResult> OnPostReclassifyStaleAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var staleAssets = await ReviewableAssets()
            .Where(asset => !asset.ClassificationIsManual
                            && asset.ClassifierVersion != MediaClassifier.ClassifierVersion)
            .ToListAsync(cancellationToken);
        if (staleAssets.Count == 0)
        {
            StatusMessage = "All automatic classifications already use the current classifier version.";
            return RedirectToCurrent();
        }

        var staleIds = staleAssets.Select(asset => asset.Id).ToArray();
        var activeJobIds = await _db.ProcessingJobs
            .Where(job => staleIds.Contains(job.MediaAssetId)
                          && job.Status == MediaProcessingJobStatus.Running
                          && job.LockExpiresAtUtc != null
                          && job.LockExpiresAtUtc > now
                          && (job.JobType == MediaProcessingJobType.AnalyseAsset
                              || job.JobType == MediaProcessingJobType.ReclassifyAsset
                              || job.JobType == MediaProcessingJobType.ClassifyMedia
                              || job.JobType == MediaProcessingJobType.RebuildIntelligence))
            .Select(job => job.MediaAssetId)
            .ToListAsync(cancellationToken);
        var activeSet = activeJobIds.ToHashSet();
        var queueable = staleAssets.Where(asset => !activeSet.Contains(asset.Id)).ToArray();
        if (queueable.Length == 0)
        {
            WarningMessage = "Stale images are currently being processed. Try again after the active jobs complete.";
            return RedirectToCurrent();
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var queueableIds = queueable.Select(asset => asset.Id).ToArray();
        var jobs = await _db.ProcessingJobs
            .Where(job => queueableIds.Contains(job.MediaAssetId)
                          && job.JobType == MediaProcessingJobType.ClassifyMedia)
            .ToDictionaryAsync(job => job.MediaAssetId, cancellationToken);
        var userId = CurrentUserId();

        foreach (var asset in queueable)
        {
            var previous = asset.Classification;
            var previousManual = asset.ClassificationIsManual;
            var previousStatus = asset.ClassificationDecisionStatus;
            var previousPrediction = asset.PredictedClassification;
            var previousPredictionScore = asset.PredictedClassificationScore;

            asset.AnalysisStatus = MediaProcessingStatus.Pending;
            asset.PredictedClassification = MediaClassification.Unknown;
            asset.PredictedClassificationScore = 0m;
            asset.Classification = MediaClassification.Unknown;
            asset.ClassificationConfidence = null;
            asset.ClassificationDecisionStatus = MediaClassificationDecisionStatus.NotProcessed;
            asset.ClassificationDecisionReasonCode = "CLASSIFIER_VERSION_CHANGED";
            asset.AutomaticClassificationSignalsJson = null;
            asset.AutomaticClassificationScoresJson = null;
            asset.AutomaticClassificationMetricsJson = null;
            asset.ClassificationConcurrencyToken = Guid.NewGuid();
            asset.AnalysisVersion = null;
            asset.ClassifierVersion = null;
            asset.AnalysisSignalsJson = null;
            asset.AnalysedAtUtc = null;
            asset.ClassifiedAtUtc = null;

            _db.ClassificationAudits.Add(new MediaClassificationAudit
            {
                MediaAssetId = asset.Id,
                PreviousClassification = previous,
                NewClassification = MediaClassification.Unknown,
                PreviousWasManual = previousManual,
                NewIsManual = false,
                AutomaticPredictedClassification = previousPrediction,
                AutomaticPredictedScore = previousPredictionScore,
                PreviousDecisionStatus = previousStatus,
                NewDecisionStatus = MediaClassificationDecisionStatus.NotProcessed,
                CorrelationId = $"reclassify:{Guid.NewGuid():N}",
                ChangedByUserId = userId,
                Reason = $"Queued for classifier upgrade to {MediaClassifier.ClassifierVersion}.",
                ChangedAtUtc = now
            });

            if (!jobs.TryGetValue(asset.Id, out var job))
            {
                _db.ProcessingJobs.Add(new MediaProcessingJob
                {
                    MediaAssetId = asset.Id,
                    JobType = MediaProcessingJobType.ClassifyMedia,
                    Status = MediaProcessingJobStatus.Pending,
                    AvailableAfterUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    MaxAttempts = 5
                });
                continue;
            }

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

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var skipped = staleAssets.Count - queueable.Length;
        StatusMessage = skipped == 0
            ? $"Queued {queueable.Length} stale automatic classification(s) for {MediaClassifier.ClassifierVersion}."
            : $"Queued {queueable.Length} stale classification(s); {skipped} active item(s) were left unchanged.";
        return RedirectToPage(new { Q, Classification, Mode = "stale", P = 1 });
    }

    private IQueryable<MediaAsset> ReviewableAssets()
        => _db.Assets.Where(asset => asset.IsAvailable
                                     && !asset.IsDeleted
                                     && !asset.IsArchived
                                     && asset.Kind == MediaAssetKind.Photo);

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? User.Identity?.Name
           ?? "unknown";

    private RedirectToPageResult RedirectToCurrent()
        => RedirectToPage(new { Q, Classification, Mode, P });

    public sealed record Row(
        long Id,
        string Title,
        string ContextTitle,
        string OriginalFileName,
        MediaClassification PredictedClassification,
        decimal PredictedScore,
        MediaClassification RunnerUpClassification,
        double RunnerUpScore,
        double ScoreMargin,
        MediaClassification Classification,
        double? Confidence,
        bool IsManual,
        string? ClassifierVersion,
        IReadOnlyList<string> Signals,
        bool FaceEligible,
        string FaceEligibilityCode,
        string FaceEligibilityReason,
        MediaProcessingStatus AnalysisStatus,
        DateTimeOffset MediaDateUtc,
        MediaClassificationDecisionStatus DecisionStatus,
        string? DecisionReasonCode,
        Guid ConcurrencyToken)
    {
        public string ReviewStatusLabel => DecisionStatus switch
        {
            MediaClassificationDecisionStatus.AutomaticallyAccepted => "Automatically accepted",
            MediaClassificationDecisionStatus.NeedsReview => "Needs review",
            MediaClassificationDecisionStatus.ManuallyConfirmed => "Manually confirmed",
            MediaClassificationDecisionStatus.ManuallyCorrected => "Manually corrected",
            MediaClassificationDecisionStatus.ProcessingFailed => "Processing failed",
            MediaClassificationDecisionStatus.NotApplicable => "Not applicable",
            _ => "Not processed"
        };

        public string ReviewStatusCss => DecisionStatus switch
        {
            MediaClassificationDecisionStatus.AutomaticallyAccepted => "bg-success",
            MediaClassificationDecisionStatus.NeedsReview => "bg-warning text-dark",
            MediaClassificationDecisionStatus.ManuallyConfirmed => "bg-primary",
            MediaClassificationDecisionStatus.ManuallyCorrected => "bg-info text-dark",
            MediaClassificationDecisionStatus.ProcessingFailed => "bg-danger",
            _ => "bg-secondary"
        };

        public string DecisionReasonLabel => DecisionReasonCode switch
        {
            "AUTO_ACCEPTED_HIGH_CONFIDENCE" => "High confidence with clear score separation",
            "BELOW_CATEGORY_THRESHOLD" => "Confidence below the acceptance threshold",
            "AMBIGUOUS_SCORE_MARGIN" => "Winner too close to the runner-up",
            "CONFLICTING_NON_PHOTO_EVIDENCE" => "Conflicting non-photograph evidence",
            "CATEGORY_DISABLED" => "This detector category is disabled",
            "NO_RELIABLE_PREDICTION" => "No reliable prediction",
            "CLASSIFIER_PROCESSING_FAILED" => "Classifier processing failed",
            "MANUAL_CONFIRMATION" => "Human confirmed the prediction",
            "MANUAL_CORRECTION" => "Human corrected the prediction",
            _ => DecisionReasonCode ?? "No decision reason recorded"
        };

        public static Row Create(MediaAsset asset, FaceEligibilityDecision eligibility)
        {
            var scores = ParseScores(asset.AutomaticClassificationScoresJson);
            var ordered = scores
                .Where(pair => pair.Key != MediaClassification.Unknown)
                .OrderByDescending(pair => pair.Value)
                .ToArray();
            var runnerUp = ordered
                .FirstOrDefault(pair => pair.Key != asset.PredictedClassification);
            var predictedScore = (double)asset.PredictedClassificationScore;
            return new Row(
                asset.Id,
                asset.Title,
                asset.ContextTitle,
                asset.OriginalFileName,
                asset.PredictedClassification,
                asset.PredictedClassificationScore,
                runnerUp.Key,
                runnerUp.Value,
                Math.Max(0d, predictedScore - runnerUp.Value),
                asset.Classification,
                asset.ClassificationConfidence,
                asset.ClassificationIsManual,
                asset.ClassifierVersion,
                ParseSignals(asset.AnalysisSignalsJson),
                eligibility.IsEligible,
                eligibility.Code,
                eligibility.Reason,
                asset.AnalysisStatus,
                asset.MediaDateUtc,
                asset.ClassificationDecisionStatus,
                asset.ClassificationDecisionReasonCode,
                asset.ClassificationConcurrencyToken);
        }

        private static IReadOnlyList<string> ParseSignals(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
            try
            {
                return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        private static IReadOnlyDictionary<MediaClassification, double> ParseScores(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<MediaClassification, double>();
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<MediaClassification, double>>(json)
                       ?? new Dictionary<MediaClassification, double>();
            }
            catch (JsonException)
            {
                return new Dictionary<MediaClassification, double>();
            }
        }
    }
}
