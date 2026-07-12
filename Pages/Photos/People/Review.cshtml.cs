using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Pages.Photos.People;

[Authorize(Roles = "Admin,HoD")]
public sealed class ReviewModel : PageModel
{
    private const int PageSize = 24;
    private readonly IMediaPeopleQueryService _people;
    private readonly IFaceIdentityGroupingRuntimeState _groupingState;
    private readonly IFaceCandidateRefreshQueueService _candidateQueue;
    private readonly IFaceReviewService _review;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<ReviewModel> _logger;

    public ReviewModel(
        IMediaPeopleQueryService people,
        IFaceIdentityGroupingRuntimeState groupingState,
        IFaceCandidateRefreshQueueService candidateQueue,
        IFaceReviewService review,
        IOptions<MediaLibraryOptions> options,
        ILogger<ReviewModel> logger)
    {
        _people = people ?? throw new ArgumentNullException(nameof(people));
        _groupingState = groupingState ?? throw new ArgumentNullException(nameof(groupingState));
        _candidateQueue = candidateQueue ?? throw new ArgumentNullException(nameof(candidateQueue));
        _review = review ?? throw new ArgumentNullException(nameof(review));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "matches";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

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

    public IReadOnlyList<MediaPersonOption> AvailablePeople { get; private set; }
        = Array.Empty<MediaPersonOption>();

    public bool FeatureEnabled => _options.People.Enabled;
    public bool GroupingEnabled => _options.People.GroupingEnabled;
    public bool GroupingAvailable { get; private set; } = true;
    public bool ReviewDataAvailable { get; private set; } = true;
    public bool IsGroupsMode => Mode == "groups";
    public bool IsMatchesMode => Mode == "matches";
    public bool IsUnidentifiedMode => Mode == "unidentified";
    public double CandidateStrongSimilarityThreshold => _options.People.CandidateStrongSimilarityThreshold;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeMode();
        if (!FeatureEnabled)
        {
            return Page();
        }

        if (IsGroupsMode && GroupingEnabled)
        {
            var snapshot = _groupingState.GetSnapshot();
            if (snapshot.IsReady && snapshot.Result is not null)
            {
                GroupResult = snapshot.Result;
                AvailablePeople = await _people.GetPersonOptionsAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(snapshot.FailureReason))
                {
                    ErrorMessage = "Identity grouping is using the last successful background snapshot because the latest refresh failed.";
                }
            }
            else
            {
                GroupingAvailable = false;
                Mode = "matches";
                ErrorMessage = string.IsNullOrWhiteSpace(snapshot.FailureReason)
                    ? "Identity grouping is still being prepared in the background. Known-person review remains operational."
                    : "Identity grouping is temporarily unavailable. Known-person review remains operational while the background worker retries.";
                await TryLoadIndividualFacesAsync(cancellationToken);
            }
        }
        else
        {
            await TryLoadIndividualFacesAsync(cancellationToken);
        }

        return Page();
    }

    private async Task TryLoadIndividualFacesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var kind = IsMatchesMode
                ? FaceReviewQueueKind.KnownMatches
                : FaceReviewQueueKind.Unidentified;
            Result = await _people.GetReviewQueueAsync(
                kind,
                Math.Max(1, PageNumber),
                PageSize,
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

    public Task<IActionResult> OnPostIgnoreAsync(
        Guid faceId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.IgnoreAsync(faceId, UserId, cancellationToken),
            "Face acknowledged and left unidentified.",
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
        Guid personId,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _review.RejectManyAsync(faceIds, personId, UserId, cancellationToken),
            "The known-person suggestion was rejected for this group.",
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
                var queued = await _candidateQueue.QueueAllUnassignedAsync(cancellationToken);
                StatusMessage = queued == 0
                    ? "No unassigned faces required a candidate refresh."
                    : $"Queued {queued} unassigned face(s) for background known-person matching.";
            },
            null,
            Mode);

    private string UserId
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? User.Identity?.Name
           ?? "unknown";

    private void NormalizeMode()
    {
        if (GroupingEnabled && string.Equals(Mode, "groups", StringComparison.OrdinalIgnoreCase))
        {
            Mode = "groups";
            return;
        }

        Mode = string.Equals(Mode, "unidentified", StringComparison.OrdinalIgnoreCase)
            ? "unidentified"
            : "matches";
    }

    private async Task<IActionResult> ExecuteAsync(
        Func<Task> action,
        string? successMessage,
        string redirectMode)
    {
        NormalizeMode();
        var normalizedRedirectMode = GroupingEnabled
                                     && string.Equals(redirectMode, "groups", StringComparison.OrdinalIgnoreCase)
            ? "groups"
            : string.Equals(redirectMode, "unidentified", StringComparison.OrdinalIgnoreCase)
                ? "unidentified"
                : "matches";
        if (!FeatureEnabled)
        {
            ErrorMessage = "People intelligence is disabled. Complete readiness checks and enable the feature before reviewing faces.";
            return RedirectToPage(new { Mode = normalizedRedirectMode, PageNumber });
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

        return RedirectToPage(new { Mode = normalizedRedirectMode, PageNumber });
    }
}
