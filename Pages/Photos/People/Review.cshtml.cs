using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos.People;

[Authorize(Roles = "Admin,HoD")]
public sealed class ReviewModel : PageModel
{
    private const int PageSize = 24;
    private readonly IMediaPeopleQueryService _people;
    private readonly IFaceReviewWorkloadService _workloadService;
    private readonly IFaceIdentityGroupingRuntimeState _groupingState;
    private readonly IFaceReviewInvalidationCoordinator _invalidation;
    private readonly IFaceReviewService _review;
    private readonly IFaceCandidateRefreshRuntimeState _candidateRuntime;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<ReviewModel> _logger;

    public ReviewModel(
        IMediaPeopleQueryService people,
        IFaceReviewWorkloadService workloadService,
        IFaceIdentityGroupingRuntimeState groupingState,
        IFaceReviewInvalidationCoordinator invalidation,
        IFaceReviewService review,
        IFaceCandidateRefreshRuntimeState candidateRuntime,
        IOptions<MediaLibraryOptions> options,
        ILogger<ReviewModel> logger)
    {
        _people = people ?? throw new ArgumentNullException(nameof(people));
        _workloadService = workloadService ?? throw new ArgumentNullException(nameof(workloadService));
        _groupingState = groupingState ?? throw new ArgumentNullException(nameof(groupingState));
        _invalidation = invalidation ?? throw new ArgumentNullException(nameof(invalidation));
        _review = review ?? throw new ArgumentNullException(nameof(review));
        _candidateRuntime = candidateRuntime ?? throw new ArgumentNullException(nameof(candidateRuntime));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "matches";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "quality-desc";

    [BindProperty(SupportsGet = true)]
    public string Layout { get; set; } = "triage";

    [BindProperty(SupportsGet = true)]
    public long[] AssetIds { get; set; } = Array.Empty<long>();

    [BindProperty(SupportsGet = true)]
    public string Source { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public string MatchStatus { get; set; } = "all";

    public FaceIdentityGroupingResult GroupResult { get; private set; } = new(
        Array.Empty<FaceIdentityGroup>(), 0, 0, 0);

    public FaceReviewQueueResult Result { get; private set; } = new(
        Array.Empty<FaceReviewQueueItem>(),
        Array.Empty<MediaPersonOption>(),
        0,
        1,
        PageSize,
        false,
        false);

    public FaceReviewWorkloadSummary Workload { get; private set; }
        = FaceReviewWorkloadSummary.Empty;
    public FaceIdentityGroupingRuntimeSnapshot GroupingSnapshot { get; private set; }
        = new(null, null, null);
    public FaceCandidateRefreshRuntimeSnapshot CandidateRuntime { get; private set; } = new(
        false, false, "Not started", string.Empty, null, null, null, null, null, 0, 0, 0, 0, null, null);

    public IReadOnlyList<MediaPersonOption> AvailablePeople { get; private set; }
        = Array.Empty<MediaPersonOption>();

    public bool FeatureEnabled => _options.People.Enabled;
    public bool ExternalSourcesEnabled => _options.IsExternalSourceFeatureEnabled;
    public bool MatchingWorkerEnabled => _options.IsPeopleWorkerEnabled && _options.People.CandidateSearchEnabled;
    public bool MatchingWorkerDelayed => IsMatchingWorkerDelayed(CandidateRuntime);
    public bool GroupingEnabled => _options.IsPeopleWorkerEnabled && _options.People.GroupingEnabled;
    public bool GroupingSnapshotAvailable => IsGroupsMode
        ? GroupingSnapshot.IsReady
        : Workload.GroupingSnapshotAvailable;
    public bool GroupingRefreshPending => IsGroupsMode
        ? GroupingSnapshot.IsRefreshPending
        : Workload.GroupingRefreshPending;
    public string? GroupingFailureReason => IsGroupsMode
        ? GroupingSnapshot.FailureReason
        : Workload.GroupingFailureReason;
    public bool GroupingRefreshing => GroupingRefreshPending
                                      && string.IsNullOrWhiteSpace(GroupingFailureReason);
    public bool GroupingActionsLocked => GroupingRefreshPending;
    public bool GroupingFailed => !string.IsNullOrWhiteSpace(GroupingFailureReason);
    public DateTimeOffset? GroupingRefreshedAtUtc => IsGroupsMode
        ? GroupingSnapshot.RefreshedAtUtc
        : Workload.GroupingRefreshedAtUtc;
    public bool ReviewDataAvailable { get; private set; } = true;
    public bool WorkloadAvailable { get; private set; } = true;
    public string? GroupingNotice { get; private set; }
    public bool IsGroupsMode => Mode == "groups";
    public bool IsMatchesMode => Mode == "matches";
    public bool IsUnidentifiedMode => Mode == "unidentified";
    public bool IsClosedMode => Mode == "closed";
    public bool IsTriageLayout => (IsUnidentifiedMode || IsClosedMode) && Layout == "triage";
    public bool IsScopedToMedia => AssetIds.Length > 0;
    public bool HasQueueFilters => Source != "all" || Year.HasValue || (IsUnidentifiedMode && MatchStatus != "all");
    public IReadOnlyList<int> AvailableYears => Result.AvailableYears ?? Array.Empty<int>();
    public double CandidateStrongSimilarityThreshold => _options.People.CandidateStrongSimilarityThreshold;
    public double GroupingReviewModerateSimilarityThreshold => _options.People.GroupingReviewModerateSimilarityThreshold;
    public double GroupingReviewStrongSimilarityThreshold => _options.People.GroupingReviewStrongSimilarityThreshold;

    public string GroupSimilarityBand(double similarity)
        => similarity >= GroupingReviewStrongSimilarityThreshold
            ? "strong"
            : similarity >= GroupingReviewModerateSimilarityThreshold
                ? "moderate"
                : "weak";

    public string BuildReviewUrl(
        string? mode = null,
        int? pageNumber = null,
        string? layout = null,
        string? sort = null,
        string? source = null,
        int? year = null,
        string? matchStatus = null)
    {
        var targetMode = NormalizeModeValue(mode ?? Mode);
        var targetSource = source ?? Source;
        var targetMatchStatus = matchStatus ?? MatchStatus;
        var targetLayout = layout ?? Layout;
        var defaultLayout = targetMode is "unidentified" or "closed" ? "triage" : "detail";
        var values = new RouteValueDictionary
        {
            ["Mode"] = targetMode,
            ["PageNumber"] = pageNumber is > 1 ? pageNumber : null,
            ["Layout"] = targetLayout == defaultLayout ? null : targetLayout,
            ["Sort"] = (sort ?? Sort) == "quality-desc" ? null : (sort ?? Sort),
            ["Source"] = targetMode == "groups" || targetSource == "all" ? null : targetSource,
            ["Year"] = targetMode == "groups" ? null : year ?? Year,
            ["MatchStatus"] = targetMode == "unidentified" && targetMatchStatus != "all"
                ? targetMatchStatus
                : null
        };
        if (targetMode != "groups")
        {
            for (var index = 0; index < AssetIds.Length; index++)
            {
                values[$"AssetIds[{index}]"] = AssetIds[index];
            }
        }
        return Url.Page("/Photos/People/Review", values) ?? "/Photos/People/Review";
    }

    public string BuildClearQueueFiltersUrl()
    {
        var values = new RouteValueDictionary
        {
            ["Mode"] = Mode,
            ["Layout"] = Layout == ((IsUnidentifiedMode || IsClosedMode) ? "triage" : "detail") ? null : Layout,
            ["Sort"] = Sort == "quality-desc" ? null : Sort
        };
        for (var index = 0; index < AssetIds.Length; index++)
        {
            values[$"AssetIds[{index}]"] = AssetIds[index];
        }
        return Url.Page("/Photos/People/Review", values) ?? "/Photos/People/Review";
    }

    public string BuildClearMediaScopeUrl()
    {
        var values = new RouteValueDictionary
        {
            ["Mode"] = Mode,
            ["Layout"] = Layout == ((IsUnidentifiedMode || IsClosedMode) ? "triage" : "detail") ? null : Layout,
            ["Sort"] = Sort == "quality-desc" ? null : Sort,
            ["Source"] = Source == "all" ? null : Source,
            ["Year"] = Year,
            ["MatchStatus"] = IsUnidentifiedMode && MatchStatus != "all" ? MatchStatus : null
        };
        return Url.Page("/Photos/People/Review", values) ?? "/Photos/People/Review";
    }

    public string BuildWorkloadStatusUrl()
    {
        var values = new RouteValueDictionary();
        for (var index = 0; index < AssetIds.Length; index++)
        {
            values[$"AssetIds[{index}]"] = AssetIds[index];
        }
        return Url.Page("/Photos/People/Review", "WorkloadStatus", values)
               ?? "/Photos/People/Review?handler=WorkloadStatus";
    }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeRequest();
        CandidateRuntime = _candidateRuntime.GetSnapshot();
        if (!FeatureEnabled)
        {
            return Page();
        }

        await TryLoadWorkloadAsync(cancellationToken);

        if (IsGroupsMode && GroupingEnabled)
        {
            GroupingSnapshot = _groupingState.GetSnapshot();
            var snapshot = GroupingSnapshot;
            if (snapshot.Result is not null)
            {
                GroupResult = snapshot.Result;
            }

            AvailablePeople = await _people.GetPersonOptionsAsync(cancellationToken);
            if (!snapshot.IsReady)
            {
                GroupingNotice = string.IsNullOrWhiteSpace(snapshot.FailureReason)
                    ? "Identity groups are being prepared in the background. This page will remain on the Groups workspace while the snapshot is built."
                    : "Identity grouping is temporarily unavailable because the latest background refresh failed. The worker will retry automatically.";
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.FailureReason))
            {
                GroupingNotice = "Identity grouping is showing the last successful snapshot because the latest refresh failed. The background worker will retry automatically.";
            }
            else if (snapshot.IsRefreshPending)
            {
                GroupingNotice = "Identity groups are refreshing in the background. The last successful snapshot remains available until the new one is ready.";
            }
            else
            {
                GroupingNotice = "Identity-group snapshot is current.";
            }
        }
        else
        {
            await TryLoadIndividualFacesAsync(cancellationToken);
        }

        return Page();
    }

