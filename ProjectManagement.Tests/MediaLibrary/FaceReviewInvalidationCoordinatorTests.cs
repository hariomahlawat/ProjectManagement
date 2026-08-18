using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceReviewInvalidationCoordinatorTests
{
    [Fact]
    public void Routine_grouping_change_does_not_requeue_candidate_matching()
    {
        var grouping = new FakeGroupingState();
        var queue = new FakeCandidateQueue();
        var coordinator = Create(grouping, queue);

        coordinator.NotifyGroupingChanged();

        Assert.Equal(1, grouping.InvalidationCount);
        Assert.Equal(0, queue.QueueFaceCount);
        Assert.Equal(0, queue.QueueFacesCount);
        Assert.Equal(0, queue.QueueAllCount);
        Assert.Empty(queue.QueuedFaces);
    }

    [Fact]
    public void Grouping_invalidation_is_a_noop_when_the_grouping_worker_is_not_operational()
    {
        var grouping = new FakeGroupingState();
        var queue = new FakeCandidateQueue();
        var coordinator = new FaceReviewInvalidationCoordinator(
            grouping,
            queue,
            Options.Create(new MediaLibraryOptions
            {
                Enabled = true,
                Catalogue = new MediaCatalogueOptions { Enabled = true },
                People = new MediaPeopleOptions
                {
                    Enabled = true,
                    WorkerEnabled = false,
                    GroupingEnabled = true
                }
            }),
            NullLogger<FaceReviewInvalidationCoordinator>.Instance);

        coordinator.NotifyGroupingChanged();

        Assert.Equal(0, grouping.InvalidationCount);
    }

    [Fact]
    public async Task Bounded_face_change_requeues_only_the_affected_faces()
    {
        var grouping = new FakeGroupingState();
        var queue = new FakeCandidateQueue();
        var coordinator = Create(grouping, queue);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await coordinator.NotifyFacesNeedRematchAsync(
            new[] { first, first, Guid.Empty, second },
            CancellationToken.None);

        Assert.Equal(1, grouping.InvalidationCount);
        Assert.Equal(new[] { first, second }, queue.QueuedFaces);
        Assert.Equal(1, queue.QueueFacesCount);
        Assert.Equal(0, queue.QueueFaceCount);
        Assert.Equal(0, queue.QueueAllCount);
    }

    [Fact]
    public async Task Trusted_reference_change_requeues_the_unresolved_corpus()
    {
        var grouping = new FakeGroupingState();
        var queue = new FakeCandidateQueue { QueueAllResult = 27 };
        var coordinator = Create(grouping, queue);

        await coordinator.NotifyReferenceEvidenceChangedAsync(CancellationToken.None);

        Assert.Equal(1, grouping.InvalidationCount);
        Assert.Equal(1, queue.QueueAllCount);
        Assert.Empty(queue.QueuedFaces);
    }

    [Fact]
    public async Task Explicit_force_rematch_returns_the_queued_count()
    {
        var grouping = new FakeGroupingState();
        var queue = new FakeCandidateQueue { QueueAllResult = 14 };
        var coordinator = Create(grouping, queue);

        var queued = await coordinator.ForceRequeueAllCandidatesAsync(CancellationToken.None);

        Assert.Equal(14, queued);
        Assert.Equal(1, grouping.InvalidationCount);
        Assert.Equal(1, queue.QueueAllCount);
    }

    private static FaceReviewInvalidationCoordinator Create(
        IFaceIdentityGroupingRuntimeState grouping,
        IFaceCandidateRefreshQueueService queue)
        => new(
            grouping,
            queue,
            Options.Create(new MediaLibraryOptions
            {
                Enabled = true,
                Catalogue = new MediaCatalogueOptions { Enabled = true },
                People = new MediaPeopleOptions
                {
                    Enabled = true,
                    WorkerEnabled = true,
                    GroupingEnabled = true
                }
            }),
            NullLogger<FaceReviewInvalidationCoordinator>.Instance);

    private sealed class FakeCandidateQueue : IFaceCandidateRefreshQueueService
    {
        public List<Guid> QueuedFaces { get; } = new();
        public int QueueFaceCount { get; private set; }
        public int QueueFacesCount { get; private set; }
        public int QueueAllCount { get; private set; }
        public int QueueAllResult { get; init; }

        public Task<bool> QueueFaceAsync(Guid faceId, CancellationToken cancellationToken)
        {
            QueueFaceCount++;
            QueuedFaces.Add(faceId);
            return Task.FromResult(true);
        }

        public Task<int> QueueFacesAsync(IReadOnlyCollection<Guid> faceIds, CancellationToken cancellationToken)
        {
            QueueFacesCount++;
            QueuedFaces.AddRange(faceIds);
            return Task.FromResult(faceIds.Count);
        }

        public Task<int> QueueAllUnassignedAsync(CancellationToken cancellationToken)
        {
            QueueAllCount++;
            return Task.FromResult(QueueAllResult);
        }
    }

    private sealed class FakeGroupingState : IFaceIdentityGroupingRuntimeState
    {
        public int InvalidationCount { get; private set; }
        public FaceIdentityGroupingRuntimeSnapshot GetSnapshot() => new(null, null, null);
        public void SetResult(FaceIdentityGroupingResult result, DateTimeOffset refreshedAtUtc, long? refreshGeneration = null) { }
        public void SetFailure(string failureReason, DateTimeOffset failedAtUtc) { }
        public void Invalidate() => InvalidationCount++;
        public Task WaitForRefreshRequestAsync(TimeSpan maximumDelay, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
