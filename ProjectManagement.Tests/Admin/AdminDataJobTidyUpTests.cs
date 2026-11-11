using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Admin.Pages.Users;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Ingestion;
using ProjectManagement.Services.Admin.Maintenance;
using ProjectManagement.Services.Admin.Recovery;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Documents;
using ProjectManagement.Services.Projects;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Admin fix 9 of the third review: the data-job tidy-up (docs/ux/admin-review-3-fixes.md,
/// C5 and C8).
/// </summary>
public sealed class AdminDataJobTidyUpTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prism-r3-pr9-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    // ---- Legacy import (C5) ----------------------------------------------------------------

    [Fact]
    public async Task LegacyImport_WhenTheAuditFails_ImportsNothing()
    {
        await using var connection = await OpenAsync();
        await using var db = await SeedAsync(connection);
        var service = LegacyImport(db, new ThrowingAudit());

        var preview = await PreviewAsync(service);
        var result = await service.CommitAsync(preview.Token, 1, 1, "admin");

        Assert.False(result.Succeeded);
        Assert.Contains("nothing was imported", result.Message);
        await using var check = new ApplicationDbContext(Options(connection));
        Assert.Empty(await check.Projects.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await check.ProjectLegacyImports.ToListAsync());
        Assert.Empty(await check.AuditLogs.ToListAsync());

        // The preview is kept, so the import can be tried again once the audit store is back.
        Assert.True(File.Exists(Path.Combine(NewStaging, $"{preview.Token:N}.json")));
    }

    [Fact]
    public async Task LegacyImport_WritesTheAuditWithTheImport()
    {
        await using var connection = await OpenAsync();
        await using var db = await SeedAsync(connection);
        var service = LegacyImport(db, new DbAudit(db));

        var preview = await PreviewAsync(service);
        var result = await service.CommitAsync(preview.Token, 1, 1, "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, result.RowsImported);
        await using var check = new ApplicationDbContext(Options(connection));
        Assert.Equal(2, await check.Projects.IgnoreQueryFilters().CountAsync());
        Assert.Single(await check.ProjectLegacyImports.ToListAsync());
        var audit = Assert.Single(await check.AuditLogs.ToListAsync());
        Assert.Equal("LegacyImportCommitted", audit.Action);
        Assert.False(File.Exists(Path.Combine(NewStaging, $"{preview.Token:N}.json")));
    }

    [Fact]
    public async Task LegacyImport_StagesUnderTheDataRoot()
    {
        await using var connection = await OpenAsync();
        await using var db = await SeedAsync(connection);
        var service = LegacyImport(db, new DbAudit(db));

        var preview = await PreviewAsync(service);

        Assert.True(File.Exists(Path.Combine(NewStaging, $"{preview.Token:N}.xlsx")));
        Assert.True(File.Exists(Path.Combine(NewStaging, $"{preview.Token:N}.json")));
        Assert.False(Directory.Exists(OldStaging));
    }

    // With no upload root configured it defaults to wwwroot/uploads, which is served without
    // sign-in. Staged workbooks then stay in the private app folder (Codex review on #3010).
    [Fact]
    public async Task LegacyImport_NeverStagesInsideTheWebRoot()
    {
        await using var connection = await OpenAsync();
        await using var db = await SeedAsync(connection);
        var webRoot = Path.Combine(_root, "app", "wwwroot");
        var service = new LegacyImportPreflightService(
            db,
            new ProjectImportService(db),
            new DbAudit(db),
            new TestWebHostEnvironment { ContentRootPath = Path.Combine(_root, "app"), WebRootPath = webRoot },
            new TestUploadRootProvider(Path.Combine(webRoot, "uploads")),
            Microsoft.Extensions.Options.Options.Create(new AdminRecoveryOptions()),
            NullLogger<LegacyImportPreflightService>.Instance);

        var preview = await PreviewAsync(service);

        Assert.True(File.Exists(Path.Combine(OldStaging, $"{preview.Token:N}.xlsx")));
        Assert.False(Directory.Exists(webRoot) && Directory.EnumerateFiles(webRoot, "*", SearchOption.AllDirectories).Any());
        Assert.True((await service.CommitAsync(preview.Token, 1, 1, "admin")).Succeeded);
    }

    [Theory]
    [InlineData("C:/site/wwwroot/uploads", "C:/site/wwwroot", true)]
    [InlineData("C:/site/wwwroot", "C:/site/wwwroot/", true)]
    [InlineData("C:/site/wwwroot-data/uploads", "C:/site/wwwroot", false)]
    [InlineData("F:/ProjectManagementData/uploads", "C:/site/wwwroot", false)]
    [InlineData("F:/ProjectManagementData/uploads", null, false)]
    public void LegacyImport_WebRootCheck(string path, string? root, bool expected) =>
        Assert.Equal(expected, LegacyImportPreflightService.IsWithin(path, root));

    // A preview checked on the previous release sits in the old app folder. For one release it
    // can still be imported.
    [Fact]
    public async Task LegacyImport_CommitsAPreviewStagedInTheOldFolder()
    {
        await using var connection = await OpenAsync();
        await using var db = await SeedAsync(connection);
        var service = LegacyImport(db, new DbAudit(db));
        var preview = await PreviewAsync(service);

        Directory.CreateDirectory(OldStaging);
        foreach (var extension in new[] { ".xlsx", ".json" })
        {
            File.Move(
                Path.Combine(NewStaging, $"{preview.Token:N}{extension}"),
                Path.Combine(OldStaging, $"{preview.Token:N}{extension}"));
        }

        var result = await service.CommitAsync(preview.Token, 1, 1, "admin");

        Assert.True(result.Succeeded, result.Message);
        Assert.False(File.Exists(Path.Combine(OldStaging, $"{preview.Token:N}.json")));
        Assert.False(File.Exists(Path.Combine(OldStaging, $"{preview.Token:N}.xlsx")));
    }

    [Fact]
    public async Task LegacyImport_CancelRemovesAPreviewFromEitherFolder()
    {
        await using var connection = await OpenAsync();
        await using var db = await SeedAsync(connection);
        var service = LegacyImport(db, new DbAudit(db));
        Directory.CreateDirectory(OldStaging);
        var token = Guid.NewGuid();
        var old = Path.Combine(OldStaging, $"{token:N}.json");
        await File.WriteAllTextAsync(old, "{}");

        await service.CancelAsync(token);

        Assert.False(File.Exists(old));
    }

    [Fact]
    public async Task LegacyImport_EachPreviewSweepsBothFolders()
    {
        await using var connection = await OpenAsync();
        await using var db = await SeedAsync(connection);
        var service = LegacyImport(db, new DbAudit(db));
        Directory.CreateDirectory(OldStaging);
        Directory.CreateDirectory(NewStaging);
        var expiredOld = Path.Combine(OldStaging, "expired.xlsx");
        var expiredNew = Path.Combine(NewStaging, "expired.json");
        var freshOld = Path.Combine(OldStaging, "fresh.json");
        foreach (var path in new[] { expiredOld, expiredNew, freshOld })
        {
            await File.WriteAllTextAsync(path, "x");
        }
        File.SetLastWriteTimeUtc(expiredOld, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(expiredNew, DateTime.UtcNow.AddDays(-2));

        await PreviewAsync(service);

        Assert.False(File.Exists(expiredOld));
        Assert.False(File.Exists(expiredNew));
        Assert.True(File.Exists(freshOld));
    }

    // ---- Unreadable PDFs paging (C8) -------------------------------------------------------

    [Fact]
    public async Task UnreadablePdfs_ArePagedNewestFirst()
    {
        await using var db = InMemory();
        var start = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 30; i++)
        {
            db.Documents.Add(Document($"Failed {i:00}", DocOcrStatus.Failed, start.AddHours(i)));
        }
        db.Documents.Add(Document("In the bin", DocOcrStatus.Failed, start.AddDays(5), isDeleted: true));
        db.Documents.Add(Document("Read fine", DocOcrStatus.Succeeded, start.AddDays(5)));
        await db.SaveChangesAsync();
        var query = new OcrFailureQueryService(db);

        var first = await query.GetPageAsync(1, 25);
        Assert.Equal(30, first.TotalCount);
        Assert.Equal(25, first.Items.Count);
        Assert.Equal("Failed 29", first.Items[0].Subject);
        Assert.Equal("Office", first.Items[0].OfficeCategoryName);
        Assert.Equal(2, first.TotalPages);

        var second = await query.GetPageAsync(2, 25);
        Assert.Equal(5, second.Items.Count);
        Assert.Equal("Failed 00", second.Items[^1].Subject);
        Assert.Equal(26, second.FirstItem);
        Assert.Equal(30, second.LastItem);

        // A page past the end shows the last page; an unknown page size falls back to 25.
        var past = await query.GetPageAsync(99, 7);
        Assert.Equal(2, past.Page);
        Assert.Equal(25, past.PageSize);
    }

    [Fact]
    public void UnreadablePdfs_KeepTheirPolicy()
    {
        var attribute = typeof(ProjectManagement.Areas.DocumentRepository.Pages.Admin.OcrFailuresModel)
            .GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();
        Assert.Equal(Policies.Documents.DeleteApprove, attribute?.Policy);
    }

    // ---- Recycle bulk loop in a service (C8) -----------------------------------------------

    [Fact]
    public async Task Recycle_ActsOnlyOnDocumentsStillInTheBin_AndCarriesOnPastAFailure()
    {
        var (documents, log) = Documents(failOn: 2);
        var service = Recycle(StubRecoveryQueryOf(new[] { 1, 2, 3 }), documents);

        var result = await service.ExecuteAsync(DocumentRecycleAction.PermanentlyDelete, new[] { 1, 2, 3, 4, 4 }, "admin");

        Assert.Equal(4, result.Requested);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(new[] { 2 }, result.FailedIds);
        Assert.Equal(new[] { "delete:1", "delete:2", "delete:3" }, log.CallLog);
        Assert.False(result.NothingEligible);
    }

    // A second run of the same request finds the documents gone from the bin and does nothing.
    [Fact]
    public async Task Recycle_SecondRunOfTheSameRequest_ChangesNothing()
    {
        var (documents, log) = Documents();
        var service = Recycle(StubRecoveryQueryOf(Array.Empty<int>()), documents);

        var result = await service.ExecuteAsync(DocumentRecycleAction.Restore, new[] { 5, 6 }, "admin");

        Assert.True(result.NothingEligible);
        Assert.Equal(2, result.Skipped);
        Assert.Empty(log.CallLog);
    }

    [Fact]
    public async Task Recycle_RefusesMoreThanTheBulkLimit()
    {
        var service = Recycle(StubRecoveryQueryOf(Array.Empty<int>()), Documents().Service, maximum: 2);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ExecuteAsync(DocumentRecycleAction.Restore, new[] { 1, 2, 3 }, "admin"));
    }

    // ---- Disable account typed results (C8) ------------------------------------------------

    [Fact]
    public void Disable_WordsComeFromTheOutcome_NotFromExceptionText()
    {
        Assert.Equal("You cannot disable your own signed-in account.", DisableModel.PublicDisableFailure(UserDisableOutcome.SelfDisable, "ref"));
        Assert.Equal("This account is waiting to be deleted. Undo that first if you want to disable it instead.", DisableModel.PublicDisableFailure(UserDisableOutcome.PendingDeletion, "ref"));
        Assert.Equal("This account is the last active Administrator and cannot be disabled.", DisableModel.PublicDisableFailure(UserDisableOutcome.LastActiveAdmin, "ref"));
        Assert.Equal("The account no longer exists or was removed by another administrator.", DisableModel.PublicDisableFailure(UserDisableOutcome.NotFound, "ref"));
        Assert.Equal("The account could not be disabled. Reference: ref.", DisableModel.PublicDisableFailure((UserDisableOutcome)99, "ref"));
    }

    [Fact]
    public void Disable_PageNoLongerReadsExceptionMessages()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Areas", "Admin", "Pages", "Users", "Disable.cshtml.cs"));
        Assert.DoesNotContain("exception.Message", source);
        Assert.DoesNotContain("Cannot disable the last active Admin.", source);
    }

    // ---- PDF run history in the database (C8) ----------------------------------------------

    [Fact]
    public async Task RunHistory_IsSharedThroughTheDatabase_AndARetryDoesNotAddARow()
    {
        await using var connection = await OpenAsync();
        await using (var setup = new ApplicationDbContext(Options(connection)))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var runId = Guid.NewGuid();
        var run = Run(runId, new DateTimeOffset(2026, 11, 7, 6, 0, 0, TimeSpan.Zero), "Partially completed");
        await using (var first = new ApplicationDbContext(Options(connection)))
        {
            var history = new PdfIngestionRunHistory(first);
            await history.RecordAsync(run);
            await history.RecordAsync(run with { Status = "Partially completed with audit warning" });
        }

        // Another process (a new context) sees the same run, with its sources and failures.
        await using var second = new ApplicationDbContext(Options(connection));
        var read = await new PdfIngestionRunHistory(second).GetAsync(runId);
        Assert.NotNull(read);
        Assert.Equal("Partially completed with audit warning", read!.Status);
        Assert.Equal(run.StartedAtUtc, read.StartedAtUtc);
        Assert.Equal(run.CompletedAtUtc, read.CompletedAtUtc);
        Assert.Equal("FFC", Assert.Single(read.Sources).Source);
        Assert.Equal("a.pdf", Assert.Single(read.Failures).FileName);
        Assert.Equal(1, await second.PdfIngestionRuns.CountAsync());
    }

    [Fact]
    public async Task RunHistory_KeepsOnlyTheNewestRuns_NewestFirst()
    {
        await using var connection = await OpenAsync();
        await using var db = new ApplicationDbContext(Options(connection));
        await db.Database.EnsureCreatedAsync();
        var history = new PdfIngestionRunHistory(db);
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < PdfIngestionRunHistory.Capacity + 3; i++)
        {
            await history.RecordAsync(Run(Guid.NewGuid(), start.AddHours(i), "Completed"));
        }

        Assert.Equal(PdfIngestionRunHistory.Capacity, await db.PdfIngestionRuns.CountAsync());
        var recent = await history.GetRecentAsync(10);
        Assert.Equal(10, recent.Count);
        Assert.Equal(start.AddHours(PdfIngestionRunHistory.Capacity + 2), recent[0].CompletedAtUtc);
        Assert.Equal(recent[0].RunId, (await history.GetLatestAsync())!.RunId);
    }

    [Fact]
    public void RunHistory_MigrationIsInTheManifest()
    {
        var manifest = File.ReadAllLines(Path.Combine(RepositoryRoot(), "Migrations", "immutable-migration-ids.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        Assert.Contains("20261226080013_AddPdfIngestionRuns", manifest);
        Assert.Contains("20261227080014_AddItProjects", manifest);
        Assert.Contains("20261229080016_AddItProjectAttachments", manifest);
        Assert.Contains("20261230080017_AddItProjectStageMovedAt", manifest);
        Assert.Contains("20261231080018_AddItProjectFinancialYears", manifest);
        Assert.Contains("20270101080019_AddItProjectRemarkType", manifest);
        Assert.Equal("20270102080020_AddItProjectModuleAndOfficer", manifest[^1]);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private string NewStaging => Path.Combine(_root, "uploads", LegacyImportPreflightService.StagingFolderName);

    private string OldStaging => Path.Combine(_root, "app", "App_Data", "AdminImportStaging");

    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static DbContextOptions<ApplicationDbContext> Options(SqliteConnection connection) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

    private static async Task<ApplicationDbContext> SeedAsync(SqliteConnection connection)
    {
        var db = new ApplicationDbContext(Options(connection));
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = "admin", UserName = "admin", NormalizedUserName = "ADMIN" });
        await db.SaveChangesAsync();
        db.ProjectCategories.Add(new ProjectCategory { Id = 1, Name = "Old projects", CreatedByUserId = "admin", IsActive = true });
        db.TechnicalCategories.Add(new TechnicalCategory { Id = 1, Name = "Radars", CreatedByUserId = "admin", IsActive = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private LegacyImportPreflightService LegacyImport(ApplicationDbContext db, IAdminAuditService audit) =>
        new(
            db,
            new ProjectImportService(db),
            audit,
            new TestWebHostEnvironment { ContentRootPath = Path.Combine(_root, "app") },
            new TestUploadRootProvider(Path.Combine(_root, "uploads")),
            Microsoft.Extensions.Options.Options.Create(new AdminRecoveryOptions()),
            NullLogger<LegacyImportPreflightService>.Instance);

    private static async Task<LegacyImportPreview> PreviewAsync(LegacyImportPreflightService service)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Projects");
        sheet.Cell(1, 1).Value = "Nomenclature";
        sheet.Cell(1, 2).Value = "YearOfDevp";
        sheet.Cell(2, 1).Value = "Old radar";
        sheet.Cell(2, 2).Value = 1998;
        sheet.Cell(3, 1).Value = "Old sonar";
        sheet.Cell(3, 2).Value = 2001;
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        var file = new FormFile(stream, 0, stream.Length, "Upload", "legacy.xlsx");

        var operation = await service.PreviewAsync(1, 1, file);
        Assert.True(operation.Succeeded, operation.UserMessage);
        Assert.True(operation.Value!.CanCommit);
        return operation.Value;
    }

    private static ApplicationDbContext InMemory() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ocr-failures-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static Document Document(string subject, DocOcrStatus status, DateTime created, bool isDeleted = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Subject = subject,
            OfficeCategory = new OfficeCategory { Name = "Office" },
            DocumentCategory = new DocumentCategory { Name = "Letters" },
            OriginalFileName = subject + ".pdf",
            FileSizeBytes = 1024,
            Sha256 = Guid.NewGuid().ToString("N"),
            StoragePath = subject + ".pdf",
            OcrStatus = status,
            OcrFailureReason = status == DocOcrStatus.Failed ? "No text layer" : null,
            IsDeleted = isDeleted,
            CreatedByUserId = "admin",
            CreatedAtUtc = created
        };

    private static DocumentRecycleService Recycle(IDocumentRecoveryQueryService query, IDocumentService documents, int maximum = 100) =>
        new(
            query,
            documents,
            Microsoft.Extensions.Options.Options.Create(new AdminRecoveryOptions { MaximumBulkDocuments = maximum }),
            NullLogger<DocumentRecycleService>.Instance);

    private static PdfIngestionRunRecord Run(Guid id, DateTimeOffset completed, string status) =>
        new(
            id,
            completed.AddMinutes(-5),
            completed,
            "admin",
            status,
            3, 1, 1, 0, 1,
            new[] { new PdfIngestionSourceSummary("FFC", 3, 1, 1, 0, 1) },
            new[] { new PdfIngestionFailure("FFC", "7", "a.pdf", "Ingestion failed.") },
            "trace-1");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class ThrowingAudit : IAdminAuditService
    {
        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Audit store unavailable.");
    }

    /// <summary>Writes the audit row through the same context, as the real audit service does.</summary>
    private sealed class DbAudit : IAdminAuditService
    {
        private readonly ApplicationDbContext _db;

        public DbAudit(ApplicationDbContext db) => _db = db;

        public async Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            _db.AuditLogs.Add(new AuditLog { Action = entry.Action, Level = entry.Level, TimeUtc = DateTime.UtcNow, Message = entry.Message });
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public class StubRecoveryQuery : DispatchProxy
    {
        public IReadOnlyList<int> Eligible { get; set; } = Array.Empty<int>();

        public static IDocumentRecoveryQueryService Create(IReadOnlyList<int> eligible)
        {
            var proxy = DispatchProxy.Create<IDocumentRecoveryQueryService, StubRecoveryQuery>();
            ((StubRecoveryQuery)(object)proxy).Eligible = eligible;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IDocumentRecoveryQueryService.GetSelectionSummaryAsync))
                throw new InvalidOperationException($"{targetMethod?.Name} should not be called.");

            var requested = ((IReadOnlyCollection<int>)args![0]!).ToArray();
            var eligible = requested.Where(Eligible.Contains).ToArray();
            return Task.FromResult(new DocumentRecoverySelectionSummary(
                requested.Length, eligible.Length, 0, Array.Empty<string>(), eligible));
        }
    }

    private static IDocumentRecoveryQueryService StubRecoveryQueryOf(IReadOnlyList<int> eligible) => StubRecoveryQuery.Create(eligible);

    private static (IDocumentService Service, RecordingDocuments Log) Documents(int failOn = 0)
    {
        var proxy = DispatchProxy.Create<IDocumentService, RecordingDocuments>();
        var log = (RecordingDocuments)(object)proxy;
        log.FailOn = failOn;
        return (proxy, log);
    }

    public class RecordingDocuments : DispatchProxy
    {
        public List<string> CallLog { get; } = new();
        public int FailOn { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var id = (int)args![0]!;
            var verb = targetMethod!.Name switch
            {
                nameof(IDocumentService.RestoreAsync) => "restore",
                nameof(IDocumentService.HardDeleteAsync) => "delete",
                _ => throw new InvalidOperationException($"{targetMethod.Name} should not be called.")
            };
            CallLog.Add($"{verb}:{id}");
            if (id == FailOn)
                return verb == "restore"
                    ? Task.FromException<ProjectDocument>(new InvalidOperationException("Storage offline."))
                    : Task.FromException(new InvalidOperationException("Storage offline."));
            return verb == "restore" ? Task.FromResult(new ProjectDocument()) : Task.CompletedTask;
        }
    }
}