    public async Task<IActionResult> OnGetWorkloadStatusAsync(CancellationToken cancellationToken)
    {
        NormalizeRequest();
        if (!FeatureEnabled)
        {
            return new JsonResult(new { enabled = false });
        }

        try
        {
            var workload = await _workloadService.GetAsync(
                new FaceReviewWorkloadQuery(AssetIds),
                cancellationToken);
            var candidateRuntime = _candidateRuntime.GetSnapshot();
            return new JsonResult(new
            {
                enabled = true,
                knownMatches = workload.KnownMatchCount,
                individualReview = workload.IndividualReviewCount,
                matching = workload.MatchingCount,
                matchingFailures = workload.MatchingFailureCount,
                closedUnidentified = workload.ClosedUnidentifiedCount,
                totalUnresolved = workload.TotalUnresolvedCount,
                suggestedGroups = workload.SuggestedGroupCount,
                groupedAppearances = workload.GroupedAppearanceCount,
                ungroupedAppearances = workload.UngroupedAppearanceCount,
                groupingSnapshotAvailable = workload.GroupingSnapshotAvailable,
                groupingRefreshPending = workload.GroupingRefreshPending,
                groupingRefreshedAtUtc = workload.GroupingRefreshedAtUtc?.ToString("O"),
                groupingFailureReason = workload.GroupingFailureReason,
                matchingWorkerDelayed = IsMatchingWorkerDelayed(candidateRuntime),
                matchingWorkerState = candidateRuntime.State,
                matchingWorkerLastHeartbeatUtc = candidateRuntime.LastHeartbeatUtc?.ToString("O")
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "People review workload status could not be refreshed.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private async Task TryLoadWorkloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Workload = await _workloadService.GetAsync(
                new FaceReviewWorkloadQuery(AssetIds),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            WorkloadAvailable = false;
            _logger.LogWarning(exception, "People review workload summary could not be loaded.");
        }
    }

    private async Task TryLoadIndividualFacesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var kind = IsMatchesMode
                ? FaceReviewQueueKind.KnownMatches
                : IsClosedMode
                    ? FaceReviewQueueKind.ClosedUnidentified
                    : FaceReviewQueueKind.Unidentified;
            Result = await _people.GetReviewQueueAsync(
                new FaceReviewQueueQuery(
                    kind,
                    Math.Max(1, PageNumber),
                    PageSize,
                    Sort,
                    AssetIds,
                    Source,
                    Year,
                    IsUnidentifiedMode ? MatchStatus : "all"),
                cancellationToken);
            AvailablePeople = Result.AvailablePeople;
            PageNumber = Result.PageNumber;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReviewDataAvailable = false;
            _logger.LogError(exception, "Individual-face review data could not be loaded.");
            ErrorMessage = "People review data is temporarily unavailable. The error has been logged; verify database connectivity and application logs.";
        }
    }

