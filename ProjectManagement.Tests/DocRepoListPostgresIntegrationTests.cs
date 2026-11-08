using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Tests.DocRepo;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Second admin review, PR 6: on PostgreSQL the repository lists use the built-in xmin column as
/// their row version (no schema change), and deleting a value locks its row first because the
/// documents' foreign key cascades. Since third-review PR 12 the list and Delete go through the
/// shared list rule (ReferenceListService). Each test creates, migrates and drops its own
/// prism_test_doclist_* database on the PRISM_TEST_POSTGRES_CONNECTION server.
/// </summary>
public sealed class DocRepoListPostgresIntegrationTests
{
    [PostgresMigrationFact]
    public async Task RowVersion_RefusesStaleChanges_AndDeleteOnlyRemovesUnusedValues()
    {
        await WithDatabaseAsync(async target =>
        {
            await using (var setup = Create(target))
            {
                setup.OfficeCategories.Add(new OfficeCategory { Name = "North", SortOrder = 1 });
                setup.DocumentCategories.AddRange(
                    new DocumentCategory { Name = "Letter", SortOrder = 1 },
                    new DocumentCategory { Name = "Unused", SortOrder = 2 });
                await setup.SaveChangesAsync();
                setup.Documents.Add(NewDocument(
                    setup.OfficeCategories.Local.Single().Id,
                    setup.DocumentCategories.Local.Single(item => item.Name == "Letter").Id));
                await setup.SaveChangesAsync();
            }

            // The page reads each row's version (xmin, carried as four bytes like every list's row version).
            var opened = await PageAsync(target);

            // A search (name and description) translates on PostgreSQL (Codex review on #3019).
            await using (var searchDb = Create(target))
            {
                var searched = await ReferenceLists(searchDb).GetPageAsync(ReferenceListKeys.DocumentTypes, new ReferenceListRequest("lett", null, 1, 25));
                Assert.Equal("Letter", Assert.Single(searched.Rows).Name);
            }
            var letter = opened.Single(item => item.Name == "Letter");
            var letterId = int.Parse(letter.Id);
            var openedVersion = Xmin(letter.RowVersion);

            // Someone renames it; the row's version moves on.
            var renamed = await RunAsync(target, lists => lists.UpdateAsync(DocRepoListKind.DocumentType, Admin, letterId, "Letters", 1, openedVersion));
            Assert.True(renamed.Succeeded, renamed.Message);
            var currentVersion = Xmin((await PageAsync(target)).Single(item => item.Id == letter.Id).RowVersion);
            Assert.NotEqual(openedVersion, currentVersion);

            // A switch from the page opened before the rename is refused and changes nothing.
            var stale = await RunAsync(target, lists => lists.SetActiveAsync(DocRepoListKind.DocumentType, Admin, letterId, false, openedVersion));
            Assert.Equal("ConcurrencyConflict", stale.ErrorCode);
            Assert.True((await PageAsync(target)).Single(item => item.Id == letter.Id).IsActive);

            // With the current version it saves; repeating it leaves it deactivated.
            var deactivated = await RunAsync(target, lists => lists.SetActiveAsync(DocRepoListKind.DocumentType, Admin, letterId, false, currentVersion));
            var repeated = await RunAsync(target, lists => lists.SetActiveAsync(DocRepoListKind.DocumentType, Admin, letterId, false, currentVersion));
            Assert.True(deactivated.Succeeded, deactivated.Message);
            Assert.True(repeated.Succeeded, repeated.Message);
            var used = (await PageAsync(target)).Single(item => item.Id == letter.Id);
            Assert.False(used.IsActive);

            // Delete, through the shared list rule: refused while a document uses the value,
            // refused from a page opened before a change, done when unused.
            Assert.Equal("1 document", used.UsedBy.Single(item => item.Count > 0).Text);
            var refused = await DeleteAsync(target, letterId, used.RowVersion);
            Assert.Equal("EntityInUse", refused.ErrorCode);

            var unused = opened.Single(item => item.Name == "Unused");
            var unusedId = int.Parse(unused.Id);
            var moved = await RunAsync(target, lists => lists.UpdateAsync(DocRepoListKind.DocumentType, Admin, unusedId, "Unused", 5, Xmin(unused.RowVersion)));
            Assert.True(moved.Succeeded, moved.Message);
            var outOfDate = await DeleteAsync(target, unusedId, unused.RowVersion);
            Assert.Equal("ConcurrencyConflict", outOfDate.ErrorCode);
            var fresh = (await PageAsync(target)).Single(item => item.Id == unused.Id);
            var deleted = await DeleteAsync(target, unusedId, fresh.RowVersion);
            Assert.True(deleted.Succeeded, deleted.UserMessage);

            await using var check = Create(target);
            Assert.Equal(1, await check.Documents.CountAsync());
            Assert.Equal(new[] { "Letters" }, await check.DocumentCategories.Select(item => item.Name).ToArrayAsync());
            // Rename, deactivate, the move and the delete, in the repository's own audit.
            Assert.Equal(4, await check.DocRepoAudits.CountAsync());
        });
    }

