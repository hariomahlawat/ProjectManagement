using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Separates inexpensive unnamed-person grouping invalidation from the materially more
/// expensive operation of re-running candidate matching across the unresolved corpus.
/// Routine review decisions invalidate grouping only; a corpus-wide candidate refresh is
/// reserved for changes to trusted biometric evidence or person visibility.
/// </summary>
public interface IFaceReviewInvalidationCoordinator
{
    void NotifyGroupingChanged();

    Task NotifyFacesNeedRematchAsync(
        IReadOnlyCollection<Guid> faceIds,
        CancellationToken cancellationToken);

    Task NotifyReferenceEvidenceChangedAsync(CancellationToken cancellationToken);

    Task<int> ForceRequeueAllCandidatesAsync(CancellationToken cancellationToken);
}

public sealed class FaceReviewInvalidationCoordinator : IFaceReviewInvalidationCoordinator
{
    private readonly IFaceIdentityGroupingRuntimeState _groupingState;
    private readonly IFaceCandidateRefreshQueueService _candidateQueue;
    private readonly bool _groupingOperational;
    private readonly ILogger<FaceReviewInvalidationCoordinator> _logger;

    public FaceReviewInvalidationCoordinator(
        IFaceIdentityGroupingRuntimeState groupingState,
        IFaceCandidateRefreshQueueService candidateQueue,
        IOptions<MediaLibraryOptions> options,
        ILogger<FaceReviewInvalidationCoordinator> logger)
    {
        _groupingState = groupingState ?? throw new ArgumentNullException(nameof(groupingState));
        _candidateQueue = candidateQueue ?? throw new ArgumentNullException(nameof(candidateQueue));
        var configured = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _groupingOperational = configured.IsPeopleWorkerEnabled && configured.People.GroupingEnabled;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void NotifyGroupingChanged()
    {
        if (_groupingOperational)
        {
            _groupingState.Invalidate();
        }
    }

    private void InvalidateGroupingIfOperational()
    {
        if (_groupingOperational)
        {
            _groupingState.Invalidate();
        }
    }

    public async Task NotifyFacesNeedRematchAsync(
        IReadOnlyCollection<Guid> faceIds,
        CancellationToken cancellationToken)
    {
        InvalidateGroupingIfOperational();
        var selected = faceIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(500)
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        try
        {
            await _candidateQueue.QueueFacesAsync(selected, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to queue {FaceCount} affected face(s) for bounded candidate rematching after a review-state change.",
                selected.Length);
        }
    }

    public async Task NotifyReferenceEvidenceChangedAsync(CancellationToken cancellationToken)
    {
        InvalidateGroupingIfOperational();
        try
        {
            await _candidateQueue.QueueAllUnassignedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The identity mutation has already committed. Matching is derived evidence and
            // may safely retry in the background without rolling back the human decision.
            _logger.LogWarning(
                exception,
                "Unable to requeue unresolved faces after trusted identity evidence changed.");
        }
    }

    public async Task<int> ForceRequeueAllCandidatesAsync(CancellationToken cancellationToken)
    {
        InvalidateGroupingIfOperational();
        return await _candidateQueue.QueueAllUnassignedAsync(cancellationToken);
    }
}