    public Task<IActionResult> OnPostConfirmAsync(
        Guid faceId,
        Guid personId,
        double? confidence,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.AssignAsync(faceId, personId, UserId, confidence, cancellationToken),
            "Identity confirmed.",
            Mode);

    public Task<IActionResult> OnPostRejectAsync(
        Guid faceId,
        Guid personId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.RejectAsync(faceId, personId, UserId, cancellationToken),
            "Suggestion rejected. It will not be recreated for this model version.",
            Mode);

    public Task<IActionResult> OnPostRejectAllAsync(
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.RejectAsync(faceId, null, UserId, cancellationToken),
            "All current suggestions for this face were rejected.",
            Mode);

    public Task<IActionResult> OnPostAssignExistingAsync(
        Guid faceId,
        Guid personId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.AssignAsync(faceId, personId, UserId, null, cancellationToken),
            "Identity assigned to the selected person.",
            Mode);

    public Task<IActionResult> OnPostAssignSelectedAsync(
        List<Guid> faceIds,
        Guid personId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.AssignManyAsync(faceIds, personId, UserId, null, cancellationToken),
            "Selected appearances were assigned to the confirmed person.",
            Mode);

    public Task<IActionResult> OnPostIgnoreSelectedAsync(
        List<Guid> faceIds,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.IgnoreManyAsync(faceIds, UserId, cancellationToken),
            "Selected appearances were closed as unidentified.",
            Mode);

