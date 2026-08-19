using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceCandidateZeroReferenceBootstrapTests
{
    [Fact]
    public async Task NoTrustedReferenceCorpus_CompletesCandidateSearchWithoutCallingSimilarityEngine()
    {
        await using var db = CreateContext();
        var options = CreateOptions();
        var faceId = await SeedUnassignedFaceAsync(db);
        var service = new FaceCandidateSuggestionService(
            db,
            new ThrowingCandidateSearchService(),
            new MediaAssetVisibilityPolicy(Options.Create(options)),
            Options.Create(options),
            NullLogger<FaceCandidateSuggestionService>.Instance);

        var processed = await service.RefreshUnassignedAsync(10, CancellationToken.None);

        Assert.Equal(1, processed);
        var face = await db.Faces.SingleAsync(item => item.Id == faceId);
        Assert.Equal(FaceCandidateSearchStatus.Ready, face.CandidateSearchStatus);
        Assert.NotNull(face.CandidateSearchCompletedAtUtc);
        Assert.Empty(await db.FaceReviewDecisions.ToListAsync());
    }

    private static MediaLibraryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"zero-reference-bootstrap-{Guid.NewGuid():N}")
            .Options;
        return new MediaLibraryDbContext(options);
    }

    private static MediaLibraryOptions CreateOptions()
        => new()
        {
            Enabled = true,
            Catalogue = new MediaCatalogueOptions { Enabled = true },
            People = new MediaPeopleOptions
            {
                Enabled = true,
                WorkerEnabled = true,
                ProcessPhotographsOnly = false,
                CandidateSearchEnabled = true,
                CandidateMinimumFaceQuality = .55,
                CandidateMinimumTrustedReferenceQuality = .65,
                Embedder = new FaceModelOptions
                {
                    Key = "sface",
                    Version = "v1",
                    EmbeddingDimension = 4
                }
            }
        };

    private static async Task<Guid> SeedUnassignedFaceAsync(MediaLibraryDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var source = new MediaLibrarySource
        {
            Id = Guid.NewGuid(),
            Key = "test",
            Name = "Test",
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
            ContextTitle = "Test",
            ContextSubtitle = "Project media",
            SourceLabel = "Project",
            Title = "Photo",
            OriginalFileName = "photo.jpg",
            ContentType = "image/jpeg",
            MediaDateUtc = now,
            IndexedAtUtc = now,
            LastSeenAtUtc = now
        };
        var faceId = Guid.NewGuid();
        var face = new MediaFace
        {
            Id = faceId,
            MediaAsset = asset,
            MediaAssetId = asset.Id,
            SequenceNumber = 1,
            Left = .2,
            Top = .2,
            Width = .3,
            Height = .3,
            DetectionConfidence = .95,
            QualityScore = .9,
            QualityStatus = FaceQualityStatus.EmbeddingEligible,
            DetectorModelKey = "detector",
            DetectorModelVersion = "v1",
            CandidateSearchStatus = FaceCandidateSearchStatus.Pending,
            CandidateSearchModelKey = "sface",
            CandidateSearchModelVersion = "v1",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        face.Embeddings.Add(new MediaFaceEmbedding
        {
            MediaFaceId = faceId,
            Embedding = new float[] { 1f, 0f, 0f, 0f },
            Dimension = 4,
            ModelKey = "sface",
            ModelVersion = "v1",
            Normalization = "L2",
            QualityScore = .9,
            CreatedAtUtc = now
        });
        db.Sources.Add(source);
        db.Assets.Add(asset);
        db.Faces.Add(face);
        await db.SaveChangesAsync();
        return faceId;
    }

    private sealed class ThrowingCandidateSearchService : IFaceCandidateSearchService
    {
        public Task<IReadOnlyList<FaceCandidate>> SearchAsync(
            Guid faceId,
            float[] embedding,
            string modelKey,
            string modelVersion,
            int dimension,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Candidate search must not execute when no trusted-reference corpus exists.");

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<FaceCandidate>>> SearchBatchAsync(
            IReadOnlyCollection<FaceCandidateSearchInput> inputs,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Candidate search must not execute when no trusted-reference corpus exists.");
    }
}
