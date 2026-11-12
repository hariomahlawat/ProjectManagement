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

    // What was just decided, so the message that confirms it can offer Undo (PeopleReviewUndo).
    // Held in TempData by hand: it is looked at without being used up while the message is on
    // screen, used up by the Undo request, and dropped as soon as the message is gone.
    private const string UndoStateKey = "PeopleReviewUndo";

    private string? UndoState
    {
        get => TempData.Peek(UndoStateKey) as string;
        set
        {
            if (value is null)
            {
                TempData.Remove(UndoStateKey);
            }
            else
            {
                TempData[UndoStateKey] = value;
            }
        }
    }

    /// <summary>True while the confirming message is on screen and its decision can be undone.</summary>
    public bool CanUndo => UndoId is not null;

    /// <summary>Names the decision this page's Undo button is about; sent back with the button.</summary>
    public string? UndoId { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeRequest();
        // Undo is offered with the message that confirms the decision, never on a later visit.
        UndoId = string.IsNullOrWhiteSpace(StatusMessage)
            ? null
            : PeopleReviewUndo.Read(UndoState, UserId, DateTimeOffset.UtcNow)?.Id;
        if (!CanUndo)
        {
            UndoState = null;
        }

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
                    ? "PRISM is grouping the faces. The groups appear here when they are ready."
                    : "PRISM could not group the faces this time. It will try again by itself.";
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.FailureReason))
            {
                GroupingNotice = "These groups are from the last time grouping worked. The latest attempt failed; PRISM will try again by itself.";
            }
            else if (snapshot.IsRefreshPending)
            {
                GroupingNotice = "PRISM is grouping the faces again. These groups stay until the new ones are ready.";
            }
            else
            {
                GroupingNotice = "The groups are up to date.";
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
            ErrorMessage = "The faces could not be loaded. The error is logged; try again in a moment.";
        }
    }

    public Task<IActionResult> OnPostConfirmAsync(
        Guid faceId,
        Guid personId,
        double? confidence,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.AssignAsync(faceId, personId, UserId, confidence, cancellationToken),
            "Named.",
            Mode,
            started => NamingUndoAsync(() => personId, new[] { faceId }, started, cancellationToken));

    public Task<IActionResult> OnPostRejectAsync(
        Guid faceId,
        Guid personId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.RejectAsync(faceId, personId, UserId, cancellationToken),
            "Marked as not this person. PRISM will not suggest it again.",
            Mode);

    public Task<IActionResult> OnPostRejectAllAsync(
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.RejectAsync(faceId, null, UserId, cancellationToken),
            "None of the suggested people. The face is now under Unnamed faces.",
            Mode);

    public Task<IActionResult> OnPostAssignExistingAsync(
        Guid faceId,
        Guid personId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.AssignAsync(faceId, personId, UserId, null, cancellationToken),
            "Named.",
            Mode,
            started => NamingUndoAsync(() => personId, new[] { faceId }, started, cancellationToken));

    public Task<IActionResult> OnPostAssignSelectedAsync(
        List<Guid> faceIds,
        Guid personId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.AssignManyAsync(faceIds, personId, UserId, null, cancellationToken),
            "The selected faces were named.",
            Mode,
            started => NamingUndoAsync(() => personId, faceIds, started, cancellationToken));

    // Several single faces named as a new person: the same operation as naming a group, kept
    // on the queue the reviewer is in.
    public Task<IActionResult> OnPostCreateSelectedAsync(
        List<Guid> faceIds,
        string displayName,
        CancellationToken cancellationToken)
    {
        var created = Guid.Empty;
        return ExecuteAsync(
            async () => created = await _review.CreatePersonAndAssignManyAsync(faceIds, displayName, UserId, cancellationToken),
            "New person added and the selected faces named.",
            Mode,
            started => NamingUndoAsync(() => created, faceIds, started, cancellationToken));
    }

    public Task<IActionResult> OnPostIgnoreSelectedAsync(
        List<Guid> faceIds,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.IgnoreManyAsync(faceIds, UserId, cancellationToken),
            "The selected faces were left unnamed.",
            Mode,
            started => LeavingUnnamedUndoAsync(faceIds, started, cancellationToken));

    public Task<IActionResult> OnPostReopenSelectedAsync(
        List<Guid> faceIds,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.ReopenUnidentifiedManyAsync(faceIds, UserId, cancellationToken),
            "The selected faces are back in review.",
            Mode);

    public Task<IActionResult> OnPostSuppressSelectedAsync(
        List<Guid> faceIds,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.SuppressManyAsync(faceIds, UserId, cancellationToken),
            "The selected items were marked as not faces.",
            Mode);

    public Task<IActionResult> OnPostIgnoreAsync(
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.IgnoreAsync(faceId, UserId, cancellationToken),
            "Left unnamed.",
            Mode,
            started => LeavingUnnamedUndoAsync(new[] { faceId }, started, cancellationToken));

    // Takes back the decision the confirming message was about. Naming is undone through the
    // same correction that the person's page uses, so it is recorded like any other correction;
    // its reason is written for the reviewer. A person created by the naming is left in People.
    public async Task<IActionResult> OnPostUndoAsync(string? undoId, CancellationToken cancellationToken)
    {
        // The button names its decision. If another decision has been made since (in another
        // tab), the note is about that one: it is left alone and this Undo is refused.
        var undo = PeopleReviewUndo.Read(UndoState, undoId, UserId, DateTimeOffset.UtcNow);
        if (undo is not null)
        {
            UndoState = null;
        }

        if (undo is null)
        {
            NormalizeRequest();
            ErrorMessage = "That can no longer be undone here. A name can still be corrected from the person's page.";
            return Redirect(BuildReviewUrl(Mode, PageNumber));
        }

        // The decision must still be this reviewer's own. If someone has since taken it back and
        // decided again, what stands now is theirs, and this Undo must not remove it.
        var since = undo.DecidedAtUtc.AddSeconds(-1);
        var standing = undo.Kind == PeopleReviewUndo.Named
            ? await _review.GetFacesAssignedByAsync(undo.PersonId!.Value, undo.FaceIds, UserId, since, cancellationToken)
            : await _review.GetFacesLeftUnnamedByAsync(undo.FaceIds, UserId, since, cancellationToken);
        if (standing.Count != undo.FaceIds.Count)
        {
            NormalizeRequest();
            ErrorMessage = "Someone has changed that since, so it was not undone. Check the face again before correcting it.";
            return Redirect(BuildReviewUrl(Mode, PageNumber));
        }

        return await (undo.Kind == PeopleReviewUndo.Named
            ? ExecuteAsync(
                () => _review.ReturnAssignmentsToReviewAsync(undo.PersonId!.Value, undo.FaceIds.ToList(), UserId, PeopleReviewUndo.UndoReason, cancellationToken),
                undo.FaceIds.Count == 1 ? "Undone. The face is back in review." : "Undone. The faces are back in review.",
                Mode)
            : ExecuteAsync(
                () => _review.ReopenUnidentifiedManyAsync(undo.FaceIds.ToList(), UserId, cancellationToken),
                undo.FaceIds.Count == 1 ? "Undone. The face is back in review." : "Undone. The faces are back in review.",
                Mode));
    }

    public Task<IActionResult> OnPostReopenAsync(
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.ReopenUnidentifiedAsync(faceId, UserId, cancellationToken),
            "Back in review.",
            Mode);

    public Task<IActionResult> OnPostCreateAsync(
        Guid faceId,
        string displayName,
        CancellationToken cancellationToken)
    {
        var created = Guid.Empty;
        return ExecuteAsync(
            async () => created = await _review.CreatePersonAndAssignAsync(faceId, displayName, UserId, cancellationToken),
            "New person added and named.",
            Mode,
            started => NamingUndoAsync(() => created, new[] { faceId }, started, cancellationToken));
    }

    public Task<IActionResult> OnPostSuppressAsync(
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.SuppressAsync(faceId, UserId, cancellationToken),
            "Marked as not a face.",
            Mode);

    public Task<IActionResult> OnPostCreateGroupAsync(
        List<Guid> faceIds,
        string displayName,
        CancellationToken cancellationToken)
    {
        var created = Guid.Empty;
        return ExecuteAsync(
            async () => created = await _review.CreatePersonAndAssignManyAsync(faceIds, displayName, UserId, cancellationToken),
            "New person added and the selected faces named.",
            "groups",
            started => NamingUndoAsync(() => created, faceIds, started, cancellationToken));
    }

    public Task<IActionResult> OnPostRejectGroupCandidateAsync(
        List<Guid> faceIds,
        Guid candidatePersonId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.RejectManyAsync(faceIds, candidatePersonId, UserId, cancellationToken),
            "Marked as not this person for the selected faces.",
            "groups");

    public Task<IActionResult> OnPostAssignGroupAsync(
        List<Guid> faceIds,
        Guid personId,
        double? confidence,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.AssignManyAsync(faceIds, personId, UserId, confidence, cancellationToken),
            "The selected faces were named.",
            "groups",
            started => NamingUndoAsync(() => personId, faceIds, started, cancellationToken));

    public Task<IActionResult> OnPostRefreshCandidatesAsync(CancellationToken cancellationToken)
        => ExecuteAsync(
            async () =>
            {
                var queued = await _invalidation.ForceRequeueAllCandidatesAsync(cancellationToken);
                StatusMessage = queued == 0
                    ? "No face needed checking again."
                    : $"Checking {queued} face(s) again. This carries on in the background.";
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

    // What a naming leaves to undo: only the faces this request itself gave to the person. A
    // face another reviewer had already given to the same person is named without anything
    // being assigned, and must not be taken away from them by this reviewer's Undo.
    private async Task<string?> NamingUndoAsync(
        Func<Guid> personId,
        IReadOnlyCollection<Guid> faceIds,
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        var person = personId();
        var assigned = await _review.GetFacesAssignedByAsync(person, faceIds, UserId, started, cancellationToken);
        return PeopleReviewUndo.ForNaming(person, assigned, UserId, started);
    }

    // The same for leaving faces unnamed: a face another reviewer had already left unnamed is
    // not closed again by this request, and this reviewer's Undo must not reopen it.
    private async Task<string?> LeavingUnnamedUndoAsync(
        IReadOnlyCollection<Guid> faceIds,
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        var closed = await _review.GetFacesLeftUnnamedByAsync(faceIds, UserId, started, cancellationToken);
        return PeopleReviewUndo.ForLeavingUnnamed(closed, UserId, started);
    }

    private async Task<IActionResult> ExecuteAsync(
        Func<Task> action,
        string? successMessage,
        string redirectMode,
        Func<DateTimeOffset, Task<string?>>? undoState = null)
    {
        // An earlier decision can no longer be undone once another has been made.
        UndoState = null;
        var started = DateTimeOffset.UtcNow;
        NormalizeRequest();
        var normalizedRedirectMode = NormalizeModeValue(redirectMode);
        if (!FeatureEnabled)
        {
            ErrorMessage = "Face review is switched off. The face recognition models must pass their checks first.";
            return Redirect(BuildReviewUrl(normalizedRedirectMode, PageNumber));
        }

        try
        {
            await action();
            if (!string.IsNullOrWhiteSpace(successMessage))
            {
                StatusMessage = successMessage;
            }

            UndoState = undoState is null ? null : await undoState(started);
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
