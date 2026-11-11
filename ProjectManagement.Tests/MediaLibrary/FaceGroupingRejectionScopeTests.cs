using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// Identity grouping searches with a group centroid on behalf of every member and applies
/// rejections at group level. The search therefore must be able to skip the per-face
/// rejection filter; otherwise a rejection on the representative face alone hid the person
/// for the whole group.
/// </summary>
public sealed class FaceGroupingRejectionScopeTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid BobReferenceFace = Guid.NewGuid();
    private static readonly Guid ReferenceFace = Guid.NewGuid();
    private static readonly Guid QueryFace = Guid.NewGuid();

    [Fact]
    public async Task Caller_exclusions_replace_the_input_faces_own_rejections()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, candidateLimit: 5);
        var input = Input();

        var faceSearch = await service.SearchBatchAsync(new[] { input }, CancellationToken.None);
        var groupSearch = await service.SearchBatchAsync(
            new[] { input },
            new Dictionary<Guid, IReadOnlySet<Guid>>(),
            CancellationToken.None);

        // The representative's own rejection hides Alice for a single-face search only.
        Assert.DoesNotContain(faceSearch[QueryFace], candidate => candidate.PersonId == Alice);
        Assert.Contains(groupSearch[QueryFace], candidate => candidate.PersonId == Alice);
    }

    [Fact]
    public async Task A_group_wide_exclusion_does_not_take_a_limited_result_slot()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, candidateLimit: 1);

        // Alice ranks first, but every group member rejected her. With one result slot,
        // Bob must still be suggested: the exclusion applies before the limit.
        var result = await service.SearchBatchAsync(
            new[] { Input() },
            new Dictionary<Guid, IReadOnlySet<Guid>> { [QueryFace] = new HashSet<Guid> { Alice } },
            CancellationToken.None);

        var only = Assert.Single(result[QueryFace]);
        Assert.Equal(Bob, only.PersonId);
    }

    private static FaceCandidateSearchInput Input()
        => new(QueryFace, 2, new[] { 1f, 0f, 0f, 0f }, "sface", "v1", 4);

    private static FaceCandidateSearchService CreateService(MediaLibraryDbContext db, int candidateLimit)
    {
        var options = Options_();
        options.People.CandidateLimit = candidateLimit;
        return new FaceCandidateSearchService(
            db,
            new MediaAssetVisibilityPolicy(Options.Create(options)),
            Options.Create(options));
    }

    private static MediaLibraryOptions Options_()
        => new()
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true },
            People = new MediaPeopleOptions
            {
                Enabled = true,
                CandidateSearchEnabled = true,
                CandidateMinimumTrustedReferenceQuality = .5,
                Embedder = new FaceModelOptions { Key = "sface", Version = "v1", EmbeddingDimension = 4 }
            }
        };

    private static async Task SeedAsync(MediaLibraryDbContext db)
    {
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        db.Assets.Add(ReclassifySeed.Asset(source, 1, null));
        db.Assets.Add(ReclassifySeed.Asset(source, 2, null));
        db.Persons.Add(new MediaPerson
        {
            Id = Alice,
            DisplayName = "Alice",
            NormalizedName = "ALICE",
            Status = MediaPersonStatus.Confirmed,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        db.Assets.Add(ReclassifySeed.Asset(source, 3, null));
        db.Persons.Add(new MediaPerson
        {
            Id = Bob,
            DisplayName = "Bob",
            NormalizedName = "BOB",
            Status = MediaPersonStatus.Confirmed,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        db.Faces.Add(Face(BobReferenceFace, 3, new[] { 0.98f, 0.199f, 0f, 0f }));
        db.PersonFaces.Add(new MediaPersonFace
        {
            MediaFaceId = BobReferenceFace,
            MediaPersonId = Bob,
            AssignmentType = FaceAssignmentType.HumanConfirmed,
            ReferenceStatus = FaceReferenceStatus.TrustedReference,
            AssignedByUserId = "reviewer",
            AssignedAtUtc = DateTimeOffset.UtcNow
        });
        db.Faces.Add(Face(ReferenceFace, 1));
        db.Faces.Add(Face(QueryFace, 2));
        db.PersonFaces.Add(new MediaPersonFace
        {
            MediaFaceId = ReferenceFace,
            MediaPersonId = Alice,
            AssignmentType = FaceAssignmentType.HumanConfirmed,
            ReferenceStatus = FaceReferenceStatus.TrustedReference,
            AssignedByUserId = "reviewer",
            AssignedAtUtc = DateTimeOffset.UtcNow
        });
        db.FaceReviewDecisions.Add(new MediaFaceReviewDecision
        {
            MediaFaceId = QueryFace,
            CandidatePersonId = Alice,
            Decision = FaceReviewDecisionType.Rejected,
            ModelKey = "sface",
            ModelVersion = "v1",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static MediaFace Face(Guid id, long assetId, float[]? embedding = null)
        => new()
        {
            Id = id,
            MediaAssetId = assetId,
            SequenceNumber = 1,
            QualityScore = .95,
            QualityStatus = FaceQualityStatus.EmbeddingEligible,
            DetectorModelKey = "yunet",
            DetectorModelVersion = "1",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Embeddings =
            {
                new MediaFaceEmbedding
                {
                    Embedding = embedding ?? new[] { 1f, 0f, 0f, 0f },
                    Dimension = 4,
                    ModelKey = "sface",
                    ModelVersion = "v1",
                    QualityScore = .95,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                }
            }
        };

    private static MediaLibraryDbContext CreateContext()
        => new(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"grouping-rejections-{Guid.NewGuid():N}")
            .Options);
}
