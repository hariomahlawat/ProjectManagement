using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Hosted;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// A job whose every attempt ends with an expired lease (for example an image that brings
/// the process down) was claimed again after each restart, indefinitely. The claim query is
/// PostgreSQL SQL (FOR UPDATE SKIP LOCKED), so this runs against the migrated schema.
/// </summary>
public sealed class MediaProcessingWorkerLeaseLossTests
{
    [PostgresMigrationFact]
    public async Task A_job_that_lost_its_lease_on_every_attempt_is_dead_lettered_not_rerun()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_leaseloss_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<MediaLibraryDbContext>(options => options.UseNpgsql(
                target,
                npgsql => npgsql.MigrationsHistoryTable(MediaLibraryDbContext.MigrationsHistoryTable)));
            await using var provider = services.BuildServiceProvider();

            long crashingJobId;
            long interruptedJobId;
            await using (var setup = provider.CreateScope().ServiceProvider.GetRequiredService<MediaLibraryDbContext>())
            {
                await setup.Database.MigrateAsync();
                var source = ReclassifySeed.Source();
                setup.Sources.Add(source);
                setup.Assets.AddRange(ReclassifySeed.Asset(source, 1, null), ReclassifySeed.Asset(source, 2, null));

                // Every attempt so far ended with the worker's lease expiring.
                var crashing = ReclassifySeed.Job(1, MediaProcessingJobType.AnalyseAsset, MediaProcessingJobStatus.Running,
                    "worker-dead", DateTimeOffset.UtcNow.AddHours(-1), attempts: 5);
                crashing.MaxAttempts = 5;
                // Interrupted by an orderly shutdown, which records its own outcome: keep running it.
                var interrupted = ReclassifySeed.Job(2, MediaProcessingJobType.AnalyseAsset, MediaProcessingJobStatus.Pending,
                    attempts: 5);
                interrupted.MaxAttempts = 5;
                interrupted.FailureCode = "WorkerStopping";
                interrupted.AvailableAfterUtc = crashing.AvailableAfterUtc.AddMinutes(1);
                setup.ProcessingJobs.AddRange(crashing, interrupted);
                await setup.SaveChangesAsync();
                crashingJobId = crashing.Id;
                interruptedJobId = interrupted.Id;
            }

            var worker = new MediaProcessingWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new MediaLibraryOptions()),
                new MediaProcessingRuntimeState(),
                NullLogger<MediaProcessingWorker>.Instance);

            Assert.Null(await worker.ClaimNextAsync(CancellationToken.None));
            var claimed = await worker.ClaimNextAsync(CancellationToken.None);

            Assert.NotNull(claimed);
            Assert.Equal(interruptedJobId, claimed!.Id);

            await using var check = provider.CreateScope().ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
            var crashingJob = await check.ProcessingJobs.AsNoTracking().SingleAsync(job => job.Id == crashingJobId);
            Assert.Equal(MediaProcessingJobStatus.DeadLetter, crashingJob.Status);
            Assert.Equal(MediaProcessingFailurePolicy.RepeatedWorkerLossCode, crashingJob.FailureCode);
            Assert.Null(crashingJob.LockedBy);
            Assert.DoesNotContain(crashingJob.FailureCode, MediaProcessingFailurePolicy.RecoverableFailureCodeNames);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
