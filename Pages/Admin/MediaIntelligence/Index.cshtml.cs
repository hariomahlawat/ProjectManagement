using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Admin.MediaIntelligence;

[Authorize(Roles = "Admin,HoD")]
public sealed class IndexModel : PageModel
{
    private static readonly string[] SafetyReferralReasonCodes =
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
    private readonly IFaceModelReadinessService _readiness;
    private readonly IFaceQueueService _queue;
    private readonly IMediaProcessingRuntimeState _runtime;
    private readonly IFaceEligibilityPolicy _eligibility;
    private readonly IFaceIdentityGroupingService _groups;
    private readonly IFaceCandidateSuggestionService _candidateSuggestions;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        MediaLibraryDbContext db,
        IFaceModelReadinessService readiness,
        IFaceQueueService queue,
        IMediaProcessingRuntimeState runtime,
        IFaceEligibilityPolicy eligibility,
        IFaceIdentityGroupingService groups,
        IFaceCandidateSuggestionService candidateSuggestions,
        ILogger<IndexModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
        _groups = groups ?? throw new ArgumentNullException(nameof(groups));
        _candidateSuggestions = candidateSuggestions ?? throw new ArgumentNullException(nameof(candidateSuggestions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FaceModelReadiness ModelStatus { get; private set; } = null!;
    public FaceDetectorReadiness DetectorStatus { get; private set; } = null!;
    public MediaProcessingRuntimeSnapshot ProcessingRuntime { get; private set; } = null!;
    public int AvailableImages { get; private set; }
    public int Eligible { get; private set; }
    public int NeedsReview { get; private set; }
    public int ConfirmedNonPhotographs { get; private set; }
    public int LowConfidence { get; private set; }
    public int FaceAdmissionPending { get; private set; }
    public int PendingClassification { get; private set; }
    public int FailedClassification { get; private set; }
    public int Faces { get; private set; }
    public int Embedded { get; private set; }
    public int Persons { get; private set; }
    public int PendingReview { get; private set; }
    public int PendingFaceJobs { get; private set; }
    public int FailedFaceJobs { get; private set; }
    public int CurrentClassifierAssets { get; private set; }
    public int SafetyGateReferrals { get; private set; }
    public int FaceEvidenceUsed { get; private set; }
    public int FaceEvidenceBlocked { get; private set; }
    public int ManualCorrections { get; private set; }
    public int IdentityGroups { get; private set; }
    public int GroupedUnassignedFaces { get; private set; }
    public int RemainingIndividualFaces { get; private set; }
    public bool IdentityGroupingAvailable { get; private set; } = true;

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ModelStatus = await _readiness.CheckAsync(cancellationToken);
        DetectorStatus = await _readiness.CheckDetectorAsync(cancellationToken);
        ProcessingRuntime = _runtime.GetSnapshot();

        var available = _db.Assets.AsNoTracking().Where(x => x.IsAvailable && !x.IsDeleted && !x.IsArchived && x.Kind == MediaAssetKind.Photo);
        AvailableImages = await available.CountAsync(cancellationToken);
        var eligiblePredicate = _eligibility.BuildEligiblePredicate();
        Eligible = await _db.Assets.AsNoTracking().CountAsync(eligiblePredicate, cancellationToken);
        ConfirmedNonPhotographs = await available.CountAsync(x =>
            (x.ClassificationIsManual && x.Classification != MediaClassification.Photograph)
            || (!x.ClassificationIsManual
                && x.AnalysisStatus == MediaProcessingStatus.Ready
                && x.ClassifierVersion == MediaClassifier.ClassifierVersion
                && x.Classification != MediaClassification.Unknown
                && x.Classification != MediaClassification.Photograph), cancellationToken);
        NeedsReview = await available.CountAsync(x =>
            x.ClassificationDecisionStatus == MediaClassificationDecisionStatus.NeedsReview
            || (!x.ClassificationIsManual && x.Classification == MediaClassification.Unknown), cancellationToken);
        LowConfidence = await available.CountAsync(x =>
            !x.ClassificationIsManual
            && x.ClassificationDecisionStatus == MediaClassificationDecisionStatus.NeedsReview
            && x.ClassificationDecisionReasonCode == "BELOW_CATEGORY_THRESHOLD", cancellationToken);
        FaceAdmissionPending = await available.CountAsync(x =>
            x.ClassificationIsManual
            && x.Classification == MediaClassification.Photograph
            && x.ClassificationDecisionStatus != MediaClassificationDecisionStatus.ManualFaceProcessingApproved, cancellationToken);
        PendingClassification = await available.CountAsync(x =>
            x.AnalysisStatus == MediaProcessingStatus.NotRequested
            || x.AnalysisStatus == MediaProcessingStatus.Pending
            || x.AnalysisStatus == MediaProcessingStatus.Processing, cancellationToken);
        FailedClassification = await available.CountAsync(x =>
            x.AnalysisStatus == MediaProcessingStatus.Failed
            || x.ClassificationDecisionStatus == MediaClassificationDecisionStatus.ProcessingFailed, cancellationToken);
        Faces = await _db.Faces.AsNoTracking().CountAsync(cancellationToken);
        Embedded = await _db.FaceEmbeddings.AsNoTracking().CountAsync(x => x.InvalidatedAtUtc == null, cancellationToken);
        Persons = await _db.Persons.AsNoTracking().CountAsync(
            x => !x.IsHidden && x.Status == MediaPersonStatus.Confirmed, cancellationToken);
        PendingReview = await _db.FaceReviewDecisions.AsNoTracking().CountAsync(x => x.Decision == FaceReviewDecisionType.Pending, cancellationToken);
        try
        {
            var identityGroups = await _groups.GetGroupsAsync(cancellationToken);
            IdentityGroups = identityGroups.TotalGroups;
            GroupedUnassignedFaces = identityGroups.GroupedFaceCount;
            RemainingIndividualFaces = identityGroups.RemainingIndividualFaceCount;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            IdentityGroupingAvailable = false;
            _logger.LogError(exception,
                "Identity grouping metrics could not be loaded. The media-intelligence dashboard will continue without optional grouping metrics.");
        }
        PendingFaceJobs = await _db.ProcessingJobs.AsNoTracking().CountAsync(x => x.JobType == MediaProcessingJobType.DetectFaces && (x.Status == MediaProcessingJobStatus.Pending || x.Status == MediaProcessingJobStatus.Running), cancellationToken);
        FailedFaceJobs = await _db.ProcessingJobs.AsNoTracking().CountAsync(x => x.JobType == MediaProcessingJobType.DetectFaces && (x.Status == MediaProcessingJobStatus.Failed || x.Status == MediaProcessingJobStatus.DeadLetter), cancellationToken);

        var classifierRows = await available
            .Where(x => x.ClassifierVersion == MediaClassifier.ClassifierVersion)
            .Select(x => new
            {
                x.ClassificationDecisionReasonCode,
                x.AutomaticClassificationMetricsJson
            })
            .ToListAsync(cancellationToken);
        CurrentClassifierAssets = classifierRows.Count;
        SafetyGateReferrals = classifierRows.Count(row =>
            row.ClassificationDecisionReasonCode is not null
            && SafetyReferralReasonCodes.Contains(row.ClassificationDecisionReasonCode));
        foreach (var row in classifierRows)
        {
            var evidence = ReadFaceEvidence(row.AutomaticClassificationMetricsJson);
            if (evidence.Used) FaceEvidenceUsed++;
            if (evidence.Detected && !evidence.Used) FaceEvidenceBlocked++;
        }

        ManualCorrections = await _db.ClassificationAudits
            .AsNoTracking()
            .CountAsync(audit => audit.NewDecisionStatus == MediaClassificationDecisionStatus.ManuallyCorrected, cancellationToken);
    }

    private static FaceEvidenceSnapshot ReadFaceEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return FaceEvidenceSnapshot.Empty;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Safety", out var safety))
            {
                return FaceEvidenceSnapshot.Empty;
            }

            return new FaceEvidenceSnapshot(
                ReadBoolean(safety, "FaceEvidenceDetected"),
                ReadBoolean(safety, "FaceEvidenceUsed"));
        }
        catch (JsonException)
        {
            return FaceEvidenceSnapshot.Empty;
        }
    }

    private static bool ReadBoolean(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return false;
        return value.ValueKind == JsonValueKind.True;
    }

    private sealed record FaceEvidenceSnapshot(bool Detected, bool Used)
    {
        public static FaceEvidenceSnapshot Empty { get; } = new(false, false);
    }

    public async Task<IActionResult> OnPostQueueAsync(int limit = 25, CancellationToken cancellationToken = default)
    {
        var readiness = await _readiness.CheckAsync(cancellationToken);
        if (!readiness.IsReady)
        {
            ErrorMessage = readiness.Message;
            return RedirectToPage();
        }

        var boundedLimit = Math.Clamp(limit, 1, 250);
        var queued = await _queue.QueueEligibleAsync(boundedLimit, cancellationToken);
        StatusMessage = queued == 0 ? "No new eligible photographs were queued." : $"Queued {queued} photograph(s) for face analysis.";
        return RedirectToPage();
    }


    public async Task<IActionResult> OnPostRefreshIdentityCandidatesAsync(CancellationToken cancellationToken)
    {
        var readiness = await _readiness.CheckAsync(cancellationToken);
        if (!readiness.IsReady)
        {
            ErrorMessage = readiness.Message;
            return RedirectToPage();
        }

        var refreshed = await _candidateSuggestions.RefreshUnassignedAsync(500, cancellationToken);
        StatusMessage = refreshed == 0
            ? "No unassigned faces were available for identity matching."
            : $"Identity suggestions refreshed for {refreshed} unassigned face(s).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshReadinessAsync(CancellationToken cancellationToken)
    {
        var readiness = await _readiness.CheckAsync(forceRefresh: true, cancellationToken);
        var detector = await _readiness.CheckDetectorAsync(forceRefresh: true, cancellationToken);
        StatusMessage = $"Readiness refreshed. People: {readiness.Message} Detector assistance: {detector.Message}";
        return RedirectToPage();
    }
}