    public Task<IActionResult> OnPostReopenSelectedAsync(
        List<Guid> faceIds,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.ReopenUnidentifiedManyAsync(faceIds, UserId, cancellationToken),
            "Selected appearances were reopened for review.",
            Mode);

    public Task<IActionResult> OnPostSuppressSelectedAsync(
        List<Guid> faceIds,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.SuppressManyAsync(faceIds, UserId, cancellationToken),
            "Selected detections were marked as not faces.",
            Mode);

    public Task<IActionResult> OnPostIgnoreAsync(
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.IgnoreAsync(faceId, UserId, cancellationToken),
            "Appearance closed as unidentified.",
            Mode);

    public Task<IActionResult> OnPostReopenAsync(
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.ReopenUnidentifiedAsync(faceId, UserId, cancellationToken),
            "Appearance reopened for review.",
            Mode);

    public Task<IActionResult> OnPostCreateAsync(
        Guid faceId,
        string displayName,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.CreatePersonAndAssignAsync(faceId, displayName, UserId, cancellationToken),
            "Person created and identity confirmed.",
            Mode);

    public Task<IActionResult> OnPostSuppressAsync(
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.SuppressAsync(faceId, UserId, cancellationToken),
            "Detection marked as not a face.",
            Mode);

    public Task<IActionResult> OnPostCreateGroupAsync(
        List<Guid> faceIds,
        string displayName,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.CreatePersonAndAssignManyAsync(faceIds, displayName, UserId, cancellationToken),
            "Person created and the selected appearances were confirmed.",
            "groups");

    public Task<IActionResult> OnPostRejectGroupCandidateAsync(
        List<Guid> faceIds,
        Guid candidatePersonId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.RejectManyAsync(faceIds, candidatePersonId, UserId, cancellationToken),
            "The known-person suggestion was rejected for the selected appearances.",
            "groups");

    public Task<IActionResult> OnPostAssignGroupAsync(
        List<Guid> faceIds,
        Guid personId,
        double? confidence,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.AssignManyAsync(faceIds, personId, UserId, confidence, cancellationToken),
            "The selected appearances were assigned to the person.",
            "groups");

