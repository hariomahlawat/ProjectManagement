using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Tests.Fakes;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// Re-analysis (for example after a model upgrade) replaces every face on a photograph
/// without a confirmed person. Human triage on those faces must survive it.
/// </summary>
public sealed class FaceReanalysisTriageCarryOverTests
{
    private const long AssetId = 1;
    private static readonly Guid PersonId = Guid.NewGuid();
    private static readonly Guid OtherPersonId = Guid.NewGuid();
    private static readonly Guid SuppressedFace = Guid.NewGuid();
    private static readonly Guid RejectedFace = Guid.NewGuid();
    private static readonly Guid UnidentifiedFace = Guid.NewGuid();
    private static readonly Guid VanishedFace = Guid.NewGuid();

    [Fact]
    public async Task Reanalysis_moves_human_triage_to_the_matching_new_faces()
    {
        await using var db = CreateContext();
        await AssertTriageCarriedOverAsync(db);
    }

    /// <summary>
    /// The in-memory provider does not enforce the unique (MediaAssetId, SequenceNumber)
    /// constraint or order commands, so the same scenario runs against the migrated schema.
    /// </summary>
    [PostgresMigrationFact]
    public async Task Reanalysis_moves_human_triage_on_postgresql()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_triage_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            await using var db = new MediaLibraryDbContext(new DbContextOptionsBuilder<MediaLibraryDbContext>()
                .UseNpgsql(target, npgsql => npgsql.MigrationsHistoryTable(MediaLibraryDbContext.MigrationsHistoryTable))
                .Options);
            await db.Database.MigrateAsync();
            await AssertTriageCarriedOverAsync(db);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static async Task AssertTriageCarriedOverAsync(MediaLibraryDbContext db)
    {
        await SeedAsync(db);
        var service = CreateService(db, new[]
        {
            Detection(0.11, 0.10, 0.20, 0.20), // same face as SuppressedFace
            Detection(0.50, 0.51, 0.20, 0.20), // same face as RejectedFace
            Detection(0.80, 0.10, 0.10, 0.10)  // same face as UnidentifiedFace
        });

        await service.ProcessAssetAsync(AssetId, CancellationToken.None);
        db.ChangeTracker.Clear();

        var faces = await db.Faces.AsNoTracking().Where(face => face.MediaAssetId == AssetId).ToListAsync();
        Assert.Equal(3, faces.Count);
        Assert.Equal(new[] { 1, 2, 3 }, faces.Select(face => face.SequenceNumber).OrderBy(n => n).ToArray());
        Assert.DoesNotContain(faces, face => face.Id == SuppressedFace || face.Id == RejectedFace
                                             || face.Id == UnidentifiedFace || face.Id == VanishedFace);

        var newSuppressed = faces.Single(face => face.Left < 0.3);
        Assert.True(newSuppressed.IsSuppressed);
        Assert.Equal("reviewer", newSuppressed.SuppressedByUserId);
        Assert.Equal(FaceCandidateSearchStatus.NotRequested, newSuppressed.CandidateSearchStatus);

        var newRejected = faces.Single(face => face.Left is > 0.4 and < 0.6);
        var decisions = await db.FaceReviewDecisions.AsNoTracking().ToListAsync();
        var rejection = Assert.Single(decisions, decision => decision.Decision == FaceReviewDecisionType.Rejected);
        Assert.Equal(newRejected.Id, rejection.MediaFaceId);
        Assert.Equal(PersonId, rejection.CandidatePersonId);
        // Machine suggestions are recomputed, not carried.
        Assert.DoesNotContain(decisions, decision => decision.Decision == FaceReviewDecisionType.Pending);

        var history = Assert.Single(await db.PersonFaces.AsNoTracking().ToListAsync());
        Assert.Equal(newRejected.Id, history.MediaFaceId);
        Assert.NotNull(history.RemovedAtUtc);

        var newUnidentified = faces.Single(face => face.Left > 0.7);
        var leftUnidentified = Assert.Single(decisions, decision => decision.Decision == FaceReviewDecisionType.Ignored);
        Assert.Equal(newUnidentified.Id, leftUnidentified.MediaFaceId);
        Assert.Equal(FaceCandidateSearchStatus.Ready, newUnidentified.CandidateSearchStatus);

        // The face that is no longer detected takes only its own decision with it.
        Assert.DoesNotContain(decisions, decision => decision.Notes == "vanished");
    }

