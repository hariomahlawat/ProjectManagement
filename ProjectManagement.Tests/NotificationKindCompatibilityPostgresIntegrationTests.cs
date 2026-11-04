using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Services.Notifications;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// During a rolling deployment an older instance can meet notification rows whose kind was added
/// by a newer release. Converting that stored name back to the enum throws, which used to fail the
/// dispatcher's whole batch (keeping it locked), the bell and the retention sweep. Rows with an
/// unknown kind can only be written with raw SQL, so this runs against PostgreSQL. The test creates,
/// migrates and drops its own prism_test_notifkind_* database on the PRISM_TEST_POSTGRES_CONNECTION server.
/// </summary>
public sealed class NotificationKindCompatibilityPostgresIntegrationTests
{
    private const string FutureKind = "KindFromANewerRelease";

    [PostgresMigrationFact]
    public async Task RowsWithAKindFromANewerReleaseAreSkippedNotFatal()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_notifkind_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(target)
                .Options;
            await using (var setup = new ApplicationDbContext(options))
            {
                await setup.Database.MigrateAsync();
            }

            var clock = new NotificationDispatcherTests.FixedClock(new DateTimeOffset(2024, 10, 1, 12, 0, 0, TimeSpan.Zero));
            var hub = new NotificationDispatcherTests.RecordingHubContext();
            await using var provider = NotificationDispatcherTests.CreateProvider(options, clock, hub);

            // A known dispatch, queued after an unknown one so the unknown row sits first in the batch.
            await ExecuteAsync(target, $"""
                INSERT INTO "NotificationDispatches" ("RecipientUserId", "Kind", "PayloadJson", "CreatedUtc", "AttemptCount")
                VALUES ('recipient-1', '{FutureKind}', '[]', '2024-10-01 10:00:00', 0);
                INSERT INTO "Notifications" ("RecipientUserId", "Kind", "Title", "CreatedUtc")
                VALUES ('recipient-1', '{FutureKind}', 'From a newer release', '2024-09-01 10:00:00');
                """);
            await NotificationDispatcherTests.SeedRecipientAndDispatchAsync(provider);

            // 1. The dispatcher delivers the known dispatch and leaves the unknown one unclaimed.
            Assert.True(await NotificationDispatcherTests.RunBatchAsync(provider, clock));
            Assert.Equal(1, await CountAsync(target,
                $"SELECT COUNT(*) FROM \"NotificationDispatches\" WHERE \"Kind\" = 'RemarkCreated' AND \"DispatchedUtc\" IS NOT NULL"));
            Assert.Equal(1, await CountAsync(target,
                $"SELECT COUNT(*) FROM \"NotificationDispatches\" WHERE \"Kind\" = '{FutureKind}' AND \"AttemptCount\" = 0 AND \"LockToken\" IS NULL AND \"DispatchedUtc\" IS NULL AND \"DeadLetteredUtc\" IS NULL"));
            Assert.Single(hub.GetClient("recipient-1").Notifications);

            // 2. The inbox queries skip the unknown row instead of failing.
            using (var scope = provider.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<UserNotificationService>();
                var principal = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, "recipient-1") },
                    "Test"));

                var page = await service.ListPageAsync(principal, "recipient-1", new NotificationListOptions());
                Assert.Single(page.Items);
                Assert.Equal(1, page.TotalCount);
                Assert.Equal(1, await service.CountUnreadAsync(principal, "recipient-1"));
            }

            // 3. Retention runs and leaves the unknown row for an instance that can read it.
            var retention = new NotificationRetentionService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new NotificationRetentionOptions { MaxAge = TimeSpan.FromDays(14), MaxPerUser = 100 }),
                clock,
                NullLogger<NotificationRetentionService>.Instance);
            await retention.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, await CountAsync(target,
                $"SELECT COUNT(*) FROM \"Notifications\" WHERE \"Kind\" = '{FutureKind}'"));
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

    private static async Task<int> CountAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
