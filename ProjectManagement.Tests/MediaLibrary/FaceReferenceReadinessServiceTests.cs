using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

public sealed class FaceReferenceReadinessServiceTests
{
    [Fact]
    public async Task MissingCurrentEmbedding_IsRepairable_ThenCurrentEmbeddingBecomesTrustable()
    {
        await using var db = CreateContext();
        var seeded = await SeedConfirmedFaceAsync(db, includeCurrentEmbedding: false);
        var service = CreateService(db);

        var missing = await service.GetAsync(seeded.PersonId, seeded.FaceId, CancellationToken.None);

        Assert.Equal(FaceReferenceReadinessCode.EmbeddingMissing, missing.Code);
        Assert.True(missing.CanPrepare);
        Assert.False(missing.CanTrust);

        db.FaceEmbeddings.Add(new MediaFaceEmbedding
        {
            MediaFaceId = seeded.FaceId,
            Embedding = new float[] { 1f, 0f, 0f, 0f },
            Dimension = 4,
            ModelKey = "sface",
            ModelVersion = "v1",
            Normalization = "L2",
            QualityScore = .9,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var ready = await service.GetAsync(seeded.PersonId, seeded.FaceId, CancellationToken.None);

        Assert.Equal(FaceReferenceReadinessCode.Eligible, ready.Code);
        Assert.True(ready.CanTrust);
        Assert.False(ready.CanPrepare);
    }


    [Fact]
    public async Task LegacyCropBoundaryState_IsRepairableAndExplicitlyCautionary()
    {
        await using var db = CreateContext();
        var seeded = await SeedConfirmedFaceAsync(db, includeCurrentEmbedding: false);
        var face = await db.Faces.SingleAsync(item => item.Id == seeded.FaceId);
        face.QualityScore = .89;
        face.QualityStatus = FaceQualityStatus.Occluded;
        face.QualitySignalsJson = JsonSerializer.Serialize(new FaceQualitySignals(
            Resolution: .95,
            Sharpness: .90,
            Exposure: .88,
            Contrast: .80,
            Pose: .92,
            CropCompleteness: .40,
            Reasons: new[] { "The detected face crop is close to the image boundary and may be incomplete." }));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var readiness = await service.GetAsync(seeded.PersonId, seeded.FaceId, CancellationToken.None);

        Assert.True(readiness.CanPrepare);
        Assert.False(readiness.CanTrust);
        Assert.Equal(FaceReferenceSuitability.UsableWithCaution, readiness.Suitability);
        Assert.Contains("crop", readiness.Message.ToLowerInvariant());
        Assert.DoesNotContain("occluded", readiness.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task CropIncomplete_WithCurrentEmbedding_CanBeTrustedOnlyWithCaution()
    {
        await using var db = CreateContext();
        var seeded = await SeedConfirmedFaceAsync(db, includeCurrentEmbedding: true);
        var face = await db.Faces.SingleAsync(item => item.Id == seeded.FaceId);
        face.QualityScore = .89;
        face.QualityStatus = FaceQualityStatus.CropIncomplete;
        face.QualitySignalsJson = JsonSerializer.Serialize(new FaceQualitySignals(
            Resolution: .95,
            Sharpness: .90,
            Exposure: .88,
            Contrast: .80,
            Pose: .92,
            CropCompleteness: .40,
            Reasons: Array.Empty<string>()));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var readiness = await service.GetAsync(seeded.PersonId, seeded.FaceId, CancellationToken.None);

        Assert.Equal(FaceReferenceReadinessCode.EligibleWithCaution, readiness.Code);
        Assert.Equal(FaceReferenceSuitability.UsableWithCaution, readiness.Suitability);
        Assert.True(readiness.CanTrust);
        Assert.True(readiness.RequiresCaution);
    }

    [Fact]
    public async Task SeverelyCroppedFace_IsNotReferenceUsableOrRepairable()
    {
        await using var db = CreateContext();
        var seeded = await SeedConfirmedFaceAsync(db, includeCurrentEmbedding: false);
        var face = await db.Faces.SingleAsync(item => item.Id == seeded.FaceId);
        face.QualityScore = .89;
        face.QualityStatus = FaceQualityStatus.SeverelyCropped;
        face.QualitySignalsJson = JsonSerializer.Serialize(new FaceQualitySignals(
            Resolution: .95,
            Sharpness: .90,
            Exposure: .88,
            Contrast: .80,
            Pose: .92,
            CropCompleteness: .05,
            Reasons: Array.Empty<string>()));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var readiness = await service.GetAsync(seeded.PersonId, seeded.FaceId, CancellationToken.None);

        Assert.Equal(FaceReferenceReadinessCode.CropTooIncomplete, readiness.Code);
        Assert.Equal(FaceReferenceSuitability.NotUsable, readiness.Suitability);
        Assert.False(readiness.CanTrust);
        Assert.False(readiness.CanPrepare);
    }

    [Fact]
    public async Task QueuePreparation_CreatesDurableEmbeddingJob_AndAudit()
    {
        await using var db = CreateContext();
        var seeded = await SeedConfirmedFaceAsync(db, includeCurrentEmbedding: false);
        var service = CreateService(db);

        var result = await service.QueuePreparationAsync(
            seeded.PersonId,
            seeded.FaceId,
            "reviewer",
            CancellationToken.None);

        Assert.True(result.IsPreparationPending);
        var job = Assert.Single(await db.ProcessingJobs.ToListAsync());
        Assert.Equal(MediaProcessingJobType.GenerateFaceEmbeddings, job.JobType);
        Assert.Equal(MediaProcessingJobStatus.Pending, job.Status);
        Assert.Contains(await db.IdentityAudits.ToListAsync(), audit =>
            audit.FaceId == seeded.FaceId && audit.Action == "ReferencePreparationQueued");
    }

    private static MediaLibraryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"face-reference-readiness-{Guid.NewGuid():N}")
            .Options;
        return new MediaLibraryDbContext(options);
    }

    private static FaceReferenceReadinessService CreateService(MediaLibraryDbContext db)
    {
        var options = CreateOptions();
        return new FaceReferenceReadinessService(
            db,
            new MediaAssetVisibilityPolicy(Options.Create(options)),
            new FaceEligibilityPolicy(Options.Create(options)),
            Options.Create(options));
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
                CandidateMinimumTrustedReferenceQuality = .65,
                Embedder = new FaceModelOptions
                {
                    Key = "sface",
                    Version = "v1",
                    EmbeddingDimension = 4
                }
            }
        };

    private static async Task<(Guid PersonId, Guid FaceId)> SeedConfirmedFaceAsync(
        MediaLibraryDbContext db,
        bool includeCurrentEmbedding)
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
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        if (includeCurrentEmbedding)
        {
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
        }
        var personId = Guid.NewGuid();
        var person = new MediaPerson
        {
            Id = personId,
            DisplayName = "Person",
            NormalizedName = "PERSON",
            Status = MediaPersonStatus.Confirmed,
            CreatedByUserId = "reviewer",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Sources.Add(source);
        db.Assets.Add(asset);
        db.Faces.Add(face);
        db.Persons.Add(person);
        db.PersonFaces.Add(new MediaPersonFace
        {
            MediaPersonId = personId,
            MediaFaceId = faceId,
            AssignmentType = FaceAssignmentType.HumanConfirmed,
            ReferenceStatus = FaceReferenceStatus.NotReference,
            AssignedByUserId = "reviewer",
            AssignedAtUtc = now
        });
        await db.SaveChangesAsync();
        return (personId, faceId);
    }
}
