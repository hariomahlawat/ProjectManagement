using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// Two reviewers on stale pages can act on the same unassigned face. The second assign
/// used to remove the first reviewer's assignment silently, leaving that person without
/// the face while their portrait still pointed at it.
/// </summary>
public sealed class FaceReviewConcurrentAssignTests
{
    private static readonly Guid FaceId = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    [Fact]
    public async Task Assigning_a_face_another_reviewer_already_assigned_is_a_conflict()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db);

        await service.AssignAsync(FaceId, Alice, "reviewer-a", null, CancellationToken.None);
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<FaceIdentityConflictException>(() =>
            service.AssignAsync(FaceId, Bob, "reviewer-b", null, CancellationToken.None));
        db.ChangeTracker.Clear();

        var active = Assert.Single(await db.PersonFaces.AsNoTracking()
            .Where(assignment => assignment.MediaFaceId == FaceId && assignment.RemovedAtUtc == null)
            .ToListAsync());
        Assert.Equal(Alice, active.MediaPersonId);
    }

    [Fact]
    public async Task Creating_a_person_from_an_already_assigned_face_is_a_conflict()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db);

        await service.AssignAsync(FaceId, Alice, "reviewer-a", null, CancellationToken.None);
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<FaceIdentityConflictException>(() =>
            service.CreatePersonAndAssignAsync(FaceId, "Carol", "reviewer-b", CancellationToken.None));
        db.ChangeTracker.Clear();

        Assert.DoesNotContain(await db.Persons.AsNoTracking().ToListAsync(), person => person.DisplayName == "Carol");
        Assert.Equal(Alice, (await db.PersonFaces.AsNoTracking().SingleAsync(a => a.RemovedAtUtc == null)).MediaPersonId);
    }

    [Fact]
    public async Task Repeating_the_same_assignment_is_harmless()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db);

        await service.AssignAsync(FaceId, Alice, "reviewer-a", null, CancellationToken.None);
        db.ChangeTracker.Clear();
        await service.AssignAsync(FaceId, Alice, "reviewer-b", null, CancellationToken.None);
        db.ChangeTracker.Clear();

        Assert.Single(await db.PersonFaces.AsNoTracking().ToListAsync());
    }

    private static FaceReviewService CreateService(MediaLibraryDbContext db)
    {
        var options = new MediaLibraryOptions
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true },
            People = new MediaPeopleOptions { Enabled = true }
        };
        return new FaceReviewService(
            db,
            new NoOpInvalidation(),
            new MediaAssetVisibilityPolicy(Options.Create(options)),
            new UnusedReadiness(),
            Options.Create(options),
            NullLogger<FaceReviewService>.Instance);
    }

    private static async Task SeedAsync(MediaLibraryDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var source = new MediaLibrarySource
        {
            Id = Guid.NewGuid(),
            Key = "prism",
            Name = "PRISM",
            SourceType = MediaLibrarySourceType.Prism,
            IsEnabled = true,
            IsVisibleInLibrary = true
        };
        var asset = new MediaAsset
        {
            Id = 1,
            SourceId = source.Id,
            Source = source,
            Origin = MediaAssetOrigin.ProjectPhoto,
            Kind = MediaAssetKind.Photo,
            IsAvailable = true,
            AvailabilityStatus = MediaAvailabilityStatus.Available,
            SourceEntityId = "photo:1",
            ContextKey = "project:1",
            CollectionKey = "project:1",
            ContextTitle = "Project",
            ContextSubtitle = "Project media",
            SourceLabel = "Project",
            Title = "Photo",
            OriginalFileName = "photo.jpg",
            ContentType = "image/jpeg",
            MediaDateUtc = now,
            IndexedAtUtc = now,
            LastSeenAtUtc = now
        };
        db.Sources.Add(source);
        db.Assets.Add(asset);
        db.Faces.Add(new MediaFace
        {
            Id = FaceId,
            MediaAssetId = asset.Id,
            SequenceNumber = 1,
            Left = .2,
            Top = .2,
            Width = .3,
            Height = .3,
            DetectionConfidence = .95,
            QualityScore = .9,
            QualityStatus = FaceQualityStatus.EmbeddingEligible,
            DetectorModelKey = "yunet",
            DetectorModelVersion = "1",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        foreach (var (id, name) in new[] { (Alice, "Alice"), (Bob, "Bob") })
        {
            db.Persons.Add(new MediaPerson
            {
                Id = id,
                DisplayName = name,
                NormalizedName = name.ToUpperInvariant(),
                Status = MediaPersonStatus.Confirmed,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        await db.SaveChangesAsync();
    }

    private static MediaLibraryDbContext CreateContext()
        => new(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"face-review-assign-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NoOpInvalidation : IFaceReviewInvalidationCoordinator
    {
        public void NotifyGroupingChanged() { }

        public Task NotifyFacesNeedRematchAsync(IReadOnlyCollection<Guid> faceIds, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task NotifyReferenceEvidenceChangedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> ForceRequeueAllCandidatesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class UnusedReadiness : IFaceReferenceReadinessService
    {
        public Task<FaceReferenceReadiness> GetAsync(Guid personId, Guid faceId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, FaceReferenceReadiness>> GetManyAsync(Guid personId, IReadOnlyCollection<Guid> faceIds, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<FaceReferenceReadiness> QueuePreparationAsync(Guid personId, Guid faceId, string userId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