    // A document filed while an administrator deletes its type must not be deleted with it (the
    // documents' foreign key cascades): the delete locks the type first, so it waits for the
    // filing, then counts the document and refuses.
    [PostgresMigrationFact]
    public async Task DeletingAType_WaitsForADocumentBeingFiled_AndThenRefuses()
    {
        await WithDatabaseAsync(async target =>
        {
            int officeId, typeId;
            await using (var setup = Create(target))
            {
                var office = new OfficeCategory { Name = "North", SortOrder = 1 };
                var type = new DocumentCategory { Name = "Minutes", SortOrder = 1 };
                setup.AddRange(office, type);
                await setup.SaveChangesAsync();
                (officeId, typeId) = (office.Id, type.Id);
            }

            var row = (await PageAsync(target)).Single();

            // Someone files a document under the type and has not committed yet.
            await using var other = Create(target);
            await using var filing = await other.Database.BeginTransactionAsync();
            var document = NewDocument(officeId, typeId);
            other.Documents.Add(document);
            await other.SaveChangesAsync();

            var deleting = DeleteAsync(target, typeId, row.RowVersion);

            // The delete waits on the document's lock instead of counting nothing and going ahead.
            await Task.Delay(1500);
            Assert.False(deleting.IsCompleted);

            await filing.CommitAsync();
            var result = await deleting;

            Assert.Equal("EntityInUse", result.ErrorCode);
            await using var check = Create(target);
            Assert.True(await check.Documents.AnyAsync(item => item.Id == document.Id));
            Assert.True(await check.DocumentCategories.AnyAsync(item => item.Id == typeId));
        });
    }

    private static ClaimsPrincipal Admin => DocRepoTestSupport.User("doclist-pg-admin", RoleNames.Admin);

    private static ApplicationDbContext Create(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);

    private static Document NewDocument(int officeId, int typeId)
    {
        var id = Guid.NewGuid();
        return new Document
        {
            Id = id,
            Subject = "Filed",
            OfficeCategoryId = officeId,
            DocumentCategoryId = typeId,
            OriginalFileName = "filed.pdf",
            FileSizeBytes = 10,
            Sha256 = id.ToString("N"),
            StoragePath = $"2026/11/{id:N}.pdf",
            CreatedByUserId = "uploader",
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static uint Xmin(string rowVersion) => BitConverter.ToUInt32(Convert.FromBase64String(rowVersion));

    private static ReferenceListService ReferenceLists(ApplicationDbContext db)
    {
        var services = new ServiceCollection()
            .AddSingleton<IDocRepoAuditService>(new DocRepoAuditService(db))
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(new DocRepoOptions()))
            .BuildServiceProvider();
        var http = new DefaultHttpContext { User = Admin, RequestServices = services };
        return new ReferenceListService(db, new NoAdminAudit(), DocRepoTestSupport.Authorization(), new HttpContextAccessor { HttpContext = http }, services);
    }

    private static async Task<System.Collections.Generic.IReadOnlyList<ReferenceListRow>> PageAsync(string connectionString)
    {
        await using var db = Create(connectionString);
        return (await ReferenceLists(db).GetPageAsync(ReferenceListKeys.DocumentTypes, new ReferenceListRequest(null, null, 1, 100))).Rows;
    }

    private static async Task<AdminOperationResult> DeleteAsync(string connectionString, int id, string rowVersion)
    {
        await using var db = Create(connectionString);
        return await ReferenceLists(db).DeleteAsync(ReferenceListKeys.DocumentTypes, id, Convert.FromBase64String(rowVersion));
    }

    /// <summary>Each change in its own context, as each request has its own.</summary>
    private static async Task<DocRepoListResult> RunAsync(string connectionString, Func<DocRepoListService, Task<DocRepoListResult>> action)
    {
        await using var db = Create(connectionString);
        return await action(new DocRepoListService(db, new DocRepoAuditService(db), DocRepoTestSupport.Authorization()));
    }

    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_doclist_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            await using (var migrate = Create(target))
            {
                await migrate.Database.MigrateAsync();
            }

            await test(target);
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

    /// <summary>The repository lists keep their own audit; nothing goes to the admin audit.</summary>
    private sealed class NoAdminAudit : IAdminAuditService
    {
        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The repository lists keep their own audit.");
    }
}
