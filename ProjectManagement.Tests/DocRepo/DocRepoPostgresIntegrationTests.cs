using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.Approvals;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.DocRepo;

/// <summary>
/// Document Repository behaviour that depends on PostgreSQL itself: conditional updates that
/// claim OCR work and approval requests, and ts_headline search snippets. Each test creates,
/// migrates and drops its own prism_test_docrepo_* database on the PRISM_TEST_POSTGRES_CONNECTION
/// server.
/// </summary>
public sealed class DocRepoPostgresIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 30, 0, TimeSpan.Zero);

    // Match the application (Program.cs), which runs Npgsql with legacy timestamp behaviour.
    static DocRepoPostgresIntegrationTests() => AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [PostgresMigrationFact]
    public async Task OcrQueue_ClaimsEachDocumentOnce_AndDropsAResultWhoseClaimNoLongerHolds()
    {
        await WithDatabaseAsync(async (target, db) =>
        {
            var first = Seed(db, "First", createdDaysAgo: 3);
            var second = Seed(db, "Second", createdDaysAgo: 2);
            var abandoned = Seed(db, "Abandoned", createdDaysAgo: 1);
            abandoned.OcrLastTriedUtc = Now - DocRepoOcrQueue.ClaimLease - TimeSpan.FromMinutes(1);
            var running = Seed(db, "Running elsewhere", createdDaysAgo: 0);
            running.OcrLastTriedUtc = Now - TimeSpan.FromMinutes(10);
            await db.SaveChangesAsync();

            // Two workers (two app processes during a deploy) never claim the same document.
            await using var otherDb = Open(target);
            var workerA = new DocRepoOcrQueue(db, FakeClock.AtUtc(Now));
            var workerB = new DocRepoOcrQueue(otherDb, FakeClock.AtUtc(Now));
            var claimA = await workerA.ClaimNextAsync(CancellationToken.None);
            var claimB = await workerB.ClaimNextAsync(CancellationToken.None);
            var claimC = await workerA.ClaimNextAsync(CancellationToken.None);
            var none = await workerB.ClaimNextAsync(CancellationToken.None);

            Assert.Equal(first.Id, claimA!.Document.Id);
            Assert.Equal(second.Id, claimB!.Document.Id);
            // A claim older than the lease belongs to a stopped worker and is taken over.
            Assert.Equal(abandoned.Id, claimC!.Document.Id);
            // A claim inside the lease is left alone.
            Assert.Null(none);

            // A normal result is saved with its text.
            Assert.True(await workerA.CompleteAsync(claimA, OcrRunResult.Ok("radar trials completed"), CancellationToken.None));
            await using (var check = Open(target))
            {
                var saved = await check.Documents.Include(d => d.DocumentText).SingleAsync(d => d.Id == first.Id);
                Assert.Equal(DocOcrStatus.Succeeded, saved.OcrStatus);
                Assert.Equal("radar trials completed", saved.DocumentText!.OcrText);
                Assert.Equal(DocRepoOcrQueue.WorkerActor, saved.UpdatedByUserId);
            }

            // Someone re-queues the second document while OCR runs: the stale result is dropped.
            await ExecuteAsync(target, $"UPDATE \"Documents\" SET \"OcrLastTriedUtc\" = NULL WHERE \"Id\" = '{second.Id}'");
            Assert.False(await workerB.CompleteAsync(claimB, OcrRunResult.Fail("too slow"), CancellationToken.None));
            await using (var check = Open(target))
            {
                var stillQueued = await check.Documents.SingleAsync(d => d.Id == second.Id);
                Assert.Equal(DocOcrStatus.Pending, stillQueued.OcrStatus);
                Assert.Null(stillQueued.OcrFailureReason);
            }

            // A document deleted while OCR runs keeps no result either.
            await ExecuteAsync(target, $"UPDATE \"Documents\" SET \"IsDeleted\" = TRUE WHERE \"Id\" = '{abandoned.Id}'");
            Assert.False(await workerA.CompleteAsync(claimC, OcrRunResult.Ok("text"), CancellationToken.None));
        });
    }

    [PostgresMigrationFact]
    public async Task DeleteApproval_RefusesTheRequester_AndAppliesOnlyOneDecision()
    {
        await WithDatabaseAsync(async (target, db) =>
        {
            var document = Seed(db, "Wrongly filed letter", createdDaysAgo: 5);
            var request = new DocumentDeleteRequest
            {
                DocumentId = document.Id,
                RequestedByUserId = "requester",
                RequestedAtUtc = Now.AddHours(-1),
                Reason = "Filed under the wrong office"
            };
            db.DocumentDeleteRequests.Add(request);
            await db.SaveChangesAsync();

            ApprovalDecisionResult Decide(ApplicationDbContext context, string actor, ApprovalDecisionAction action)
                => new RepositoryDocumentDeleteApprovalService(context, new DocRepoAuditService(context), FakeClock.AtUtc(Now))
                    .DecideAsync(request.Id, action, actor, null, CancellationToken.None).GetAwaiter().GetResult();

            await using (var own = Open(target))
            {
                var self = Decide(own, "requester", ApprovalDecisionAction.Approve);
                Assert.Equal(ApprovalDecisionOutcome.Forbidden, self.Outcome);
            }

            // Two approvers open the same request; the second decision finds it already decided.
            await using var approverA = Open(target);
            await using var approverB = Open(target);
            var loadedByB = await approverB.DocumentDeleteRequests.Include(r => r.Document).SingleAsync(r => r.Id == request.Id);
            Assert.Null(loadedByB.ApprovedAtUtc);

            var approved = Decide(approverA, "hod", ApprovalDecisionAction.Approve);
            var rejected = Decide(approverB, "admin", ApprovalDecisionAction.Reject);

            Assert.Equal(ApprovalDecisionOutcome.Success, approved.Outcome);
            Assert.Equal(ApprovalDecisionOutcome.AlreadyDecided, rejected.Outcome);
            await using var check = Open(target);
            Assert.True((await check.Documents.SingleAsync(d => d.Id == document.Id)).IsDeleted);
            Assert.Equal("hod", (await check.DocumentDeleteRequests.SingleAsync()).ApprovedByUserId);
            var events = await check.DocRepoAudits.Select(a => a.EventType).ToListAsync();
            Assert.Equal(new[] { "SoftDeleted" }, events);
        });
    }

    [PostgresMigrationFact]
    public async Task SearchSnippet_FromRealTsHeadline_RendersNoMarkupFromThePdf()
    {
        await WithDatabaseAsync(async (_, db) =>
        {
            var document = Seed(db, "Minutes of radar trials", createdDaysAgo: 1);
            db.DocumentTexts.Add(new DocumentText
            {
                DocumentId = document.Id,
                OcrText = "Radar evaluation <img src=x onerror=alert(1)> <div style=position:fixed;inset:0> radar trials",
                UpdatedAtUtc = Now.UtcDateTime
            });
            document.OcrStatus = DocOcrStatus.Succeeded;
            await db.SaveChangesAsync();

            var hit = await new DocumentSearchService()
                .ApplySearchProjected(db.Documents.AsNoTracking(), "radar")
                .SingleAsync();

            // ts_headline really does pass this markup through...
            Assert.Contains("<img", hit.Snippet);
            // ...and the page renders it as text.
            using var writer = new StringWriter();
            DocRepoSnippetHtml.Render(hit.Snippet).WriteTo(writer, HtmlEncoder.Default);
            var html = writer.ToString();
            Assert.DoesNotContain("<img", html);
            Assert.DoesNotContain("<div", html);
            Assert.Contains("<mark>", html);
        });
    }

    private static async Task WithDatabaseAsync(Func<string, ApplicationDbContext, Task> test)
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_docrepo_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            await using var db = Open(target);
            await db.Database.MigrateAsync();
            db.OfficeCategories.Add(new OfficeCategory { Name = "CPDS North" });
            db.DocumentCategories.Add(new DocumentCategory { Name = "Letter" });
            await db.SaveChangesAsync();
            await test(target, db);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static ApplicationDbContext Open(string connectionString)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options);

    private static Document Seed(ApplicationDbContext db, string subject, int createdDaysAgo)
    {
        var id = Guid.NewGuid();
        var document = new Document
        {
            Id = id,
            Subject = subject,
            OfficeCategoryId = db.OfficeCategories.Local.Single().Id,
            DocumentCategoryId = db.DocumentCategories.Local.Single().Id,
            OriginalFileName = $"{subject}.pdf",
            FileSizeBytes = 10,
            Sha256 = id.ToString("N"),
            StoragePath = $"2026/10/{id:N}.pdf",
            OcrStatus = DocOcrStatus.Pending,
            CreatedByUserId = "uploader",
            CreatedAtUtc = Now.UtcDateTime.AddDays(-createdDaysAgo)
        };
        db.Documents.Add(document);
        return document;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
