using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
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
    private readonly IMediaLibrarySchemaService _schema;
    private readonly IFaceModelReadinessService _readiness;
    private readonly IFaceQueueService _queue;
    private readonly IMediaProcessingRuntimeState _runtime;
    private readonly IFaceEligibilityPolicy _eligibility;
    private readonly IFaceCandidateRefreshQueueService _candidateQueue;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        MediaLibraryDbContext db,
        IMediaLibrarySchemaService schema,
        IFaceModelReadinessService readiness,
        IFaceQueueService queue,
        IMediaProcessingRuntimeState runtime,
        IFaceEligibilityPolicy eligibility,
        IFaceCandidateRefreshQueueService candidateQueue,
        IOptions<MediaLibraryOptions> options,
        ILogger<IndexModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _eligibility = eligibility ?? throw new ArgumentNullException(nameof(eligibility));
        _candidateQueue = candidateQueue ?? throw new ArgumentNullException(nameof(candidateQueue));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MediaLibrarySchemaStatus SchemaStatus { get; private set; } =
        MediaLibrarySchemaStatus.Unavailable("Schema status has not been evaluated.", "MLS-NOT-CHECKED");
    public bool SchemaReady => SchemaStatus.IsCurrent;
    public bool PeopleEnabled => _options.People.Enabled;
    public string? TelemetryWarning { get; private set; }
    public FaceModelReadiness ModelStatus { get; private set; } = CreateUnavailablePeopleReadiness("Schema status has not been evaluated.");
    public FaceDetectorReadiness DetectorStatus { get; private set; } = CreateUnavailableDetectorReadiness("Schema status has not been evaluated.");
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
    public int ConfirmedAppearances { get; private set; }
    public int KnownPersonSuggestions { get; private set; }
    public int UnidentifiedFaces { get; private set; }
    public int CandidateSearchPending { get; private set; }
    public int CandidateSearchFailures { get; private set; }
    public int PendingFaceJobs { get; private set; }
    public int FailedFaceJobs { get; private set; }
    public int CurrentClassifierAssets { get; private set; }
    public int SafetyGateReferrals { get; private set; }
    public int FaceEvidenceUsed { get; private set; }
    public int FaceEvidenceBlocked { get; private set; }
    public int ManualCorrections { get; private set; }

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ProcessingRuntime = _runtime.GetSnapshot();
        SchemaStatus = await _schema.GetStatusAsync(cancellationToken);

        if (!SchemaStatus.IsCurrent)
        {
            var message = SchemaStatus.Error
                ?? $"The media catalogue schema is not current. Reference {SchemaStatus.DiagnosticReference}.";
            ModelStatus = CreateUnavailablePeopleReadiness(message);
            DetectorStatus = CreateUnavailableDetectorReadiness(message);
            TelemetryWarning = message;
            return;
        }

        ModelStatus = PeopleEnabled
            ? await _readiness.CheckAsync(cancellationToken)
            : CreateDisabledPeopleReadiness();
        DetectorStatus = await _readiness.CheckDetectorAsync(cancellationToken);

        try
        {
            var available = _db.Assets.AsNoTracking()
                .Where(asset => asset.IsAvailable
                                && !asset.IsDeleted
                                && !asset.IsArchived
                                && asset.Kind == MediaAssetKind.Photo);
            AvailableImages = await available.CountAsync(cancellationToken);
            Eligible = await _db.Assets.AsNoTracking()
                .CountAsync(_eligibility.BuildEligiblePredicate(), cancellationToken);
            ConfirmedNonPhotographs = await available.CountAsync(asset =>
                (asset.ClassificationIsManual && asset.Classification != MediaClassification.Photograph)
                || (!asset.ClassificationIsManual
                    && asset.AnalysisStatus == MediaProcessingStatus.Ready
                    && asset.ClassifierVersion == MediaClassifier.ClassifierVersion
                    && asset.Classification != MediaClassification.Unknown
                    && asset.Classification != MediaClassification.Photograph), cancellationToken);
            NeedsReview = await available.CountAsync(asset =>
                asset.ClassificationDecisionStatus == MediaClassificationDecisionStatus.NeedsReview
                || (!asset.ClassificationIsManual && asset.Classification == MediaClassification.Unknown), cancellationToken);
            LowConfidence = await available.CountAsync(asset =>
                !asset.ClassificationIsManual
                && asset.ClassificationDecisionStatus == MediaClassificationDecisionStatus.NeedsReview
                && asset.ClassificationDecisionReasonCode == "BELOW_CATEGORY_THRESHOLD", cancellationToken);
            FaceAdmissionPending = await available.CountAsync(asset =>
                asset.ClassificationIsManual
                && asset.Classification == MediaClassification.Photograph
                && asset.ClassificationDecisionStatus != MediaClassificationDecisionStatus.ManualFaceProcessingApproved,
                cancellationToken);
            PendingClassification = await available.CountAsync(asset =>
                asset.AnalysisStatus == MediaProcessingStatus.NotRequested
                || asset.AnalysisStatus == MediaProcessingStatus.Pending
                || asset.AnalysisStatus == MediaProcessingStatus.Processing, cancellationToken);
            FailedClassification = await available.CountAsync(asset =>
                asset.AnalysisStatus == MediaProcessingStatus.Failed
                || asset.ClassificationDecisionStatus == MediaClassificationDecisionStatus.ProcessingFailed,
                cancellationToken);

            if (PeopleEnabled)
            {
                Faces = await _db.Faces.AsNoTracking().CountAsync(cancellationToken);
                Embedded = await _db.FaceEmbeddings.AsNoTracking()
                    .CountAsync(embedding => embedding.InvalidatedAtUtc == null, cancellationToken);
                Persons = await _db.Persons.AsNoTracking()
                    .CountAsync(person => !person.IsHidden && person.Status == MediaPersonStatus.Confirmed,
                        cancellationToken);
                ConfirmedAppearances = await _db.PersonFaces.AsNoTracking()
                    .CountAsync(assignment => assignment.RemovedAtUtc == null, cancellationToken);

                var reviewableFaces = _db.Faces.AsNoTracking()
                    .Where(face => !face.IsSuppressed
                                   && face.QualityStatus == FaceQualityStatus.EmbeddingEligible
                                   && face.MediaAsset.IsAvailable
                                   && !face.MediaAsset.IsDeleted
                                   && !face.MediaAsset.IsArchived
                                   && !face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null)
                                   && !_db.FaceReviewDecisions.Any(decision =>
                                       decision.MediaFaceId == face.Id
                                       && !decision.CandidatePersonId.HasValue
                                       && decision.Decision == FaceReviewDecisionType.Ignored));
                KnownPersonSuggestions = await reviewableFaces.CountAsync(face =>
                    _db.FaceReviewDecisions.Any(decision =>
                        decision.MediaFaceId == face.Id
                        && decision.CandidatePersonId.HasValue
                        && decision.Decision == FaceReviewDecisionType.Pending), cancellationToken);
                CandidateSearchPending = await reviewableFaces.CountAsync(face =>
                    face.CandidateSearchStatus == FaceCandidateSearchStatus.Pending
                    || face.CandidateSearchStatus == FaceCandidateSearchStatus.Processing, cancellationToken);
                CandidateSearchFailures = await reviewableFaces.CountAsync(face =>
                    face.CandidateSearchStatus == FaceCandidateSearchStatus.Failed, cancellationToken);
                UnidentifiedFaces = await reviewableFaces.CountAsync(face =>
                    !_db.FaceReviewDecisions.Any(decision =>
                        decision.MediaFaceId == face.Id
                        && decision.CandidatePersonId.HasValue
                        && decision.Decision == FaceReviewDecisionType.Pending)
                    && face.CandidateSearchStatus != FaceCandidateSearchStatus.Pending
                    && face.CandidateSearchStatus != FaceCandidateSearchStatus.Processing,
                    cancellationToken);

                PendingFaceJobs = await _db.ProcessingJobs.AsNoTracking().CountAsync(job =>
                    job.JobType == MediaProcessingJobType.DetectFaces
                    && (job.Status == MediaProcessingJobStatus.Pending
                        || job.Status == MediaProcessingJobStatus.Running), cancellationToken);
                FailedFaceJobs = await _db.ProcessingJobs.AsNoTracking().CountAsync(job =>
                    job.JobType == MediaProcessingJobType.DetectFaces
                    && (job.Status == MediaProcessingJobStatus.Failed
                        || job.Status == MediaProcessingJobStatus.DeadLetter), cancellationToken);
            }

            var classifierRows = await available
                .Where(asset => asset.ClassifierVersion == MediaClassifier.ClassifierVersion)
                .Select(asset => new
                {
                    asset.ClassificationDecisionReasonCode,
                    asset.AutomaticClassificationMetricsJson
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

            ManualCorrections = await _db.ClassificationAudits.AsNoTracking()
                .CountAsync(audit =>
                    audit.NewDecisionStatus == MediaClassificationDecisionStatus.ManuallyCorrected,
                    cancellationToken);

        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsCatalogueQueryException(exception))
        {
            var reference = CreateDiagnosticReference();
            _logger.LogError(
                exception,
                "Media intelligence telemetry query failed. Reference={Reference}.",
                reference);

            TelemetryWarning =
                $"Media intelligence telemetry is temporarily unavailable. Reference {reference}.";
        }
    }

    public async Task<IActionResult> OnPostQueueAsync(
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (!PeopleEnabled)
        {
            ErrorMessage = "People intelligence is disabled in MediaLibrary configuration.";
            return RedirectToPage();
        }

        if (!await EnsureSchemaReadyAsync(cancellationToken))
        {
            return RedirectToPage();
        }

        var readiness = await _readiness.CheckAsync(cancellationToken);
        if (!readiness.IsReady)
        {
            ErrorMessage = readiness.Message;
            return RedirectToPage();
        }

        var queued = await _queue.QueueEligibleAsync(Math.Clamp(limit, 1, 250), cancellationToken);
        StatusMessage = queued == 0
            ? "All currently approved photographs have already been queued or processed."
            : $"Queued {queued} photograph(s) for face detection and embedding.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshIdentityCandidatesAsync(
        CancellationToken cancellationToken)
    {
        if (!PeopleEnabled)
        {
            ErrorMessage = "People intelligence is disabled in MediaLibrary configuration.";
            return RedirectToPage();
        }

        if (!await EnsureSchemaReadyAsync(cancellationToken))
        {
            return RedirectToPage();
        }

        var readiness = await _readiness.CheckAsync(cancellationToken);
        if (!readiness.IsReady)
        {
            ErrorMessage = readiness.Message;
            return RedirectToPage();
        }

        var queued = await _candidateQueue.QueueAllUnassignedAsync(cancellationToken);
        StatusMessage = queued == 0
            ? "No eligible unassigned faces require identity matching."
            : $"Queued {queued} unassigned face(s) for background known-person matching.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshReadinessAsync(
        CancellationToken cancellationToken)
    {
        var schema = await _schema.GetStatusAsync(cancellationToken);
        if (!schema.IsCurrent)
        {
            ErrorMessage = schema.Error
                ?? $"The media catalogue schema is not current. Reference {schema.DiagnosticReference}.";
            return RedirectToPage();
        }

        var detector = await _readiness.CheckDetectorAsync(forceRefresh: true, cancellationToken);
        if (PeopleEnabled)
        {
            var readiness = await _readiness.CheckAsync(forceRefresh: true, cancellationToken);
            StatusMessage = $"Readiness refreshed. People: {readiness.Message} Detector assistance: {detector.Message}";
        }
        else
        {
            StatusMessage = $"Readiness refreshed. People intelligence is disabled. Detector assistance: {detector.Message}";
        }
        return RedirectToPage();
    }

    private async Task<bool> EnsureSchemaReadyAsync(CancellationToken cancellationToken)
    {
        var status = await _schema.GetStatusAsync(cancellationToken);
        if (status.IsCurrent)
        {
            return true;
        }

        ErrorMessage = status.Error
            ?? $"The media catalogue schema is not current. Reference {status.DiagnosticReference}.";
        return false;
    }

    private static bool IsCatalogueQueryException(Exception exception)
        => exception is NpgsqlException
            or DbUpdateException
            or InvalidOperationException
            or TimeoutException
            or System.Data.DataException;

    private static string CreateDiagnosticReference()
        => $"MLI-{Guid.NewGuid():N}"[..14].ToUpperInvariant();

    private static FaceModelReadiness CreateDisabledPeopleReadiness()
        => new(
            IsEnabled: false,
            IsReady: false,
            State: FaceReadinessState.Disabled,
            Message: "People intelligence is disabled in MediaLibrary configuration.",
            DetectorPath: null,
            EmbedderPath: null,
            CheckedAtUtc: DateTimeOffset.UtcNow,
            Checks: new[]
            {
                new FaceReadinessCheck(
                    "feature",
                    "People intelligence",
                    false,
                    "Disabled",
                    "Enable MediaLibrary:People:Enabled only after approved models and operating policy are in place.")
            });

    private static FaceModelReadiness CreateUnavailablePeopleReadiness(string message)
        => new(
            IsEnabled: false,
            IsReady: false,
            State: FaceReadinessState.SchemaUnavailable,
            Message: message,
            DetectorPath: null,
            EmbedderPath: null,
            CheckedAtUtc: DateTimeOffset.UtcNow,
            Checks: new[]
            {
                new FaceReadinessCheck(
                    "schema",
                    "Media catalogue schema",
                    false,
                    "Unavailable",
                    "Apply and validate the MediaLibraryDbContext migrations.")
            });

    private static FaceDetectorReadiness CreateUnavailableDetectorReadiness(string message)
        => new(
            IsEnabled: false,
            IsReady: false,
            State: FaceReadinessState.SchemaUnavailable,
            Message: message,
            DetectorPath: null,
            CheckedAtUtc: DateTimeOffset.UtcNow,
            Checks: new[]
            {
                new FaceReadinessCheck(
                    "schema",
                    "Media catalogue schema",
                    false,
                    "Unavailable",
                    "Apply and validate the MediaLibraryDbContext migrations.")
            });

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
        => element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.True;

    private sealed record FaceEvidenceSnapshot(bool Detected, bool Used)
    {
        public static FaceEvidenceSnapshot Empty { get; } = new(false, false);
    }
}
