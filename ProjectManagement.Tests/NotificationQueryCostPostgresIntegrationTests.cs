using System;
using System.Data.Common;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Notifications;
using ProjectManagement.Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

namespace ProjectManagement.Tests;

/// <summary>
/// The bell runs on every page load and the count endpoint after every read/seen mutation, so
/// their database round trips are counted here against PostgreSQL. The limits are regression
/// guards: raise one only with a reason. The test creates, migrates and drops its own
/// prism_test_notifcost_* database on the PRISM_TEST_POSTGRES_CONNECTION server.
/// </summary>
public sealed class NotificationQueryCostPostgresIntegrationTests
{
    private const string UserId = "notif-cost-user";
    private static readonly DateTime Now = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    private readonly ITestOutputHelper _output;

    public NotificationQueryCostPostgresIntegrationTests(ITestOutputHelper output) => _output = output;

    [PostgresMigrationFact]
    public async Task BellPageAndCountsStayWithinTheirRoundTripBudgets()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_notifcost_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            await using (var setup = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(target).Options))
            {
                await setup.Database.MigrateAsync();
                for (var projectId = 1; projectId <= 3; projectId++)
                {
                    setup.Projects.Add(new Project { Id = projectId, Name = $"Project {projectId}", CreatedByUserId = "creator" });
                }

                await setup.SaveChangesAsync();
                setup.UserProjectMutes.Add(new UserProjectMute { UserId = UserId, ProjectId = 3 });
                setup.Notifications.AddRange(Enumerable.Range(0, 30).Select(i => new Notification
                {
                    RecipientUserId = UserId,
                    Kind = i % 5 == 0 ? NotificationKind.PlanSubmitted : NotificationKind.DocumentPublished,
                    ProjectId = i % 4 == 3 ? null : (i % 3) + 1,
                    Title = $"Notification {i}",
                    CreatedUtc = Now.AddMinutes(-i),
                    DeliveredUtc = Now.AddMinutes(-i),
                    ReadUtc = i % 2 == 0 ? Now : null,
                    SeenUtc = i % 3 == 0 ? Now : null,
                    ActorUserId = "actor-1",
                }));
                await setup.SaveChangesAsync();
            }

            var counter = new CommandCounter();
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(target)
                .AddInterceptors(counter)
                .Options);
            var service = new UserNotificationService(db, FakeClock.AtUtc(new DateTimeOffset(Now)));

            // The bell's page: what the view component and the bell's refresh request.
            counter.Reset();
            var bell = await service.ListPageAsync(Principal, UserId, new NotificationListOptions
            {
                Limit = 50,
                ActionRequiredFirst = true,
                IncludeFilterOptions = false,
            });
            var bellCommands = counter.Count;

            counter.Reset();
            var counts = await service.CountAsync(Principal, UserId);
            var countCommands = counter.Count;

            _output.WriteLine($"Bell page: {bellCommands} commands; counts: {countCommands} commands.");

            // Results are what the bell relies on, checked against the separate count queries and
            // SQL rather than against the shared aggregate itself.
            Assert.NotEmpty(bell.Items);
            Assert.DoesNotContain(bell.Items, item => item.ProjectId == 3);
            var unread = await service.CountUnreadAsync(Principal, UserId);
            var unseen = await service.CountUnseenAsync(Principal, UserId);
            Assert.True(unread > 0 && unseen > 0);
            Assert.Equal(unread, counts.UnreadCount);
            Assert.Equal(unseen, counts.UnseenCount);
            Assert.Equal(unread, bell.UnreadCount);
            Assert.Equal(unseen, bell.UnseenCount);
            Assert.Equal(
                await ScalarAsync(target, $"SELECT MAX(\"Id\") FROM \"Notifications\" WHERE \"RecipientUserId\" = '{UserId}' AND (\"ProjectId\" IS NULL OR \"ProjectId\" <> 3)"),
                bell.LatestNotificationId);

            // Budgets, in database round trips.
            Assert.True(bellCommands <= 6, $"Bell page used {bellCommands} round trips (budget 6).");
            Assert.True(countCommands <= 3, $"Counts used {countCommands} round trips (budget 3).");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
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

    private static async Task<int> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => _count;

        public void Reset() => _count = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
