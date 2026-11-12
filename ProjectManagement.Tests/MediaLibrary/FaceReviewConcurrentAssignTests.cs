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

    // The three corrections on a person's page take a face away from that person. They used
    // to record the audit entry and leave the face where it was: the query that loaded the
    // assignments was untracked, so marking them removed was never saved.
    [Fact]
    public async Task Returning_a_face_to_review_takes_it_away_from_the_person()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db);
        await service.AssignAsync(FaceId, Alice, "reviewer-a", null, CancellationToken.None);
        db.ChangeTracker.Clear();

        await service.ReturnAssignmentsToReviewAsync(Alice, new[] { FaceId }, "reviewer-b", "Not this person.", CancellationToken.None);
        db.ChangeTracker.Clear();

        var assignment = await db.PersonFaces.AsNoTracking().SingleAsync();
        Assert.NotNull(assignment.RemovedAtUtc);
        Assert.Equal("reviewer-b", assignment.RemovedByUserId);
        Assert.Equal("Not this person.", assignment.RemovalReason);
        Assert.Contains(await db.IdentityAudits.AsNoTracking().ToListAsync(), audit => audit.Action == "AssignmentRemoved");

        // The face is free again: it can be named as someone else.
        await service.AssignAsync(FaceId, Bob, "reviewer-b", null, CancellationToken.None);
        db.ChangeTracker.Clear();
        Assert.Equal(Bob, (await db.PersonFaces.AsNoTracking().SingleAsync(a => a.RemovedAtUtc == null)).MediaPersonId);
    }

    [Fact]
    public async Task Moving_a_face_to_another_person_leaves_it_with_that_person_only()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db);
        await service.AssignAsync(FaceId, Alice, "reviewer-a", null, CancellationToken.None);
        db.ChangeTracker.Clear();

        await service.MoveAssignmentsAsync(Alice, new[] { FaceId }, Bob, "reviewer-b", "Named as the wrong person.", CancellationToken.None);
        db.ChangeTracker.Clear();

        var active = Assert.Single(await db.PersonFaces.AsNoTracking().Where(a => a.RemovedAtUtc == null).ToListAsync());
        Assert.Equal(Bob, active.MediaPersonId);
        Assert.Contains(await db.PersonFaces.AsNoTracking().ToListAsync(), a => a.MediaPersonId == Alice && a.RemovedAtUtc != null);
    }

    [Fact]
    public async Task Splitting_a_face_into_a_new_person_leaves_it_with_the_new_person_only()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db);
        await service.AssignAsync(FaceId, Alice, "reviewer-a", null, CancellationToken.None);
        db.ChangeTracker.Clear();

        var carol = await service.SplitToNewPersonAsync(Alice, new[] { FaceId }, "Carol", "reviewer-b", "A different person.", CancellationToken.None);
        db.ChangeTracker.Clear();

        var active = Assert.Single(await db.PersonFaces.AsNoTracking().Where(a => a.RemovedAtUtc == null).ToListAsync());
        Assert.Equal(carol, active.MediaPersonId);
    }

    [Fact]
    public async Task Marking_a_detection_as_not_a_face_suppresses_it_and_frees_it_from_the_person()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db);
        await service.AssignAsync(FaceId, Alice, "reviewer-a", null, CancellationToken.None);
        db.ChangeTracker.Clear();

        await service.SuppressManyAsync(new[] { FaceId }, "reviewer-b", CancellationToken.None);
        db.ChangeTracker.Clear();

        Assert.True((await db.Faces.AsNoTracking().SingleAsync(face => face.Id == FaceId)).IsSuppressed);
        Assert.DoesNotContain(await db.PersonFaces.AsNoTracking().ToListAsync(), a => a.RemovedAtUtc == null);
    }

    [Fact]
    public async Task A_face_left_unnamed_can_be_brought_back_to_review()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db);

        await service.IgnoreManyAsync(new[] { FaceId }, "reviewer-a", CancellationToken.None);
        db.ChangeTracker.Clear();
        await service.ReopenUnidentifiedManyAsync(new[] { FaceId }, "reviewer-a", CancellationToken.None);
        db.ChangeTracker.Clear();

        var actions = (await db.IdentityAudits.AsNoTracking().ToListAsync()).Select(audit => audit.Action).ToList();
        Assert.Contains("FaceLeftUnidentified", actions);
        Assert.Contains("FaceUnidentifiedReopened", actions);
    }

    // Undo on the review page may only take back what a request itself assigned.
    [Fact]
    public async Task Only_the_reviewer_who_made_an_assignment_is_told_it_is_theirs()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await service.AssignAsync(FaceId, Alice, "reviewer-a", null, CancellationToken.None);
        db.ChangeTracker.Clear();

        // A second reviewer confirms the same person: nothing new is assigned.
        var secondStarted = DateTimeOffset.UtcNow.AddMilliseconds(1);
        await service.AssignAsync(FaceId, Alice, "reviewer-b", null, CancellationToken.None);
        db.ChangeTracker.Clear();

        Assert.Equal(new[] { FaceId }, await service.GetFacesAssignedByAsync(Alice, new[] { FaceId }, "reviewer-a", before, CancellationToken.None));
        Assert.Empty(await service.GetFacesAssignedByAsync(Alice, new[] { FaceId }, "reviewer-b", secondStarted, CancellationToken.None));
        Assert.Empty(await service.GetFacesAssignedByAsync(Alice, new[] { FaceId }, "reviewer-b", before, CancellationToken.None));
        Assert.Empty(await service.GetFacesAssignedByAsync(Bob, new[] { FaceId }, "reviewer-a", before, CancellationToken.None));
    }

    [Fact]
    public async Task Only_the_reviewer_who_left_a_face_unnamed_is_told_it_is_theirs()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await service.IgnoreManyAsync(new[] { FaceId }, "reviewer-a", CancellationToken.None);
        db.ChangeTracker.Clear();

        // A second reviewer leaves the same face unnamed: nothing new is recorded.
        await service.IgnoreManyAsync(new[] { FaceId }, "reviewer-b", CancellationToken.None);
        db.ChangeTracker.Clear();

        Assert.Equal(new[] { FaceId }, await service.GetFacesLeftUnnamedByAsync(new[] { FaceId }, "reviewer-a", before, CancellationToken.None));
        Assert.Empty(await service.GetFacesLeftUnnamedByAsync(new[] { FaceId }, "reviewer-b", before, CancellationToken.None));
        Assert.Empty(await service.GetFacesLeftUnnamedByAsync(new[] { FaceId }, "reviewer-a", DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task Excluding_an_appearance_that_is_not_a_reference_needs_no_other_reference()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db);
        await service.AssignAsync(FaceId, Alice, "reviewer-a", null, CancellationToken.None);
        db.ChangeTracker.Clear();

        // Alice has no trusted references. Excluding a non-reference appearance must not
        // demand one (the readiness service would throw if it were consulted).
        await service.SetReferenceStatusAsync(
            Alice, FaceId, FaceReferenceStatus.Excluded, "reviewer-a", "Blurred side view.", CancellationToken.None);
        db.ChangeTracker.Clear();

        var assignment = await db.PersonFaces.AsNoTracking().SingleAsync(a => a.RemovedAtUtc == null);
        Assert.Equal(FaceReferenceStatus.Excluded, assignment.ReferenceStatus);
    }

    [Fact]
    public async Task Rejecting_a_suggestion_records_the_decision_and_audit()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        db.FaceReviewDecisions.Add(new MediaFaceReviewDecision
        {
            MediaFaceId = FaceId,
            CandidatePersonId = Bob,
            Decision = FaceReviewDecisionType.Pending,
            ModelKey = "sface",
            ModelVersion = "1",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = CreateService(db);

        await service.RejectAsync(FaceId, Bob, "reviewer-a", CancellationToken.None);
        db.ChangeTracker.Clear();

        var decision = await db.FaceReviewDecisions.AsNoTracking().SingleAsync();
        Assert.Equal(FaceReviewDecisionType.Rejected, decision.Decision);
        Assert.Equal("reviewer-a", decision.DecidedByUserId);
        Assert.Contains(await db.IdentityAudits.AsNoTracking().ToListAsync(), audit => audit.Action == "CandidateRejected");

        // Rejecting a face someone has since assigned is refused inside the transaction.
        await service.AssignAsync(FaceId, Alice, "reviewer-b", null, CancellationToken.None);
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<FaceIdentityConflictException>(() =>
            service.RejectAsync(FaceId, Bob, "reviewer-a", CancellationToken.None));
    }

    [Fact]
    public async Task Merging_people_carries_rejections_over_to_the_merged_person()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        // A reviewer said this face is not Alice (the duplicate record), and Bob currently
        // has a pending suggestion for the same face.
        db.FaceReviewDecisions.Add(new MediaFaceReviewDecision
        {
            MediaFaceId = FaceId,
            CandidatePersonId = Alice,
            Decision = FaceReviewDecisionType.Rejected,
            ModelKey = "sface",
            ModelVersion = "1",
            DecidedByUserId = "reviewer-a",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        db.FaceReviewDecisions.Add(new MediaFaceReviewDecision
        {
            MediaFaceId = FaceId,
            CandidatePersonId = Bob,
            Decision = FaceReviewDecisionType.Pending,
            ModelKey = "sface",
            ModelVersion = "1",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await CreateService(db).MergePeopleAsync(Alice, Bob, "admin", "Duplicate record.", CancellationToken.None);
        db.ChangeTracker.Clear();

        var decisions = await db.FaceReviewDecisions.AsNoTracking().ToListAsync();
        var rejection = Assert.Single(decisions, decision => decision.Decision == FaceReviewDecisionType.Rejected);
        Assert.Equal(Bob, rejection.CandidatePersonId);
        Assert.DoesNotContain(decisions, decision => decision.Decision == FaceReviewDecisionType.Pending);
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
