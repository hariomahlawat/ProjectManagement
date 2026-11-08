using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Admin;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// Fourth admin review, PR 3 (C2): "Sort older images again" used to un-claim a job a worker had
/// claimed while the request was running, so two workers could sort the same image at once. The
/// service now locks the job rows before deciding. The test creates, migrates and drops its own
/// prism_test_reclassify_* database on the PRISM_TEST_POSTGRES_CONNECTION server.
/// </summary>
public sealed class MediaReclassificationPostgresIntegrationTests
{
    [PostgresMigrationFact]
    public async Task AJobClaimedMidRequest_IsNotReset_AndALockedJobCannotBeClaimed()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_reclassify_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            var now = DateTimeOffset.UtcNow;
            long claimedJobId;
            long expiredJobId;
            await using (var setup = Create(target))
            {
                await setup.Database.MigrateAsync();
                var source = ReclassifySeed.Source();
                setup.Sources.Add(source);
                setup.Assets.AddRange(
                    ReclassifySeed.Asset(source, 1, "hybrid-media-v6"),
                    ReclassifySeed.Asset(source, 2, "hybrid-media-v6"),
                    ReclassifySeed.Asset(source, 3, "hybrid-media-v6"));
                // Asset 1: waiting after a failure when the request starts; a worker claims it
                // mid-request. Asset 2: a worker's lease ran out, so a worker may reclaim it.
                var claimed = ReclassifySeed.Job(1, MediaProcessingJobType.ClassifyMedia, MediaProcessingJobStatus.Failed);
                var expired = ReclassifySeed.Job(2, MediaProcessingJobType.ClassifyMedia, MediaProcessingJobStatus.Running, "worker-old", now.AddHours(-1));
                setup.ProcessingJobs.AddRange(claimed, expired);
                await setup.SaveChangesAsync();
                claimedJobId = claimed.Id;
                expiredJobId = expired.Id;
            }

            var interceptor = new WorkerAtTheLock(target, claimedJobId);
            await using (var db = Create(target, interceptor))
            {
                var service = new MediaReclassificationService(
                    db,
                    new RecordingInvalidation(),
                    new CapturingAudit(),
                    FakeClock.AtUtc(now),
                    NullLogger<MediaReclassificationService>.Instance);

                var result = await service.QueueStaleAsync("admin-1", CancellationToken.None);

                Assert.Equal(new MediaReclassifyStaleResult(MediaReclassifyStaleOutcome.Queued, 2, 1, false), result);
            }

            Assert.True(interceptor.ClaimedBeforeLock);
            // While the request held the lock, a worker's SKIP LOCKED claim did not get the
            // expired job it was about to reset.
            Assert.NotNull(interceptor.ClaimableDuringLock);
            Assert.DoesNotContain(expiredJobId, interceptor.ClaimableDuringLock!);

            await using var check = Create(target);
            var claimedJob = await check.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == claimedJobId);
            Assert.Equal(MediaProcessingJobStatus.Running, claimedJob.Status);
            Assert.Equal("worker-new", claimedJob.LockedBy);
            Assert.Equal("hybrid-media-v6", (await check.Assets.AsNoTracking().SingleAsync(a => a.Id == 1)).ClassifierVersion);

            var expiredJob = await check.ProcessingJobs.AsNoTracking().SingleAsync(j => j.Id == expiredJobId);
            Assert.Equal(MediaProcessingJobStatus.Pending, expiredJob.Status);
            Assert.Null(expiredJob.LockedBy);
            Assert.Single(await check.ProcessingJobs.AsNoTracking()
                .Where(j => j.MediaAssetId == 3 && j.Status == MediaProcessingJobStatus.Pending)
                .ToListAsync());
            Assert.Equal(new long[] { 2, 3 }, await check.ClassificationAudits.AsNoTracking()
                .OrderBy(a => a.MediaAssetId)
                .Select(a => a.MediaAssetId)
                .ToListAsync());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static MediaLibraryDbContext Create(string connectionString, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(MediaLibraryDbContext.MigrationsHistoryTable));
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new MediaLibraryDbContext(builder.Options);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Plays a worker on its own connection: just before the request locks the job rows, it
    /// claims job <c>claimJobId</c> (as MediaProcessingWorker.ClaimNextAsync does, committed);
    /// just after, it runs the worker's FOR UPDATE SKIP LOCKED claim query and records what it
    /// could have claimed.
    /// </summary>
    private sealed class WorkerAtTheLock : DbCommandInterceptor
    {
        private readonly string _connectionString;
        private readonly long _claimJobId;

        public WorkerAtTheLock(string connectionString, long claimJobId)
        {
            _connectionString = connectionString;
            _claimJobId = claimJobId;
        }

        public bool ClaimedBeforeLock { get; private set; }

        public List<long>? ClaimableDuringLock { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsTheLock(command) && !ClaimedBeforeLock)
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var claim = new NpgsqlCommand("""
                    UPDATE "MediaProcessingJobs"
                    SET "Status" = 'Running', "LockedBy" = 'worker-new', "AttemptCount" = "AttemptCount" + 1,
                        "StartedAtUtc" = now(), "LockExpiresAtUtc" = now() + interval '15 minutes'
                    WHERE "Id" = @id
                    """, connection);
                claim.Parameters.AddWithValue("id", _claimJobId);
                Assert.Equal(1, await claim.ExecuteNonQueryAsync(cancellationToken));
                ClaimedBeforeLock = true;
            }

            return result;
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (IsTheLock(command) && ClaimableDuringLock is null)
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await using var probe = new NpgsqlCommand("""
                    SELECT "Id"
                    FROM "MediaProcessingJobs"
                    WHERE "Status" = 'Pending'
                       OR ("Status" = 'Running' AND "LockExpiresAtUtc" IS NOT NULL AND "LockExpiresAtUtc" < now())
                    FOR UPDATE SKIP LOCKED
                    """, connection, transaction);
                var ids = new List<long>();
                await using (var reader = await probe.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        ids.Add(reader.GetInt64(0));
                    }
                }

                await transaction.RollbackAsync(cancellationToken);
                ClaimableDuringLock = ids;
            }

            return result;
        }

        private static bool IsTheLock(DbCommand command)
            => command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
               && command.CommandText.Contains("MediaProcessingJobs", StringComparison.Ordinal);
    }
}
