using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Hosted;
using ProjectManagement.Features.MediaLibrary.Outbox;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// The PRISM media outbox worker against PostgreSQL:
/// <list type="bullet">
/// <item>While the media schema is not operational the worker must hold events, not claim
/// them, fail them ten times and dead-letter them.</item>
/// <item>Lease renewal must translate to SQL. It used <c>DateTimeOffset.UtcNow.Add</c> inside
/// <c>SetProperty</c>, which threw on every renewal.</item>
/// </list>
/// </summary>
public sealed class PrismMediaOutboxWorkerPostgresTests
{
    [PostgresMigrationFact]
    public async Task Events_are_held_while_the_media_schema_is_not_operational()
    {
        await WithDatabaseAsync(async options =>
        {
            long messageId;
            await using (var setup = new ApplicationDbContext(options))
            {
                var message = new PrismMediaOutboxMessage
                {
                    EventType = PrismMediaOutboxEventType.ActivityPhotoUpsert,
                    ActivityId = 1,
                    AttachmentId = 1,
                    AvailableAfterUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
                };
                setup.PrismMediaOutboxMessages.Add(message);
                await setup.SaveChangesAsync();
                messageId = message.Id;
            }

            var ingestion = new RecordingIngestion();
            var runtimeState = new PrismMediaOutboxRuntimeState();
            var services = new ServiceCollection();
            services.AddScoped(_ => new ApplicationDbContext(options));
            services.AddSingleton<IMediaLibrarySchemaService>(new UnavailableSchema());
            services.AddSingleton<IPrismActivityMediaIngestionService>(ingestion);
            await using var provider = services.BuildServiceProvider();

            var worker = new PrismMediaOutboxWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new PrismMediaOutboxSignal(),
                runtimeState,
                NullLogger<PrismMediaOutboxWorker>.Instance);

            await worker.StartAsync(CancellationToken.None);
            try
            {
                // The worker waits 3 seconds before its first pass.
                var stopwatch = Stopwatch.StartNew();
                while (runtimeState.GetSnapshot().LastError?.Contains("Waiting for an operational media schema", StringComparison.Ordinal) != true)
                {
                    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), "The worker never reported that it was holding events.");
                    await Task.Delay(100);
                }
            }
            finally
            {
                await worker.StopAsync(CancellationToken.None);
            }

            Assert.Equal(0, ingestion.Calls);
            await using var check = new ApplicationDbContext(options);
            var stored = await check.PrismMediaOutboxMessages.AsNoTracking().SingleAsync(message => message.Id == messageId);
            Assert.Equal(PrismMediaOutboxStatus.Pending, stored.Status);
            Assert.Equal(0, stored.AttemptCount);
            Assert.Null(stored.LockedBy);
        });
    }

    [PostgresMigrationFact]
    public async Task Lease_renewal_extends_only_this_workers_processing_messages()
    {
        await WithDatabaseAsync(async options =>
        {
            var oldExpiry = DateTimeOffset.UtcNow.AddSeconds(30);
            PrismMediaOutboxMessage Leased(string? lockedBy, PrismMediaOutboxStatus status) => new()
            {
                EventType = PrismMediaOutboxEventType.ActivityPhotoUpsert,
                Status = status,
                LockedBy = lockedBy,
                LockExpiresAtUtc = oldExpiry
            };

            var mine = Leased("worker-a", PrismMediaOutboxStatus.Processing);
            var other = Leased("worker-b", PrismMediaOutboxStatus.Processing);
            var finished = Leased("worker-a", PrismMediaOutboxStatus.Completed);
            await using (var setup = new ApplicationDbContext(options))
            {
                setup.PrismMediaOutboxMessages.AddRange(mine, other, finished);
                await setup.SaveChangesAsync();
            }

            await using (var db = new ApplicationDbContext(options))
            {
                var renewed = await PrismMediaOutboxWorker.RenewLeasesAsync(
                    db,
                    new[] { mine.Id, other.Id, finished.Id },
                    "worker-a",
                    CancellationToken.None);
                Assert.Equal(1, renewed);
            }

            await using var check = new ApplicationDbContext(options);
            var expiries = await check.PrismMediaOutboxMessages.AsNoTracking()
                .ToDictionaryAsync(message => message.Id, message => message.LockExpiresAtUtc);
            Assert.True(expiries[mine.Id] > DateTimeOffset.UtcNow.AddMinutes(4));
            Assert.Equal(oldExpiry.ToUnixTimeMilliseconds(), expiries[other.Id]!.Value.ToUnixTimeMilliseconds());
            Assert.Equal(oldExpiry.ToUnixTimeMilliseconds(), expiries[finished.Id]!.Value.ToUnixTimeMilliseconds());
        });
    }

    private static async Task WithDatabaseAsync(Func<DbContextOptions<ApplicationDbContext>, Task> test)
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_mediaoutbox_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(target).Options;
            await using (var setup = new ApplicationDbContext(options))
            {
                await setup.Database.MigrateAsync();
            }

            await test(options);
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

    private sealed class UnavailableSchema : IMediaLibrarySchemaService
    {
        public Task<MediaLibrarySchemaStatus> GetStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(MediaLibrarySchemaStatus.Unavailable("media schema missing (test)", "TEST"));
    }

    private sealed class RecordingIngestion : IPrismActivityMediaIngestionService
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task ProcessAsync(PrismMediaOutboxMessage message, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.CompletedTask;
        }
    }
}
