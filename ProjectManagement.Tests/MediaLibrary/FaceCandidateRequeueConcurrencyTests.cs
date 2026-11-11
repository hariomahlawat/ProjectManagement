using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// A requeue that lands while the candidate worker is searching used to be overwritten:
/// the worker saved Ready with candidates computed against the evidence the requeue was
/// meant to replace. The requeue now rotates the face's concurrency token, so the stale
/// save fails and the face stays Pending.
/// </summary>
public sealed class FaceCandidateRequeueConcurrencyTests
{
    [PostgresMigrationFact]
    public async Task A_requeue_during_a_search_makes_the_stale_save_fail()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_requeue_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
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
            await using (var setup = Create(target))
            {
                await setup.Database.MigrateAsync();
                var source = ReclassifySeed.Source();
                setup.Sources.Add(source);
                setup.Assets.Add(ReclassifySeed.Asset(source, 1, null));
                setup.Faces.Add(new MediaFace
                {
                    Id = faceId,
                    MediaAssetId = 1,
                    SequenceNumber = 1,
                    QualityScore = .9,
                    QualityStatus = FaceQualityStatus.EmbeddingEligible,
                    DetectorModelKey = "yunet",
                    DetectorModelVersion = "1",
                    CandidateSearchStatus = FaceCandidateSearchStatus.Processing,
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
                await setup.SaveChangesAsync();
            }

            // The worker loaded the face before the requeue.
            await using var worker = Create(target);
            var inFlight = await worker.Faces.SingleAsync(face => face.Id == faceId);

            await using (var reviewer = Create(target))
            {
                var queue = new FaceCandidateRefreshQueueService(
                    reviewer,
                    new MediaAssetVisibilityPolicy(Options.Create(options)),
                    new FaceCandidateRefreshRuntimeState(),
                    Options.Create(options));
                Assert.True(await queue.QueueFaceAsync(faceId, CancellationToken.None));
                // The bulk path (reference evidence changed) must rotate the token too.
                Assert.Equal(1, await queue.QueueAllUnassignedAsync(CancellationToken.None));
            }

            inFlight.CandidateSearchStatus = FaceCandidateSearchStatus.Ready;
            inFlight.ConcurrencyToken = Guid.NewGuid();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => worker.SaveChangesAsync());

            await using var check = Create(target);
            var stored = await check.Faces.AsNoTracking().SingleAsync(face => face.Id == faceId);
            Assert.Equal(FaceCandidateSearchStatus.Pending, stored.CandidateSearchStatus);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static MediaLibraryDbContext Create(string connectionString)
        => new(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MediaLibraryDbContext.MigrationsHistoryTable))
            .Options);

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
