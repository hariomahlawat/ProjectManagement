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
public sealed class IndexModel : PageModel
{
    private readonly MediaLibraryDbContext _db;
    private readonly IFaceModelReadinessService _readiness;
    private readonly IFaceQueueService _queue;
    private readonly IMediaProcessingRuntimeState _runtime;
    private readonly IFaceEligibilityPolicy _eligibility;
    private readonly MediaLibraryOptions _options;

    public IndexModel(MediaLibraryDbContext db, IFaceModelReadinessService readiness, IFaceQueueService queue, IMediaProcessingRuntimeState runtime, IFaceEligibilityPolicy eligibility, IOptions<MediaLibraryOptions> options)
    {
        _db = db;
        _readiness = readiness;
        _queue = queue;
        _runtime = runtime;
        _eligibility = eligibility;
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public FaceModelReadiness ModelStatus { get; private set; } = null!;
    public MediaProcessingRuntimeSnapshot ProcessingRuntime { get; private set; } = null!;
    public int AvailableImages { get; private set; }
    public int Eligible { get; private set; }
    public int NeedsReview { get; private set; }
    public int ConfirmedNonPhotographs { get; private set; }
    public int LowConfidence { get; private set; }
    public int Faces { get; private set; }
    public int Embedded { get; private set; }
    public int Persons { get; private set; }
    public int PendingReview { get; private set; }
    public int PendingFaceJobs { get; private set; }
    public int FailedFaceJobs { get; private set; }

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ModelStatus = await _readiness.CheckAsync(cancellationToken);
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
        NeedsReview = Math.Max(0, AvailableImages - Eligible - ConfirmedNonPhotographs);
        LowConfidence = await available.CountAsync(x => !x.ClassificationIsManual
            && (x.ClassificationConfidence == null
                || x.ClassificationConfidence < _options.People.MinimumClassificationConfidence), cancellationToken);
        Faces = await _db.Faces.AsNoTracking().CountAsync(cancellationToken);
        Embedded = await _db.FaceEmbeddings.AsNoTracking().CountAsync(x => x.InvalidatedAtUtc == null, cancellationToken);
        Persons = await _db.Persons.AsNoTracking().CountAsync(x => !x.IsHidden, cancellationToken);
        PendingReview = await _db.FaceReviewDecisions.AsNoTracking().CountAsync(x => x.Decision == FaceReviewDecisionType.Pending, cancellationToken);
        PendingFaceJobs = await _db.ProcessingJobs.AsNoTracking().CountAsync(x => x.JobType == MediaProcessingJobType.DetectFaces && (x.Status == MediaProcessingJobStatus.Pending || x.Status == MediaProcessingJobStatus.Running), cancellationToken);
        FailedFaceJobs = await _db.ProcessingJobs.AsNoTracking().CountAsync(x => x.JobType == MediaProcessingJobType.DetectFaces && (x.Status == MediaProcessingJobStatus.Failed || x.Status == MediaProcessingJobStatus.DeadLetter), cancellationToken);
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

    public async Task<IActionResult> OnPostRefreshReadinessAsync(CancellationToken cancellationToken)
    {
        var readiness = await _readiness.CheckAsync(forceRefresh: true, cancellationToken);
        StatusMessage = $"Readiness refreshed: {readiness.Message}";
        return RedirectToPage();
    }
}
