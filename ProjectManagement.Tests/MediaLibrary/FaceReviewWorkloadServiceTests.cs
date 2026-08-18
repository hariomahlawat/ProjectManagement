using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceReviewWorkloadServiceTests
{
    [Fact]
    public async Task Workload_keeps_matching_faces_unresolved_without_presenting_them_as_individual_review()
    {
        await using var db = CreateContext();
        var source = CreateSource();
        db.Sources.Add(source);

        var knownFace = AddFace(db, source, 1, FaceCandidateSearchStatus.Ready);
        var matchingFace = AddFace(db, source, 2, FaceCandidateSearchStatus.Pending);
        var individualFace = AddFace(db, source, 3, FaceCandidateSearchStatus.Ready);
        var failedFace = AddFace(db, source, 4, FaceCandidateSearchStatus.Failed);
        var closedFace = AddFace(db, source, 5, FaceCandidateSearchStatus.Ready);

        var person = new MediaPerson
        {
            Id = Guid.NewGuid(),
            DisplayName = "Confirmed Person",
            NormalizedName = "confirmed person",
            Status = MediaPersonStatus.Confirmed,
            CreatedByUserId = "test",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
        db.Persons.Add(person);
        db.FaceReviewDecisions.AddRange(
            new MediaFaceReviewDecision
            {
                MediaFaceId = knownFace.Id,
                CandidatePersonId = person.Id,
                Decision = FaceReviewDecisionType.Pending,
                ModelKey = "model",
                ModelVersion = "1",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ConcurrencyToken = Guid.NewGuid()
            },
            new MediaFaceReviewDecision
            {
                MediaFaceId = closedFace.Id,
                CandidatePersonId = null,
                Decision = FaceReviewDecisionType.Ignored,
                ModelKey = "model",
                ModelVersion = "1",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                DecidedAtUtc = DateTimeOffset.UtcNow,
                DecidedByUserId = "reviewer",
                ConcurrencyToken = Guid.NewGuid()
            });
        await db.SaveChangesAsync();

        var grouping = new FakeGroupingState(new FaceIdentityGroupingRuntimeSnapshot(
            new FaceIdentityGroupingResult(Array.Empty<FaceIdentityGroup>(), 2, 6, 3),
            DateTimeOffset.UtcNow,
            null));
        var visibility = new MediaAssetVisibilityPolicy(Options.Create(new MediaLibraryOptions
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true }
        }));
        var service = new FaceReviewWorkloadService(
            db,
            visibility,
            grouping,
            Options.Create(CreateOperationalOptions()));

        var result = await service.GetAsync(new FaceReviewWorkloadQuery(), CancellationToken.None);

        Assert.Equal(4, result.TotalUnresolvedCount);
        Assert.Equal(1, result.KnownMatchCount);
        Assert.Equal(1, result.MatchingCount);
        Assert.Equal(2, result.IndividualReviewCount);
        Assert.Equal(1, result.MatchingFailureCount);
        Assert.Equal(1, result.ClosedUnidentifiedCount);
        Assert.Equal(2, result.SuggestedGroupCount);
        Assert.Equal(6, result.GroupedAppearanceCount);
        Assert.Equal(3, result.UngroupedAppearanceCount);

        var scoped = await service.GetAsync(
            new FaceReviewWorkloadQuery(new[] { 1L, 2L }),
            CancellationToken.None);
        Assert.Equal(2, scoped.TotalUnresolvedCount);
        Assert.Equal(0, scoped.SuggestedGroupCount);
        Assert.Equal(0, scoped.GroupedAppearanceCount);
        Assert.Equal(0, scoped.UngroupedAppearanceCount);
        Assert.False(scoped.GroupingSnapshotAvailable);
        Assert.False(scoped.GroupingRefreshPending);

        // Hiding a person immediately removes stale suggestions from the actionable known-match
        // workload while the affected face is re-matched in the background.
        person.IsHidden = true;
        person.Status = MediaPersonStatus.Hidden;
        knownFace.CandidateSearchStatus = FaceCandidateSearchStatus.Pending;
        await db.SaveChangesAsync();

        var afterHide = await service.GetAsync(new FaceReviewWorkloadQuery(), CancellationToken.None);
        Assert.Equal(0, afterHide.KnownMatchCount);
        Assert.Equal(2, afterHide.MatchingCount);
        Assert.Equal(2, afterHide.IndividualReviewCount);
        Assert.Equal(4, afterHide.TotalUnresolvedCount);
    }

    private static MediaLibraryOptions CreateOperationalOptions()
        => new()
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true },
            People = new MediaPeopleOptions
            {
                Enabled = true,
                WorkerEnabled = true,
                GroupingEnabled = true
            }
        };

    private static MediaLibraryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"face-review-workload-{Guid.NewGuid():N}")
            .Options;
        return new MediaLibraryDbContext(options);
    }

    private static MediaLibrarySource CreateSource()
        => new()
        {
            Id = Guid.NewGuid(),
            Key = "prism",
            Name = "PRISM",
            SourceType = MediaLibrarySourceType.Prism,
            IsEnabled = true,
            IsVisibleInLibrary = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

    private static MediaFace AddFace(
        MediaLibraryDbContext db,
        MediaLibrarySource source,
        long assetId,
        FaceCandidateSearchStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var asset = new MediaAsset
        {
            Id = assetId,
            SourceId = source.Id,
            Source = source,
            Origin = MediaAssetOrigin.ProjectPhoto,
            Kind = MediaAssetKind.Photo,
            SourceEntityId = assetId.ToString(),
            OriginalFileName = $"{assetId}.jpg",
            ContentType = "image/jpeg",
            ContextKey = $"project:{assetId}",
            CollectionKey = $"project:{assetId}",
            ContextTitle = $"Project {assetId}",
            ContextSubtitle = "Project media",
            SourceLabel = "Project",
            Title = $"Photo {assetId}",
            MediaDateUtc = now,
            IndexedAtUtc = now,
            LastSeenAtUtc = now,
            LastSeenScanId = Guid.NewGuid(),
            IsAvailable = true,
            AvailabilityStatus = MediaAvailabilityStatus.Available
        };
        var face = new MediaFace
        {
            Id = Guid.NewGuid(),
            MediaAssetId = assetId,
            MediaAsset = asset,
            QualityScore = .8,
            QualityStatus = FaceQualityStatus.EmbeddingEligible,
            CandidateSearchStatus = status,
            DetectorModelKey = "detector",
            DetectorModelVersion = "1",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ConcurrencyToken = Guid.NewGuid()
        };
        asset.Faces.Add(face);
        db.Assets.Add(asset);
        db.Faces.Add(face);
        return face;
    }

    private sealed class FakeGroupingState : IFaceIdentityGroupingRuntimeState
    {
        private readonly FaceIdentityGroupingRuntimeSnapshot _snapshot;
        public FakeGroupingState(FaceIdentityGroupingRuntimeSnapshot snapshot) => _snapshot = snapshot;
        public FaceIdentityGroupingRuntimeSnapshot GetSnapshot() => _snapshot;
        public void SetResult(FaceIdentityGroupingResult result, DateTimeOffset refreshedAtUtc, long? refreshGeneration = null) { }
        public void SetFailure(string failureReason, DateTimeOffset failedAtUtc) { }
        public void Invalidate() { }
        public Task WaitForRefreshRequestAsync(TimeSpan maximumDelay, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