    public Task<IActionResult> OnPostRefreshCandidatesAsync(CancellationToken cancellationToken)
        => ExecuteAsync(
            async () =>
            {
                var queued = await _invalidation.ForceRequeueAllCandidatesAsync(cancellationToken);
                StatusMessage = queued == 0
                    ? "No unresolved faces required candidate rematching."
                    : $"Re-run requested for {queued} unresolved face(s). Matching will continue in the background.";
            },
            null,
            Mode);

    private string UserId
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? User.Identity?.Name
           ?? "unknown";

    private void NormalizeRequest()
    {
        Mode = NormalizeModeValue(Mode);
        if (Mode == "groups" && !GroupingEnabled)
        {
            Mode = "unidentified";
        }
        PageNumber = Math.Max(1, PageNumber);
        Sort = Sort?.Trim().ToLowerInvariant() switch
        {
            "quality-asc" => "quality-asc",
            "newest" => "newest",
            "oldest" => "oldest",
            _ => "quality-desc"
        };
        Source = Source?.Trim().ToLowerInvariant() switch
        {
            "projects" => "projects",
            "visits" => "visits",
            "events" => "events",
            "activities" => "activities",
            "external" => "external",
            _ => "all"
        };
        Year = Year is >= 1900 and <= 2200 ? Year : null;
        MatchStatus = IsUnidentifiedMode
            ? MatchStatus?.Trim().ToLowerInvariant() switch
            {
                "no-match" => "no-match",
                "failed" => "failed",
                "not-requested" => "not-requested",
                _ => "all"
            }
            : "all";
        if (IsGroupsMode)
        {
            Source = "all";
            Year = null;
            MatchStatus = "all";
        }
        Layout = IsUnidentifiedMode || IsClosedMode
            ? string.Equals(Layout, "detail", StringComparison.OrdinalIgnoreCase) ? "detail" : "triage"
            : "detail";
        AssetIds = (AssetIds ?? Array.Empty<long>())
            .Where(id => id > 0)
            .Distinct()
            .Take(250)
            .ToArray();
        // Identity grouping is a corpus-level snapshot. A media-scoped deep link must not
        // combine global group membership with scoped workload counters.
        if (IsGroupsMode)
        {
            AssetIds = Array.Empty<long>();
        }
    }

    private bool IsMatchingWorkerDelayed(FaceCandidateRefreshRuntimeSnapshot runtime)
    {
        if (!MatchingWorkerEnabled || !runtime.WorkerConfigured)
        {
            return false;
        }
        if (!runtime.WorkerStarted || !runtime.LastHeartbeatUtc.HasValue)
        {
            return Workload.MatchingCount > 0;
        }

        var allowedSilenceSeconds = Math.Max(
            _options.People.CandidateSearchTimeoutSeconds + 30,
            Math.Max(60, _options.People.CandidateRefreshIdleDelaySeconds * 4));
        return DateTimeOffset.UtcNow - runtime.LastHeartbeatUtc.Value
               > TimeSpan.FromSeconds(allowedSilenceSeconds);
    }

    private string NormalizeModeValue(string? value)
    {
        if (GroupingEnabled && string.Equals(value, "groups", StringComparison.OrdinalIgnoreCase))
        {
            return "groups";
        }
        if (string.Equals(value, "unidentified", StringComparison.OrdinalIgnoreCase))
        {
            return "unidentified";
        }
        if (string.Equals(value, "closed", StringComparison.OrdinalIgnoreCase))
        {
            return "closed";
        }
        return "matches";
    }

    private async Task<IActionResult> ExecuteAsync(
        Func<Task> action,
        string? successMessage,
        string redirectMode)
    {
        NormalizeRequest();
        var normalizedRedirectMode = NormalizeModeValue(redirectMode);
        if (!FeatureEnabled)
        {
            ErrorMessage = "People intelligence is disabled. Complete readiness checks and enable the feature before reviewing faces.";
            return Redirect(BuildReviewUrl(normalizedRedirectMode, PageNumber));
        }

        try
        {
            await action();
            if (!string.IsNullOrWhiteSpace(successMessage))
            {
                StatusMessage = successMessage;
            }
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or InvalidOperationException
                                           or KeyNotFoundException)
        {
            _logger.LogWarning(exception, "Face review operation failed.");
            ErrorMessage = exception.Message;
        }

        return Redirect(BuildReviewUrl(normalizedRedirectMode, PageNumber));
    }
}
