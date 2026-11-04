using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Notifications;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// The transactional outbox relies on PostgreSQL semantics the in-memory and SQLite providers do
/// not reproduce: a failed statement aborts the whole transaction unless it is rolled back to a
/// savepoint. These tests run <see cref="TransactionalNotificationQueue"/> against PostgreSQL. The
/// test creates, migrates and drops its own prism_test_outbox_* database on the
/// PRISM_TEST_POSTGRES_CONNECTION server.
/// </summary>
public sealed class TransactionalNotificationQueuePostgresIntegrationTests
{
    [PostgresMigrationFact]
    public async Task NotificationCommitsWithTheChangeRollsBackWithItAndNeverFailsIt()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_outbox_{Guid.NewGuid():N}"[..40];
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

            // 1. Success: the business row and its notification commit together.
            var committed = await RunAsync(options, "Committed", failNotification: false, commit: true);
            Assert.True(committed);
            Assert.Equal(1, await CountAsync(target, "SELECT COUNT(*) FROM \"Projects\" WHERE \"Name\" = 'Committed'"));
            Assert.Equal(1, await CountAsync(target, "SELECT COUNT(*) FROM \"NotificationDispatches\" WHERE \"Title\" = 'Committed'"));

            // 2. The business transaction rolls back: the notification goes with it.
            await RunAsync(options, "RolledBack", failNotification: false, commit: false);
            Assert.Equal(0, await CountAsync(target, "SELECT COUNT(*) FROM \"Projects\" WHERE \"Name\" = 'RolledBack'"));
            Assert.Equal(0, await CountAsync(target, "SELECT COUNT(*) FROM \"NotificationDispatches\" WHERE \"Title\" = 'RolledBack'"));

            // 3. The notification step hits a real SQL error. Without the savepoint PostgreSQL
            //    would abort the transaction and the commit would fail; with it the change commits.
            var queued = await RunAsync(options, "NotificationFailed", failNotification: true, commit: true);
            Assert.False(queued);
            Assert.Equal(1, await CountAsync(target, "SELECT COUNT(*) FROM \"Projects\" WHERE \"Name\" = 'NotificationFailed'"));
            Assert.Equal(0, await CountAsync(target, "SELECT COUNT(*) FROM \"NotificationDispatches\" WHERE \"Title\" = 'NotificationFailed'"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static async Task<bool> RunAsync(
        DbContextOptions<ApplicationDbContext> options,
        string name,
        bool failNotification,
        bool commit)
    {
        await using var db = new ApplicationDbContext(options);
        var outbox = new NotificationPublisher(
            db,
            FakeClock.AtUtc(new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero)),
            NullLogger<NotificationPublisher>.Instance);
        var queue = new TransactionalNotificationQueue(db);

        await using var transaction = await RelationalTransactionScope.CreateAsync(db.Database);
        db.Projects.Add(new Project { Name = name, CreatedByUserId = "creator" });

        var queued = await queue.QueueAfterSaveAsync(
            "test change",
            async ct =>
            {
                await outbox.QueueAsync(
                    NotificationKind.DocumentPublished,
                    new[] { "recipient-1" },
                    new { name },
                    module: "Test",
                    eventType: "Test",
                    scopeType: null,
                    scopeId: null,
                    projectId: null,
                    actorUserId: "actor-1",
                    route: null,
                    title: name,
                    summary: null,
                    fingerprint: null,
                    ct);

                if (failNotification)
                {
                    // A real failed statement, as a broken lookup or insert would produce.
                    await db.Database.ExecuteSqlRawAsync("SELECT 1 / 0", ct);
                }
            });

        // The transaction must still be usable for further business work after a failure.
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").SingleAsync());

        if (commit)
        {
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }

        return queued;
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
