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
    private static readonly Guid ReferenceFace = Guid.NewGuid();
    private static readonly Guid QueryFace = Guid.NewGuid();

    [Fact]
    public async Task Per_face_rejection_is_applied_only_when_requested()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = new FaceCandidateSearchService(
            db,
            new MediaAssetVisibilityPolicy(Options.Create(Options_())),
            Options.Create(Options_()));
        var input = new FaceCandidateSearchInput(QueryFace, 2, new[] { 1f, 0f, 0f, 0f }, "sface", "v1", 4);

        var faceSearch = await service.SearchBatchAsync(new[] { input }, CancellationToken.None);
        var groupSearch = await service.SearchBatchAsync(new[] { input }, excludeRejectedForInputFace: false, CancellationToken.None);

        Assert.DoesNotContain(faceSearch[QueryFace], candidate => candidate.PersonId == Alice);
        Assert.Contains(groupSearch[QueryFace], candidate => candidate.PersonId == Alice);
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

    private static MediaFace Face(Guid id, long assetId)
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
                    Embedding = new[] { 1f, 0f, 0f, 0f },
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
