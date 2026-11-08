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
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Admin fix 16: Reactivate re-checks "restored but still hidden" with the document rows locked,
/// so a deactivation made at the same moment (Documents/Manage) is never overridden. The test
/// creates, migrates and drops its own prism_test_hidden_* database on the
/// PRISM_TEST_POSTGRES_CONNECTION server.
/// </summary>
public sealed class DocRepoRestoredHiddenPostgresIntegrationTests
{
    [PostgresMigrationFact]
    public async Task Reactivate_WaitsForAConcurrentDeactivation_AndThenLeavesTheDocumentHidden()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_hidden_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            var hiddenId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            await using (var setup = Create(target))
            {
                await setup.Database.MigrateAsync();
                setup.OfficeCategories.Add(new OfficeCategory { Name = "North", SortOrder = 1 });
                setup.DocumentCategories.Add(new DocumentCategory { Name = "Letter", SortOrder = 1 });
                await setup.SaveChangesAsync();
                foreach (var id in new[] { hiddenId, otherId })
                {
                    setup.Documents.Add(NewDocument(setup, id));
                    // Restored before second-review PR 2: out of the trash, still inactive.
                    setup.DocRepoAudits.Add(new DocRepoAudit
                    {
                        DocumentId = id,
                        EventType = DocRepoRestoredHiddenService.RestoredEvent,
                        ActorUserId = "restorer",
                        OccurredAtUtc = DateTimeOffset.UtcNow.AddDays(-5),
                        DetailsJson = "{}"
                    });
                }

                await setup.SaveChangesAsync();
            }

            // Both are listed when the admin opens the page.
            await using (var listing = Create(target))
            {
                Assert.Equal(2, (await Service(listing).ListAsync(Admin)).Count);
            }

            // Another administrator deactivates one on purpose (as Documents/Manage does: the row
            // and its audit entry in one save), and has not committed yet.
            await using var deactivating = Create(target);
            await using var deactivation = await deactivating.Database.BeginTransactionAsync();
            var document = await deactivating.Documents.SingleAsync(d => d.Id == hiddenId);
            document.IsActive = false;
            document.UpdatedAtUtc = DateTime.UtcNow;
            document.UpdatedByUserId = "other-admin";
            new DocRepoAuditService(deactivating).Stage(hiddenId, "other-admin", DocRepoRestoredHiddenService.DeactivatedEvent, new { document.Id });
            await deactivating.SaveChangesAsync();

            // "Reactivate all shown" now waits for that row lock...
            await using var reactivating = Create(target);
            var reactivate = Service(reactivating).ReactivateManyAsync(Admin, new[] { hiddenId, otherId });
            await Task.Delay(TimeSpan.FromMilliseconds(750));
            Assert.False(reactivate.IsCompleted);

            // ...and once the deactivation commits, it sees it and leaves that document hidden.
            await deactivation.CommitAsync();
            var result = await reactivate.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(new DocRepoReactivateManyResult(1, 1), result);
            await using var check = Create(target);
            Assert.False((await check.Documents.AsNoTracking().SingleAsync(d => d.Id == hiddenId)).IsActive);
            Assert.True((await check.Documents.AsNoTracking().SingleAsync(d => d.Id == otherId)).IsActive);
            var activated = await check.DocRepoAudits.AsNoTracking()
                .Where(a => a.EventType == DocRepoRestoredHiddenService.ActivatedEvent)
                .Select(a => a.DocumentId)
                .ToListAsync();
            Assert.Equal(new Guid?[] { otherId }, activated);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static ClaimsPrincipal Admin => DocRepoTestSupport.User("hidden-pg-admin", RoleNames.Admin);

    private static ApplicationDbContext Create(string connectionString) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);

    private static DocRepoRestoredHiddenService Service(ApplicationDbContext db) =>
        new(db, new DocRepoAuditService(db), DocRepoTestSupport.Authorization(), FakeClock.AtUtc(DateTimeOffset.UtcNow));

    private static Document NewDocument(ApplicationDbContext db, Guid id) => new()
    {
        Id = id,
        Subject = $"Restored {id:N}"[..20],
        OfficeCategoryId = db.OfficeCategories.Local.Single().Id,
        DocumentCategoryId = db.DocumentCategories.Local.Single().Id,
        OriginalFileName = "restored.pdf",
        FileSizeBytes = 10,
        Sha256 = id.ToString("N"),
        StoragePath = $"2026/11/{id:N}.pdf",
        IsActive = false,
        CreatedByUserId = "uploader",
        CreatedAtUtc = DateTime.UtcNow.AddDays(-30)
    };

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
