using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// Queueing one face for a new match search loaded the face through a query that held an
/// untracked sub-query (the visible asset ids). EF Core applies AsNoTracking to the whole
/// query a sub-query is composed into, so the face was not tracked: the method reported
/// success and saved nothing. The same fault as the face corrections that were not saved.
/// </summary>
public sealed class FaceCandidateQueueSingleFaceTests
{
    [Fact]
    public async Task Queueing_one_face_marks_it_pending_and_saves_it()
    {
        var options = new MediaLibraryOptions
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true },
            People = new MediaPeopleOptions
            {
                Enabled = true,
                WorkerEnabled = true,
                CandidateSearchEnabled = true,
                CandidateMinimumFaceQuality = .5,
                Embedder = new FaceModelOptions { Key = "sface", Version = "v1", EmbeddingDimension = 4 }
            }
        };
        var faceId = Guid.NewGuid();
        var originalToken = Guid.NewGuid();
        await using var db = new MediaLibraryDbContext(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"face-queue-one-{Guid.NewGuid():N}")
            .Options);
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        db.Assets.Add(ReclassifySeed.Asset(source, 1, null));
        db.Faces.Add(new MediaFace
        {
            Id = faceId,
            MediaAssetId = 1,
            SequenceNumber = 1,
            QualityScore = .9,
            QualityStatus = FaceQualityStatus.EmbeddingEligible,
            DetectorModelKey = "yunet",
            DetectorModelVersion = "1",
            CandidateSearchStatus = FaceCandidateSearchStatus.Ready,
            CandidateSearchCompletedAtUtc = DateTimeOffset.UtcNow,
            ConcurrencyToken = originalToken,
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
                    CreatedAtUtc = DateTimeOffset.UtcNow
                }
            }
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var queue = new FaceCandidateRefreshQueueService(
            db,
            new MediaAssetVisibilityPolicy(Options.Create(options)),
            new FaceCandidateRefreshRuntimeState(),
            Options.Create(options));

        Assert.True(await queue.QueueFaceAsync(faceId, CancellationToken.None));
        db.ChangeTracker.Clear();

        var saved = await db.Faces.AsNoTracking().SingleAsync(face => face.Id == faceId);
        Assert.Equal(FaceCandidateSearchStatus.Pending, saved.CandidateSearchStatus);
        Assert.Null(saved.CandidateSearchCompletedAtUtc);
        // A search already running for this face must fail its save, not overwrite the request.
        Assert.NotEqual(originalToken, saved.ConcurrencyToken);
    }
}