    [Fact]
    public async Task Reanalysis_does_not_carry_triage_to_a_face_that_does_not_overlap()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, new[] { Detection(0.30, 0.70, 0.10, 0.10) });

        await service.ProcessAssetAsync(AssetId, CancellationToken.None);

        var face = await db.Faces.AsNoTracking().SingleAsync();
        Assert.False(face.IsSuppressed);
        Assert.Empty(await db.FaceReviewDecisions.AsNoTracking().ToListAsync());
    }

    private static DetectedFaceData Detection(double left, double top, double width, double height)
        => new(left, top, width, height, 0.99, 0.9, FaceQualityStatus.Detected, null, null, null);

    private static async Task SeedAsync(MediaLibraryDbContext db)
    {
        var source = new MediaLibrarySource
        {
            Id = Guid.NewGuid(),
            Key = "prism",
            Name = "PRISM",
            SourceType = MediaLibrarySourceType.Prism,
            IsEnabled = true,
            IsVisibleInLibrary = true
        };
        db.Sources.Add(source);
        db.Assets.Add(new MediaAsset
        {
            Id = AssetId,
            SourceId = source.Id,
            Source = source,
            Origin = MediaAssetOrigin.ProjectPhoto,
            Kind = MediaAssetKind.Photo,
            IsAvailable = true,
            AvailabilityStatus = MediaAvailabilityStatus.Available,
            SourceEntityId = "project-photo:1",
            ContextKey = "project:1",
            CollectionKey = "project:1",
            ContextTitle = "Project",
            ContextSubtitle = "Project media",
            SourceLabel = "Project",
            Title = "Photo",
            OriginalFileName = "photo.jpg",
            ContentType = "image/jpeg",
            MediaDateUtc = DateTimeOffset.UtcNow,
            IndexedAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow,
            FaceAnalysisVersion = "old-model"
        });
        db.Persons.Add(new MediaPerson
        {
            Id = PersonId,
            DisplayName = "Alice",
            NormalizedName = "ALICE",
            Status = MediaPersonStatus.Confirmed
        });
        db.Persons.Add(new MediaPerson
        {
            Id = OtherPersonId,
            DisplayName = "Bob",
            NormalizedName = "BOB",
            Status = MediaPersonStatus.Confirmed
        });

        db.Faces.Add(OldFace(SuppressedFace, 1, 0.10, 0.10, 0.20, 0.20, suppressed: true));
        db.Faces.Add(OldFace(RejectedFace, 2, 0.50, 0.50, 0.20, 0.20));
        db.Faces.Add(OldFace(UnidentifiedFace, 3, 0.80, 0.10, 0.10, 0.10));
        db.Faces.Add(OldFace(VanishedFace, 4, 0.10, 0.70, 0.10, 0.10));

        db.FaceReviewDecisions.Add(Decision(RejectedFace, PersonId, FaceReviewDecisionType.Rejected));
        db.FaceReviewDecisions.Add(Decision(RejectedFace, OtherPersonId, FaceReviewDecisionType.Pending));
        db.FaceReviewDecisions.Add(Decision(UnidentifiedFace, null, FaceReviewDecisionType.Ignored));
        var vanished = Decision(VanishedFace, PersonId, FaceReviewDecisionType.Rejected);
        vanished.Notes = "vanished";
        db.FaceReviewDecisions.Add(vanished);

        db.PersonFaces.Add(new MediaPersonFace
        {
            MediaFaceId = RejectedFace,
            MediaPersonId = PersonId,
            AssignmentType = FaceAssignmentType.HumanConfirmed,
            AssignedByUserId = "reviewer",
            AssignedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            RemovedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            RemovedByUserId = "reviewer"
        });

        await db.SaveChangesAsync();
    }

    private static MediaFace OldFace(Guid id, int sequence, double left, double top, double width, double height, bool suppressed = false)
        => new()
        {
            Id = id,
            MediaAssetId = AssetId,
            SequenceNumber = sequence,
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            DetectorModelKey = "old",
            DetectorModelVersion = "1",
            IsSuppressed = suppressed,
            SuppressedByUserId = suppressed ? "reviewer" : null,
            SuppressedAtUtc = suppressed ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-3),
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddDays(-3)
        };

    private static MediaFaceReviewDecision Decision(Guid faceId, Guid? personId, FaceReviewDecisionType type)
        => new()
        {
            MediaFaceId = faceId,
            CandidatePersonId = personId,
            Decision = type,
            ModelKey = "old",
            ModelVersion = "1",
            DecidedByUserId = type == FaceReviewDecisionType.Pending ? null : "reviewer",
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
        };

    private static FaceIntelligenceService CreateService(MediaLibraryDbContext db, IReadOnlyList<DetectedFaceData> detections)
    {
        var options = new MediaLibraryOptions();
        options.People.Enabled = true;
        return new FaceIntelligenceService(
            db,
            new BytesResolver(new byte[] { 1, 2, 3, 4 }),
            new FixedEngine(detections),
            new AlwaysEligible(),
            new UnusedContentInvalidation(),
            new UnusedReviewInvalidation(),
            Options.Create(options),
            new TestWebHostEnvironment { ContentRootPath = Path.GetTempPath() },
            NullLogger<FaceIntelligenceService>.Instance);
    }

    private static MediaLibraryDbContext CreateContext()
        => new(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"face-reanalysis-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class BytesResolver(byte[] bytes) : IMediaContentProviderResolver
    {
        public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
            => Task.FromResult<MediaContentDescriptor?>(new(
                "photo.jpg", "image/jpeg", bytes.Length, null,
                _ => Task.FromResult<Stream>(new MemoryStream(bytes))));
    }

    private sealed class FixedEngine(IReadOnlyList<DetectedFaceData> detections) : IFaceAnalysisEngine
    {
        public Task<IReadOnlyList<DetectedFaceData>> AnalyseAsync(byte[] imageBytes, CancellationToken cancellationToken)
            => Task.FromResult(detections);
    }

    private sealed class AlwaysEligible : IFaceEligibilityPolicy
    {
        public FaceEligibilityDecision Evaluate(MediaAsset asset) => new(true, "OK", "Eligible");

        public Expression<Func<MediaAsset, bool>> BuildEligiblePredicate() => _ => true;
    }

    private sealed class UnusedContentInvalidation : IMediaContentChangeInvalidationService
    {
        public MediaContentChangeSnapshot ResetAsset(MediaAsset asset, string newFingerprint, MediaAssetKind newKind, bool classificationEnabled)
            => throw new NotSupportedException();

        public Task RetireDerivedIntelligenceAsync(IReadOnlyCollection<MediaContentChangeSnapshot> changes, DateTimeOffset changedAtUtc, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task RetireFaceIntelligenceAsync(IReadOnlyCollection<long> assetIds, string action, string performedByUserId, string notes, DateTimeOffset changedAtUtc, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class UnusedReviewInvalidation : IFaceReviewInvalidationCoordinator
    {
        public void NotifyGroupingChanged() { }

        public Task NotifyFacesNeedRematchAsync(IReadOnlyCollection<Guid> faceIds, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task NotifyReferenceEvidenceChangedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> ForceRequeueAllCandidatesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
