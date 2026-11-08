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
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
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

            // The shared list page (PR 10) searches the reference lists; its In use tile and
            // Used by counts must translate for PostgreSQL too.
            var lists = new ReferenceListService(db, new NoAdminAudit(), new DenyAuthorization(), new Microsoft.AspNetCore.Http.HttpContextAccessor());
            var underscore = await lists.GetPageAsync(
                ReferenceListKeys.SponsoringUnits, new ReferenceListRequest("t_7", "all", 1, 25));
            Assert.Equal(new[] { "Unit_7" }, underscore.Rows.Select(row => row.Name));

            var percent = await lists.GetPageAsync(
                ReferenceListKeys.SponsoringUnits, new ReferenceListRequest("50%", "inuse", 1, 25));
            Assert.Empty(percent.Rows);
            Assert.Equal(new ReferenceListTiles(4, 0, 0), percent.Tiles);
            percent = await lists.GetPageAsync(
                ReferenceListKeys.SponsoringUnits, new ReferenceListRequest("50%", "all", 1, 25));
            Assert.Equal(new[] { "Share 50% cut" }, percent.Rows.Select(row => row.Name));
            Assert.Equal(new[] { "0 projects", "0 change requests" }, percent.Rows[0].UsedBy.Select(item => item.Text));

            var recovery = new ProjectRecoveryQueryService(
                db,
                new AdminTimeService(FakeClock.AtUtc(DateTimeOffset.UtcNow)),
                Options.Create(new ProjectRetentionOptions()),
                Options.Create(new AdminRecoveryOptions()));
            var archived = await recovery.QueryArchivedAsync(new ProjectArchiveQuery("r_M"));
            Assert.Equal(new[] { "Radar_Mk2" }, archived.Items.Select(row => row.Name));
        });
    }

    // Codex review on #3018: an ARPP entry's link is emptied when its value is deleted, so the
    // database does not refuse the delete. An entry saved while an administrator deletes the
    // value must either be counted (and the delete refused) or be refused itself; it must never
    // silently lose its value.
    [AdminSearchPostgresFact]
    public async Task DeletingAnArppValue_WaitsForAnEntryBeingSaved_AndThenRefuses()
    {
        await WithDatabaseAsync(async (db, connectionString) =>
        {
            var now = DateTimeOffset.UtcNow;
            db.Users.Add(new ApplicationUser { Id = "admin", UserName = "admin", NormalizedUserName = "ADMIN" });
            var value = new ProjectManagement.Models.Arpp.ArppCfaOption { Name = "CFA One", NormalizedName = "CFA ONE", CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = "admin", UpdatedByUserId = "admin" };
            var issue = new ProjectManagement.Models.Arpp.ArppIssue { Name = "ARPP 2026-27", FinancialYearStart = 2026, Kind = ProjectManagement.Models.Arpp.ArppIssueKind.Original, IssueSequence = 0, IssueDate = new DateOnly(2026, 4, 1), CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = "admin", UpdatedByUserId = "admin" };
            db.AddRange(value, issue);
            await db.SaveChangesAsync();
            var rowVersion = value.RowVersion;

            // Another person saves an entry that uses the value, and has not committed yet.
            await using var other = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
            await using var saving = await other.Database.BeginTransactionAsync();
            other.ArppEntries.Add(new ProjectManagement.Models.Arpp.ArppEntry
            {
                ArppIssueId = issue.Id, ProjectReference = "P-1", Category = ProjectManagement.Models.Arpp.ArppCategory.New, SerialNumber = "1",
                CfaOptionId = value.Id, Cfa = value.Name, Fund = "Capital", DfpdsSchedule = "Sch 4",
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = "admin", UpdatedByUserId = "admin"
            });
            await other.SaveChangesAsync();

            var http = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "admin") }, "test"))
            };
            var lists = new ReferenceListService(db, new NoAdminAudit(), new AllowAuthorization(), new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = http });
            var deleting = lists.DeleteAsync(ReferenceListKeys.ArppCfa, value.Id, rowVersion);

            // The delete waits on the entry's lock instead of counting nothing and going ahead.
            await Task.Delay(1500);
            Assert.False(deleting.IsCompleted);

            await saving.CommitAsync();
            var result = await deleting;

            Assert.False(result.Succeeded);
            Assert.Equal("EntityInUse", result.ErrorCode);
            await using var check = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
            Assert.Equal(value.Id, await check.ArppEntries.Select(entry => entry.CfaOptionId).SingleAsync());
            Assert.True(await check.ArppCfaOptions.AnyAsync(item => item.Id == value.Id));
        });
    }

    // PR 12: the social media lists are keyed by Guid. A platform locked for its delete makes an
    // event being logged on it wait (or the delete waits for the event): either way the event is
    // counted and the delete refused, and the Guid row lock and count translate on PostgreSQL.
    [AdminSearchPostgresFact]
    public async Task DeletingASocialMediaPlatform_WaitsForAnEventBeingLogged_AndThenRefuses()
    {
        await WithDatabaseAsync(async (db, connectionString) =>
        {
            // Unique names: the migrations seed some event types and platforms.
            var type = ProjectManagement.Tests.SocialMediaTestData.CreateEventType(name: "PR12 race type");
            var platform = ProjectManagement.Tests.SocialMediaTestData.CreatePlatform(name: "PR12 Threads");
            var unused = ProjectManagement.Tests.SocialMediaTestData.CreatePlatform(name: "PR12 Mastodon");
            db.SocialMediaEventTypes.Add(type);
            db.SocialMediaPlatforms.AddRange(platform, unused);
            await db.SaveChangesAsync();

            var http = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "admin") }, "test"))
            };
            ReferenceListService Lists(ApplicationDbContext context) =>
                new(context, new NoAdminAudit(), new AllowAuthorization(), new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = http });

            var page = await Lists(db).GetPageAsync(ReferenceListKeys.SocialMediaPlatforms, new ReferenceListRequest(null, null, 1, 25));
            var row = page.Rows.Single(item => item.Name == "PR12 Threads");
            Assert.Equal(platform.Id.ToString(), row.Id);

            // Someone logs an event on the platform and has not committed yet.
            await using var other = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
            await using var logging = await other.Database.BeginTransactionAsync();
            other.SocialMediaEvents.Add(ProjectManagement.Tests.SocialMediaTestData.CreateEvent(type.Id, platform.Id));
            await other.SaveChangesAsync();

            await using var deleteContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
            var deleting = Lists(deleteContext).DeleteAsync(ReferenceListKeys.SocialMediaPlatforms, platform.Id, Convert.FromBase64String(row.RowVersion));

            await Task.Delay(1500);
            Assert.False(deleting.IsCompleted);

            await logging.CommitAsync();
            var result = await deleting;

            Assert.Equal("EntityInUse", result.ErrorCode);
            await using var check = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);
            Assert.True(await check.SocialMediaPlatforms.AnyAsync(item => item.Id == platform.Id));

            // An unused platform goes.
            var unusedRow = (await Lists(check).GetPageAsync(ReferenceListKeys.SocialMediaPlatforms, new ReferenceListRequest(null, null, 1, 25)))
                .Rows.Single(item => item.Name == "PR12 Mastodon");
            var deleted = await Lists(check).DeleteAsync(ReferenceListKeys.SocialMediaPlatforms, unused.Id, Convert.FromBase64String(unusedRow.RowVersion));
            Assert.True(deleted.Succeeded, deleted.UserMessage);
        });
    }

    private static Task WithDatabaseAsync(Func<ApplicationDbContext, Task> test) =>
        WithDatabaseAsync((db, _) => test(db));

    private static async Task WithDatabaseAsync(Func<ApplicationDbContext, string, Task> test)
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
            await test(db, target);
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

