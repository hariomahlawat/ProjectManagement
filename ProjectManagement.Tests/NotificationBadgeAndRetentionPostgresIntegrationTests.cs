using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Notifications;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// The bell's priority ordering, the unseen badge count, seen-all and the retention auto-read are
/// set-based queries (ORDER BY over an enum-string predicate, ExecuteUpdate with COALESCE) that the
/// EF in-memory provider does not translate. These run them against PostgreSQL. The test creates,
/// migrates and drops its own prism_test_notif_* database on the PRISM_TEST_POSTGRES_CONNECTION server.
/// </summary>
public sealed class NotificationBadgeAndRetentionPostgresIntegrationTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    private const string UserId = "notif-pg-user";

    [PostgresMigrationFact]
    public async Task PriorityOrderUnseenCountSeenAllAndAutoReadTranslateOnPostgres()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_notif_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(target)
                .Options;

            await using (var setup = new ApplicationDbContext(dbOptions))
            {
                await setup.Database.MigrateAsync();
                setup.Notifications.AddRange(
                    New(NotificationKind.PlanSubmitted, Now.AddDays(-3)),
                    New(NotificationKind.DocumentPublished, Now.AddMinutes(-1)),
                    New(NotificationKind.RemarkCreated, Now.AddMinutes(-2)),
                    New(NotificationKind.ActionTaskAssigned, Now.AddDays(-1), readUtc: Now),
                    New(NotificationKind.MentionedInRemark, Now.AddDays(-2)),
                    // Retention fixtures: stale informational, stale-but-protected and expired action.
                    New(NotificationKind.StageStatusChanged, Now.AddDays(-9)),
                    New(NotificationKind.ActivityDeleteRequested, Now.AddDays(-20)),
                    New(NotificationKind.TrainingDeleteRequested, Now.AddDays(-31)));
                await setup.SaveChangesAsync();
            }

            var ids = await IdsByKindAsync(dbOptions);

            // 1. Priority order: unread action-required newest first, then everything else newest first.
            await using (var db = new ApplicationDbContext(dbOptions))
            {
                var service = new UserNotificationService(db, FakeClock.AtUtc(new DateTimeOffset(Now)));
                var page = await service.ListPageAsync(
                    Principal,
                    UserId,
                    new NotificationListOptions { Limit = 6, ActionRequiredFirst = true, IncludeFilterOptions = false });

                Assert.Equal(
                    new[]
                    {
                        ids[NotificationKind.MentionedInRemark],
                        ids[NotificationKind.PlanSubmitted],
                        ids[NotificationKind.ActivityDeleteRequested],
                        ids[NotificationKind.TrainingDeleteRequested],
                        ids[NotificationKind.DocumentPublished],
                        ids[NotificationKind.RemarkCreated],
                    },
                    page.Items.Select(item => item.Id).ToArray());
                Assert.Null(page.NextCursor);
                // Every row except the already-read task assignment is unseen.
                Assert.Equal(7, page.UnseenCount);

                // 2. Seen-all up to an id leaves later rows unseen and never touches read state.
                var upToId = ids[NotificationKind.RemarkCreated];
                var seen = await service.MarkAllSeenAsync(Principal, UserId, upToId);
                Assert.True(seen.AffectedCount > 0);
                Assert.Equal(
                    await CountAsync(target, $"SELECT COUNT(*) FROM \"Notifications\" WHERE \"Id\" > {upToId} AND \"SeenUtc\" IS NULL AND \"ReadUtc\" IS NULL"),
                    await service.CountUnseenAsync(Principal, UserId));
                Assert.Equal(7, await service.CountUnreadAsync(Principal, UserId));
            }

            // 3. Retention auto-read and protection, with the production 14 day age limit.
            await using (var provider = new ServiceCollection()
                .AddLogging()
                .AddScoped(_ => new ApplicationDbContext(dbOptions))
                .BuildServiceProvider())
            {
                var retention = new NotificationRetentionService(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    Options.Create(new NotificationRetentionOptions
                    {
                        MaxAge = TimeSpan.FromDays(14),
                        MaxPerUser = 100,
                        AutoReadInformationalAfter = TimeSpan.FromDays(7),
                        AutoReadActionRequiredAfter = TimeSpan.FromDays(30),
                    }),
                    FakeClock.AtUtc(new DateTimeOffset(Now)),
                    NullLogger<NotificationRetentionService>.Instance);

                var result = await retention.RunSweepAsync(CancellationToken.None);

                // Auto-read: the 9 day stage update and the 31 day training request.
                Assert.Equal(2, result.AutoReadCount);
                // MaxAge then removes the now-read 31 day request; the unread 20 day request is kept.
                Assert.Equal(1, result.RemovedCount);
            }

            Assert.Equal(1, await CountAsync(target,
                $"SELECT COUNT(*) FROM \"Notifications\" WHERE \"Id\" = {ids[NotificationKind.StageStatusChanged]} AND \"ReadUtc\" IS NOT NULL AND \"SeenUtc\" IS NOT NULL"));
            Assert.Equal(1, await CountAsync(target,
                $"SELECT COUNT(*) FROM \"Notifications\" WHERE \"Id\" = {ids[NotificationKind.ActivityDeleteRequested]} AND \"ReadUtc\" IS NULL"));
            Assert.Equal(0, await CountAsync(target,
                $"SELECT COUNT(*) FROM \"Notifications\" WHERE \"Id\" = {ids[NotificationKind.TrainingDeleteRequested]}"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static Notification New(NotificationKind kind, DateTime createdUtc, DateTime? readUtc = null)
        => new()
        {
            RecipientUserId = UserId,
            Kind = kind,
            Title = kind.ToString(),
            CreatedUtc = createdUtc,
            DeliveredUtc = createdUtc,
            ReadUtc = readUtc,
            SeenUtc = readUtc,
        };

    private static async Task<System.Collections.Generic.Dictionary<NotificationKind, int>> IdsByKindAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        return await db.Notifications
            .AsNoTracking()
            .ToDictionaryAsync(n => n.Kind!.Value, n => n.Id);
    }

    private static ClaimsPrincipal Principal => new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.NameIdentifier, UserId) },
        "Test"));

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
