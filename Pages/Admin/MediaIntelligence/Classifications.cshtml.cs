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
    private static readonly string[] SupportedModes =
    {
        "review", "admission", "risk", "automatic", "manual", "nonphoto",
        "failures", "stale", "eligible", "all"
    };

    private static readonly string[] SafetyReviewReasonCodes =
    {
        "EXPLICIT_NON_PHOTO_FILENAME_VETO",
        "DOCUMENT_STRUCTURE_VETO",
        "DIAGRAM_STRUCTURE_VETO",
        "GRAPHIC_STRUCTURE_VETO",
        "PHOTO_BASELINE_INSUFFICIENT",
        "CONFLICTING_BASE_NON_PHOTO_EVIDENCE",
        "FACE_EVIDENCE_NOT_NATURAL_PHOTO",
        "CONFLICTING_NON_PHOTO_EVIDENCE"
    };

    private readonly MediaLibraryDbContext _db;
    private readonly IMediaClassificationOverrideService _overrides;
    private readonly IFaceEligibilityPolicy _eligibility;
    private readonly IMediaContentChangeInvalidationService _contentInvalidation;
    private readonly MediaLibraryOptions _options;

    public ClassificationsModel(
        MediaLibraryDbContext db,
        IMediaClassificationOverrideService overrides,
        IFaceEligibilityPolicy eligibility,
        IMediaContentChangeInvalidationService contentInvalidation,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
        _contentInvalidation = contentInvalidation ?? throw new ArgumentNullException(nameof(contentInvalidation));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public MediaClassification? FilterClassification { get; set; }
    [BindProperty(SupportsGet = true)] public string Mode { get; set; } = "review";
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;

    public int PageSize { get; } = 24;
    public int Total { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public int StaleCount => ReviewCounts.Stale;
    public ReviewSetCounts ReviewCounts { get; private set; } = ReviewSetCounts.Empty;
    public IReadOnlyList<Row> Rows { get; private set; } = Array.Empty<Row>();

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? WarningMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Mode = NormalizeMode(Mode);
        P = Math.Max(1, P);

        var baseQuery = ReviewableAssets().AsNoTracking();
        ReviewCounts = await BuildReviewSetCountsAsync(baseQuery, cancellationToken);

        var query = baseQuery;
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var queryText = Q.Trim().ToLower();
            query = query.Where(asset => asset.Title.ToLower().Contains(queryText)
                                         || asset.OriginalFileName.ToLower().Contains(queryText)
                                         || asset.ContextTitle.ToLower().Contains(queryText));
        }

        if (FilterClassification.HasValue)
        {
            query = query.Where(asset => asset.Classification == FilterClassification.Value);
        }

        query = ApplyMode(query, Mode);

        Total = await query.CountAsync(cancellationToken);
        if (P > TotalPages) P = TotalPages;

        var assets = await query
            .OrderByDescending(asset => asset.ClassificationDecisionStatus == MediaClassificationDecisionStatus.NeedsReview)
            .ThenByDescending(asset => asset.ClassificationDecisionReasonCode != null
                                       && SafetyReviewReasonCodes.Contains(asset.ClassificationDecisionReasonCode))
            .ThenByDescending(asset => asset.MediaDateUtc)
            .ThenBy(asset => asset.Id)
            .Skip((P - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        Rows = assets.Select(asset => Row.Create(asset, _eligibility.Evaluate(asset))).ToList();
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
            StatusMessage = classification == MediaClassification.Photograph
                ? "Natural-photograph classification saved. Face processing remains blocked until separately approved."
                : "Classification updated and audited.";
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
                : classification == MediaClassification.Photograph
                    ? $"Classified and audited {updated} image(s) as natural photographs. Face processing remains blocked pending individual approval."
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

    public async Task<IActionResult> OnPostApproveFaceAsync(
        long assetId,
        Guid expectedConcurrencyToken,
        string? reason,
        bool acknowledgeBiometricProcessing,
        CancellationToken cancellationToken)
    {
        if (!acknowledgeBiometricProcessing)
        {
            WarningMessage = "Confirm that this is a real natural photograph and acknowledge that approval permits biometric face processing.";
            return RedirectToCurrent();
        }

        try
        {
            await _overrides.ApproveFaceProcessingAsync(
                assetId,
                expectedConcurrencyToken,
                CurrentUserId(),
                reason ?? string.Empty,
                cancellationToken);
            StatusMessage = "Face-processing admission approved and audited for this exact image content.";
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

    public async Task<IActionResult> OnPostRevokeFaceAsync(
        long assetId,
        Guid expectedConcurrencyToken,
        string? reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await _overrides.RevokeFaceProcessingAsync(
                assetId,
                expectedConcurrencyToken,
                CurrentUserId(),
                reason ?? string.Empty,
                cancellationToken);
            StatusMessage = "Face-processing admission revoked. Existing face intelligence was retired.";
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

        // A classifier upgrade changes the admission policy that authorised any prior face
        // processing. Retire derived face intelligence before resetting the classification so
        // a v6 false positive can never survive the v7 safety-policy transition.
        await _contentInvalidation.RetireFaceIntelligenceAsync(
            queueableIds,
            "ClassifierUpgrade",
            userId,
            $"Face intelligence retired before reclassification with {MediaClassifier.ClassifierVersion}.",
            now,
            cancellationToken);
        await _db.ProcessingJobs
            .Where(job => queueableIds.Contains(job.MediaAssetId)
                          && (job.JobType == MediaProcessingJobType.DetectFaces
                              || job.JobType == MediaProcessingJobType.GenerateFaceEmbeddings
                              || job.JobType == MediaProcessingJobType.AssignFaceCluster)
                          && job.Status != MediaProcessingJobStatus.Running
                          && job.Status != MediaProcessingJobStatus.Completed)
            .ExecuteDeleteAsync(cancellationToken);

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
            asset.FaceAnalysisStatus = MediaProcessingStatus.NotRequested;
            asset.FaceAnalysisVersion = null;
            asset.FaceAnalysedAtUtc = null;
            asset.FaceProcessingFailureReason = null;
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
        return RedirectToPage(new { Q, FilterClassification, Mode = "stale", P = 1 });
    }

    private IQueryable<MediaAsset> ApplyMode(IQueryable<MediaAsset> query, string mode)
        => mode switch
        {
            "review" => query.Where(asset => asset.ClassificationDecisionStatus
                                             == MediaClassificationDecisionStatus.NeedsReview
                                             || (!asset.ClassificationIsManual
                                                 && asset.Classification == MediaClassification.Unknown)),
            "admission" => query.Where(asset => asset.ClassificationIsManual
                                                && asset.Classification == MediaClassification.Photograph
                                                && asset.ClassificationDecisionStatus
                                                   != MediaClassificationDecisionStatus.ManualFaceProcessingApproved),
            "risk" => query.Where(asset => asset.ClassificationDecisionReasonCode != null
                                           && SafetyReviewReasonCodes.Contains(asset.ClassificationDecisionReasonCode)),
            "automatic" => query.Where(asset => !asset.ClassificationIsManual
                                                && asset.ClassificationDecisionStatus
                                                   == MediaClassificationDecisionStatus.AutomaticallyAccepted),
            "manual" => query.Where(asset => asset.ClassificationIsManual),
            "nonphoto" => query.Where(asset => asset.ClassificationIsManual
                ? asset.Classification != MediaClassification.Photograph
                : asset.AnalysisStatus == MediaProcessingStatus.Ready
                  && asset.ClassifierVersion == MediaClassifier.ClassifierVersion
                  && asset.Classification != MediaClassification.Unknown
                  && asset.Classification != MediaClassification.Photograph),
            "failures" => query.Where(asset => asset.AnalysisStatus == MediaProcessingStatus.Failed
                                               || asset.ClassificationDecisionStatus
                                                  == MediaClassificationDecisionStatus.ProcessingFailed),
            "stale" => query.Where(asset => !asset.ClassificationIsManual
                                             && asset.ClassifierVersion != MediaClassifier.ClassifierVersion),
            "eligible" => query.Where(_eligibility.BuildEligiblePredicate()),
            _ => query
        };

    private async Task<ReviewSetCounts> BuildReviewSetCountsAsync(
        IQueryable<MediaAsset> baseQuery,
        CancellationToken cancellationToken)
    {
        var all = await baseQuery.CountAsync(cancellationToken);
        var review = await baseQuery.CountAsync(
            asset => asset.ClassificationDecisionStatus == MediaClassificationDecisionStatus.NeedsReview
                     || (!asset.ClassificationIsManual
                         && asset.Classification == MediaClassification.Unknown),
            cancellationToken);
        var admission = await baseQuery.CountAsync(
            asset => asset.ClassificationIsManual
                     && asset.Classification == MediaClassification.Photograph
                     && asset.ClassificationDecisionStatus
                        != MediaClassificationDecisionStatus.ManualFaceProcessingApproved,
            cancellationToken);
        var risk = await baseQuery.CountAsync(
            asset => asset.ClassificationDecisionReasonCode != null
                     && SafetyReviewReasonCodes.Contains(asset.ClassificationDecisionReasonCode),
            cancellationToken);
        var automatic = await baseQuery.CountAsync(
            asset => !asset.ClassificationIsManual
                     && asset.ClassificationDecisionStatus
                        == MediaClassificationDecisionStatus.AutomaticallyAccepted,
            cancellationToken);
        var manual = await baseQuery.CountAsync(asset => asset.ClassificationIsManual, cancellationToken);
        var nonPhoto = await baseQuery.CountAsync(
            asset => asset.ClassificationIsManual
                ? asset.Classification != MediaClassification.Photograph
                : asset.AnalysisStatus == MediaProcessingStatus.Ready
                  && asset.ClassifierVersion == MediaClassifier.ClassifierVersion
                  && asset.Classification != MediaClassification.Unknown
                  && asset.Classification != MediaClassification.Photograph,
            cancellationToken);
        var failures = await baseQuery.CountAsync(
            asset => asset.AnalysisStatus == MediaProcessingStatus.Failed
                     || asset.ClassificationDecisionStatus
                        == MediaClassificationDecisionStatus.ProcessingFailed,
            cancellationToken);
        var stale = await baseQuery.CountAsync(
            asset => !asset.ClassificationIsManual
                     && asset.ClassifierVersion != MediaClassifier.ClassifierVersion,
            cancellationToken);
        var eligible = await baseQuery.CountAsync(_eligibility.BuildEligiblePredicate(), cancellationToken);

        return new ReviewSetCounts(all, review, admission, risk, automatic, manual, nonPhoto, failures, stale, eligible);
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
        => RedirectToPage(new { Q, FilterClassification, Mode = NormalizeMode(Mode), P });

    private static string NormalizeMode(string? mode)
        => !string.IsNullOrWhiteSpace(mode)
           && SupportedModes.Contains(mode, StringComparer.OrdinalIgnoreCase)
            ? mode.ToLowerInvariant()
            : "review";

    public sealed record ReviewSetCounts(
        int All,
        int Review,
        int Admission,
        int Risk,
        int Automatic,
        int Manual,
        int NonPhoto,
        int Failures,
        int Stale,
        int Eligible)
    {
        public static ReviewSetCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

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
        Guid ConcurrencyToken,
        double BasePhotographScore,
        double NaturalPhotoScore,
        double DocumentStructureScore,
        double GraphicStructureScore,
        double DiagramStructureScore,
        bool FaceProbeAttempted,
        bool FaceEvidenceDetected,
        bool FaceEvidenceUsed,
        string? FaceEvidenceDecisionCode)
    {
        public bool RequiresFaceAdmission => IsManual
                                             && Classification == MediaClassification.Photograph
                                             && DecisionStatus
                                                != MediaClassificationDecisionStatus.ManualFaceProcessingApproved;

        public bool HasManualFaceAdmission => IsManual
                                              && Classification == MediaClassification.Photograph
                                              && DecisionStatus
                                                 == MediaClassificationDecisionStatus.ManualFaceProcessingApproved;

        public string ReviewStatusLabel => DecisionStatus switch
        {
            MediaClassificationDecisionStatus.AutomaticallyAccepted => "Automatically accepted",
            MediaClassificationDecisionStatus.NeedsReview => "Needs review",
            MediaClassificationDecisionStatus.ManuallyConfirmed => "Manually confirmed",
            MediaClassificationDecisionStatus.ManuallyCorrected => "Manually corrected",
            MediaClassificationDecisionStatus.ManualFaceProcessingApproved => "Face processing approved",
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
            MediaClassificationDecisionStatus.ManualFaceProcessingApproved => "bg-success",
            MediaClassificationDecisionStatus.ProcessingFailed => "bg-danger",
            _ => "bg-secondary"
        };

        public string DecisionReasonLabel => DecisionReasonCode switch
        {
            "AUTO_ACCEPTED_HIGH_CONFIDENCE" => "High evidence with clear score separation",
            "BELOW_CATEGORY_THRESHOLD" => "Evidence below the acceptance threshold",
            "AMBIGUOUS_SCORE_MARGIN" => "Winner too close to the runner-up",
            "CONFLICTING_NON_PHOTO_EVIDENCE" => "Conflicting non-photograph evidence",
            "CONFLICTING_BASE_NON_PHOTO_EVIDENCE" => "Strong non-photograph evidence exists before face assistance",
            "EXPLICIT_NON_PHOTO_FILENAME_VETO" => "Filename strongly indicates non-photographic content",
            "DOCUMENT_STRUCTURE_VETO" => "Page or document structure blocks Photograph admission",
            "DIAGRAM_STRUCTURE_VETO" => "Diagram structure blocks Photograph admission",
            "GRAPHIC_STRUCTURE_VETO" => "Designed-graphic structure blocks Photograph admission",
            "PHOTO_BASELINE_INSUFFICIENT" => "Natural-photograph baseline is insufficient",
            "FACE_EVIDENCE_NOT_NATURAL_PHOTO" => "Face-like structure was not accepted as natural-photo evidence",
            "CATEGORY_DISABLED" => "This detector category is disabled",
            "NO_RELIABLE_PREDICTION" => "No reliable prediction",
            "CLASSIFIER_PROCESSING_FAILED" => "Classifier processing failed",
            "MANUAL_CONFIRMATION" => "Human confirmed the prediction",
            "MANUAL_CORRECTION" => "Human corrected the prediction",
            "MANUAL_FACE_ADMISSION" => "Human separately approved biometric face processing",
            "MANUAL_FACE_ADMISSION_REVOKED" => "Human revoked biometric face-processing approval",
            _ => DecisionReasonCode ?? "No decision reason recorded"
        };

        public string FaceAssistanceLabel => FaceEvidenceUsed
            ? "Face evidence used"
            : FaceEvidenceDetected
                ? "Face-like structure blocked"
                : FaceProbeAttempted
                    ? "No verified face support"
                    : "Face probe not required";

        public string FaceAssistanceCss => FaceEvidenceUsed
            ? "classification-assistance--used"
            : FaceEvidenceDetected
                ? "classification-assistance--blocked"
                : "classification-assistance--neutral";

        public static Row Create(MediaAsset asset, FaceEligibilityDecision eligibility)
        {
            var scores = ParseScores(asset.AutomaticClassificationScoresJson);
            var ordered = scores
                .Where(pair => pair.Key != MediaClassification.Unknown)
                .OrderByDescending(pair => pair.Value)
                .ToArray();
            var runnerUp = ordered.FirstOrDefault(pair => pair.Key != asset.PredictedClassification);
            var predictedScore = (double)asset.PredictedClassificationScore;
            var telemetry = ParseTelemetry(asset.AutomaticClassificationMetricsJson);

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
                asset.ClassificationConcurrencyToken,
                telemetry.BasePhotographScore,
                telemetry.NaturalPhotoScore,
                telemetry.DocumentStructureScore,
                telemetry.GraphicStructureScore,
                telemetry.DiagramStructureScore,
                telemetry.FaceProbeAttempted,
                telemetry.FaceEvidenceDetected,
                telemetry.FaceEvidenceUsed,
                telemetry.FaceEvidenceDecisionCode);
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

        private static TelemetrySnapshot ParseTelemetry(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return TelemetrySnapshot.Empty;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("Safety", out var safety))
                {
                    return TelemetrySnapshot.Empty;
                }

                return new TelemetrySnapshot(
                    ReadDouble(safety, "BasePhotographScore"),
                    ReadDouble(safety, "NaturalPhotoScore"),
                    ReadDouble(safety, "DocumentStructureScore"),
                    ReadDouble(safety, "GraphicStructureScore"),
                    ReadDouble(safety, "DiagramStructureScore"),
                    ReadBoolean(safety, "FaceProbeAttempted"),
                    ReadBoolean(safety, "FaceEvidenceDetected"),
                    ReadBoolean(safety, "FaceEvidenceUsed"),
                    ReadString(safety, "FaceEvidenceDecisionCode"));
            }
            catch (JsonException)
            {
                return TelemetrySnapshot.Empty;
            }
        }

        private static double ReadDouble(JsonElement element, string property)
            => element.TryGetProperty(property, out var value) && value.TryGetDouble(out var parsed)
                ? parsed
                : 0d;

        private static bool ReadBoolean(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value))
            {
                return false;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => false
            };
        }

        private static string? ReadString(JsonElement element, string property)
            => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private sealed record TelemetrySnapshot(
            double BasePhotographScore,
            double NaturalPhotoScore,
            double DocumentStructureScore,
            double GraphicStructureScore,
            double DiagramStructureScore,
            bool FaceProbeAttempted,
            bool FaceEvidenceDetected,
            bool FaceEvidenceUsed,
            string? FaceEvidenceDecisionCode)
        {
            public static TelemetrySnapshot Empty { get; } = new(0, 0, 0, 0, 0, false, false, false, null);
        }
    }
}
