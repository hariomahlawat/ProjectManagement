using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Tests.DocRepo;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Second admin review, PR 6: on PostgreSQL the repository lists use the built-in xmin column as
/// their row version (no schema change), and deleting a value locks its row first because the
/// documents' foreign key cascades. The test creates, migrates and drops its own
/// prism_test_doclist_* database on the PRISM_TEST_POSTGRES_CONNECTION server.
/// </summary>
public sealed class DocRepoListPostgresIntegrationTests
{
    [PostgresMigrationFact]
    public async Task RowVersion_RefusesStaleChanges_AndDeleteOnlyRemovesUnusedValues()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_doclist_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            await using (var setup = Create(target))
            {
                await setup.Database.MigrateAsync();
                setup.OfficeCategories.Add(new OfficeCategory { Name = "North", SortOrder = 1 });
                setup.DocumentCategories.AddRange(
                    new DocumentCategory { Name = "Letter", SortOrder = 1 },
                    new DocumentCategory { Name = "Unused", SortOrder = 2 });
                await setup.SaveChangesAsync();
                var id = Guid.NewGuid();
                setup.Documents.Add(new Document
                {
                    Id = id,
                    Subject = "Filed",
                    OfficeCategoryId = setup.OfficeCategories.Local.Single().Id,
                    DocumentCategoryId = setup.DocumentCategories.Local.Single(item => item.Name == "Letter").Id,
                    OriginalFileName = "filed.pdf",
                    FileSizeBytes = 10,
                    Sha256 = id.ToString("N"),
                    StoragePath = $"2026/11/{id:N}.pdf",
                    CreatedByUserId = "uploader",
                    CreatedAtUtc = DateTime.UtcNow
                });
                await setup.SaveChangesAsync();
            }

            // The page reads each row's version.
            var opened = await ListAsync(target, DocRepoListKind.DocumentType);
            var letter = opened.Single(item => item.Name == "Letter");
            Assert.NotNull(letter.Version);

            // Someone renames it; the row's version moves on.
            var renamed = await RunAsync(target, lists => lists.UpdateAsync(DocRepoListKind.DocumentType, Admin, letter.Id, "Letters", 1, letter.Version));
            Assert.True(renamed.Succeeded, renamed.Message);
            var current = (await ListAsync(target, DocRepoListKind.DocumentType)).Single(item => item.Id == letter.Id);
            Assert.NotEqual(letter.Version, current.Version);

            // A switch from the page opened before the rename is refused and changes nothing.
            var stale = await RunAsync(target, lists => lists.SetActiveAsync(DocRepoListKind.DocumentType, Admin, letter.Id, false, letter.Version));
            Assert.Equal("ConcurrencyConflict", stale.ErrorCode);
            Assert.True((await ListAsync(target, DocRepoListKind.DocumentType)).Single(item => item.Id == letter.Id).IsActive);

            // With the current version it saves; repeating it leaves it deactivated.
            var deactivated = await RunAsync(target, lists => lists.SetActiveAsync(DocRepoListKind.DocumentType, Admin, letter.Id, false, current.Version));
            var repeated = await RunAsync(target, lists => lists.SetActiveAsync(DocRepoListKind.DocumentType, Admin, letter.Id, false, current.Version));
            Assert.True(deactivated.Succeeded, deactivated.Message);
            Assert.True(repeated.Succeeded, repeated.Message);
            Assert.False((await ListAsync(target, DocRepoListKind.DocumentType)).Single(item => item.Id == letter.Id).IsActive);

            // Delete: refused while a document uses the value, done (row lock and all) when unused.
            var used = (await ListAsync(target, DocRepoListKind.DocumentType)).Single(item => item.Id == letter.Id);
            var refused = await RunAsync(target, lists => lists.DeleteAsync(DocRepoListKind.DocumentType, Admin, used.Id, used.Version));
            Assert.Equal("InUse", refused.ErrorCode);
            var unused = opened.Single(item => item.Name == "Unused");
            var deleted = await RunAsync(target, lists => lists.DeleteAsync(DocRepoListKind.DocumentType, Admin, unused.Id, unused.Version));
            Assert.True(deleted.Succeeded, deleted.Message);

            await using var check = Create(target);
            Assert.Equal(1, await check.Documents.CountAsync());
            Assert.Equal(new[] { "Letters" }, await check.DocumentCategories.Select(item => item.Name).ToArrayAsync());
            Assert.Equal(3, await check.DocRepoAudits.CountAsync());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static ClaimsPrincipal Admin => DocRepoTestSupport.User("doclist-pg-admin", RoleNames.Admin);

    private static ApplicationDbContext Create(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);

    private static async Task<System.Collections.Generic.IReadOnlyList<DocRepoListItem>> ListAsync(string connectionString, DocRepoListKind kind)
    {
        await using var db = Create(connectionString);
        return await new DocRepoListService(db, new DocRepoAuditService(db), DocRepoTestSupport.Authorization(), Microsoft.Extensions.Options.Options.Create(new DocRepoOptions())).ListAsync(kind);
    }

    /// <summary>Each change in its own context, as each request has its own.</summary>
    private static async Task<DocRepoListResult> RunAsync(string connectionString, Func<DocRepoListService, Task<DocRepoListResult>> action)
    {
        await using var db = Create(connectionString);
        return await action(new DocRepoListService(db, new DocRepoAuditService(db), DocRepoTestSupport.Authorization(), Microsoft.Extensions.Options.Options.Create(new DocRepoOptions())));
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
