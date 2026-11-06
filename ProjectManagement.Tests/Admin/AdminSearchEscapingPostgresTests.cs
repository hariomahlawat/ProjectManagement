using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Admin searches run ILIKE on PostgreSQL. A typed % or _ must match itself, not act as a
/// wildcard. Creates, migrates and drops its own prism_test_adminsearch_* database on the
/// PRISM_TEST_POSTGRES_CONNECTION server.
/// </summary>
public sealed class AdminSearchEscapingPostgresTests
{
    static AdminSearchEscapingPostgresTests() => AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [AdminSearchPostgresFact]
    public async Task ListsAndRecovery_TreatPercentAndUnderscoreAsOrdinaryCharacters()
    {
        await WithDatabaseAsync(async db =>
        {
            db.Users.Add(new ApplicationUser { Id = "admin", UserName = "admin", NormalizedUserName = "ADMIN" });
            db.SponsoringUnits.AddRange(
                new SponsoringUnit { Name = "Unit_7", IsActive = true, SortOrder = 1 },
                new SponsoringUnit { Name = "UnitA7", IsActive = true, SortOrder = 2 },
                new SponsoringUnit { Name = "Share 50% cut", IsActive = true, SortOrder = 3 },
                new SponsoringUnit { Name = "Share 50 per cut", IsActive = true, SortOrder = 4 });
            db.Projects.AddRange(
                new Project { Name = "Radar_Mk2", CreatedByUserId = "admin", CreatedAt = DateTime.UtcNow, IsArchived = true, ArchivedAt = DateTimeOffset.UtcNow },
                new Project { Name = "RadarXMk2", CreatedByUserId = "admin", CreatedAt = DateTime.UtcNow, IsArchived = true, ArchivedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            var lists = new MasterDataAdministrationQueryService(db, new AuditActionPresentationCatalog());
            var underscore = await lists.GetFlatLookupAsync(
                MasterDataFlatLookupKind.SponsoringUnit, new FlatLookupDirectoryRequest("t_7", "all", 1, 25));
            Assert.Equal(new[] { "Unit_7" }, underscore.Rows.Select(row => row.Name));

            var percent = await lists.GetFlatLookupAsync(
                MasterDataFlatLookupKind.SponsoringUnit, new FlatLookupDirectoryRequest("50%", "all", 1, 25));
            Assert.Equal(new[] { "Share 50% cut" }, percent.Rows.Select(row => row.Name));

            var recovery = new ProjectRecoveryQueryService(
                db,
                new AdminTimeService(FakeClock.AtUtc(DateTimeOffset.UtcNow)),
                Options.Create(new ProjectRetentionOptions()),
                Options.Create(new AdminRecoveryOptions()));
            var archived = await recovery.QueryArchivedAsync(new ProjectArchiveQuery("r_M"));
            Assert.Equal(new[] { "Radar_Mk2" }, archived.Items.Select(row => row.Name));
        });
    }

    private static async Task WithDatabaseAsync(Func<ApplicationDbContext, Task> test)
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_adminsearch_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(target).Options);
            await db.Database.MigrateAsync();
            await test(db);
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

[AttributeUsage(AttributeTargets.Method)]
internal sealed class AdminSearchPostgresFactAttribute : FactAttribute
{
    public AdminSearchPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")))
        {
            Skip = "Provide PRISM_TEST_POSTGRES_CONNECTION to run the admin search escaping test.";
        }
    }
}
