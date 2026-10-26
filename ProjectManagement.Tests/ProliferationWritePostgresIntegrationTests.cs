using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Proliferation writes and their audit entries share one PostgreSQL transaction. These run
/// the real AuditService, whose derived usage-summary upsert is isolated by a savepoint so a
/// failure there cannot abort the caller's transaction. The test creates, migrates and drops
/// its own prism_test_prolif_* database on the PRISM_TEST_POSTGRES_CONNECTION server.
/// </summary>
public sealed class ProliferationWritePostgresIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 8, 0, 0, TimeSpan.Zero);
    private const string AdminUserId = "prolif-pg-admin";

    [PostgresMigrationFact]
    public async Task AuditCommitsWithTheChange_SummaryFailureIsContained_AuditFailureRollsBack()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_prolif_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(target)
                .Options);
            await db.Database.MigrateAsync();
            await SeedAsync(db);

            using var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ILookupNormalizer, UpperInvariantLookupNormalizer>()
                .BuildServiceProvider();
            using var users = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser>(db),
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                services.GetRequiredService<ILookupNormalizer>(),
                new IdentityErrorDescriber(),
                services,
                NullLogger<UserManager<ApplicationUser>>.Instance);
            var clock = FakeClock.AtUtc(Now);
            var service = new ProliferationSubmissionService(
                db,
                users,
                clock,
                new AuditService(db, new HttpContextAccessor(), clock, NullLogger<AuditService>.Instance));

            // 1. A normal write commits the record, its audit row and the usage summary together.
            var first = await service.CreateYearlyAsync(Create(2024), Admin, CancellationToken.None);
            Assert.True(first.Success, first.Error);
            Assert.Equal(1, await CountAsync(target, "SELECT COUNT(*) FROM \"ProliferationYearly\""));
            Assert.Equal(1, await CountAsync(target, "SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"Action\" = 'ProjectOfficeReports.Proliferation.YearlyRecorded'"));
            Assert.Equal(1, await CountAsync(target, $"SELECT COUNT(*) FROM \"UserActivityDailySummaries\" WHERE \"UserId\" = '{AdminUserId}'"));

            // 2. The derived usage summary fails: the savepoint contains it and the change commits.
            await ExecuteAsync(target, "ALTER TABLE \"UserActivityDailySummaries\" RENAME TO \"UserActivityDailySummaries_off\"");
            var second = await service.CreateYearlyAsync(Create(2025), Admin, CancellationToken.None);
            Assert.True(second.Success, second.Error);
            Assert.Null(db.Database.CurrentTransaction);
            Assert.Equal(2, await CountAsync(target, "SELECT COUNT(*) FROM \"ProliferationYearly\""));
            Assert.Equal(2, await CountAsync(target, "SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"Action\" = 'ProjectOfficeReports.Proliferation.YearlyRecorded'"));
            await ExecuteAsync(target, "ALTER TABLE \"UserActivityDailySummaries_off\" RENAME TO \"UserActivityDailySummaries\"");

            // 3. The audit insert itself fails: the change is rolled back, not left behind.
            await ExecuteAsync(target, "ALTER TABLE \"AuditLogs\" RENAME TO \"AuditLogs_off\"");
            await Assert.ThrowsAnyAsync<Exception>(() => service.CreateYearlyAsync(Create(2026), Admin, CancellationToken.None));
            Assert.Equal(2, await CountAsync(target, "SELECT COUNT(*) FROM \"ProliferationYearly\""));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static ClaimsPrincipal Admin => new(new ClaimsIdentity(
        new[]
        {
            new Claim(ClaimTypes.NameIdentifier, AdminUserId),
            new Claim(ClaimTypes.Name, AdminUserId),
            new Claim(ClaimTypes.Role, "Admin")
        },
        "Test"));

    private static ProliferationYearlyCreateDto Create(int year)
        => new() { ProjectId = 1, Source = ProliferationSource.Sdd, Year = year, TotalQuantity = 4 };

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        db.Users.Add(new ApplicationUser
        {
            Id = AdminUserId,
            UserName = AdminUserId,
            NormalizedUserName = AdminUserId.ToUpperInvariant(),
            FullName = "Proliferation Admin",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Simulator",
            CreatedByUserId = AdminUserId,
            LifecycleStatus = ProjectLifecycleStatus.Completed
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
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