file sealed class NoAdminAudit : IAdminAuditService
{
    public Task RecordAsync(AdminAuditEntry entry, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file sealed class DenyAuthorization : Microsoft.AspNetCore.Authorization.IAuthorizationService
{
    public Task<Microsoft.AspNetCore.Authorization.AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, object? resource, System.Collections.Generic.IEnumerable<Microsoft.AspNetCore.Authorization.IAuthorizationRequirement> requirements) =>
        Task.FromResult(Microsoft.AspNetCore.Authorization.AuthorizationResult.Failed());

    public Task<Microsoft.AspNetCore.Authorization.AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, object? resource, string policyName) =>
        Task.FromResult(Microsoft.AspNetCore.Authorization.AuthorizationResult.Failed());
}

file sealed class AllowAuthorization : Microsoft.AspNetCore.Authorization.IAuthorizationService
{
    public Task<Microsoft.AspNetCore.Authorization.AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, object? resource, System.Collections.Generic.IEnumerable<Microsoft.AspNetCore.Authorization.IAuthorizationRequirement> requirements) =>
        Task.FromResult(Microsoft.AspNetCore.Authorization.AuthorizationResult.Success());

    public Task<Microsoft.AspNetCore.Authorization.AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, object? resource, string policyName) =>
        Task.FromResult(Microsoft.AspNetCore.Authorization.AuthorizationResult.Success());
}
