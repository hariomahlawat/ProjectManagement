using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Areas.Admin.Pages.Documents;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Ingestion;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Navigation;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Guards from the cold code review of the Administration module (admin fix PR 6).
/// </summary>
public sealed class AdminCodeGuardsTests
{
    // A malformed legacy category can be its own ancestor. The tree walk survived that, but the
    // pass that lists unlinked rows filtered its list before the loop, so in a cycle A -> B -> A
    // visiting A also visited B and B was then listed a second time.
    [Fact]
    public async Task CategoryTree_ListsEveryCategoryOnce_WhenParentsFormACycle()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Users.Add(new ApplicationUser { Id = "admin", UserName = "admin", NormalizedUserName = "ADMIN" });
            await seed.SaveChangesAsync();

            var root = Category("Root");
            var self = Category("Own parent");
            var first = Category("Cycle A");
            var second = Category("Cycle B");
            seed.ProjectCategories.AddRange(root, self, first, second);
            await seed.SaveChangesAsync();

            seed.ProjectCategories.Add(new ProjectCategory { Name = "Child", CreatedByUserId = "admin", IsActive = true, ParentId = root.Id });
            self.ParentId = self.Id;
            first.ParentId = second.Id;
            second.ParentId = first.Id;
            await seed.SaveChangesAsync();
        }

        await using var db = new ApplicationDbContext(options);
        var result = await new MasterDataAdministrationQueryService(db, new AuditActionPresentationCatalog())
            .GetCategoriesAsync(MasterDataCategoryKind.Project, new CategoryDirectoryRequest(null, "all"));

        Assert.Equal(5, result.Total);
        Assert.Equal(5, result.Rows.Count);
        Assert.Equal(result.Rows.Count, result.Rows.Select(row => row.Id).Distinct().Count());
        Assert.Equal(new[] { "Root", "Child" }, result.Rows.Take(2).Select(row => row.Name));
    }

    // The failure report used a private escaper that missed a leading tab, carriage return or
    // spaces before a formula character. It now uses SafeCsv like every other admin export.
    [Fact]
    public async Task PdfIngestionFailureReport_NeutralisesFormulasAndQuotes()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var history = new PdfIngestionRunHistory(db);
        var runId = Guid.NewGuid();
        await history.RecordAsync(new PdfIngestionRunRecord(
            runId,
            new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 6, 6, 5, 0, TimeSpan.Zero),
            "admin",
            "Completed with failures",
            1, 0, 0, 0, 1,
            Array.Empty<PdfIngestionSourceSummary>(),
            new[] { new PdfIngestionFailure("\t=HYPERLINK(\"x\")", "  +42", "\rcmd.pdf", "Said \"no\", -1") },
            null));

        var page = new IngestExternalPdfsModel(
            Unused<IPdfIngestionCoordinator>(),
            history,
            new AdminTimeService(FakeClock.AtUtc(new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero))),
            Unused<IAdminNavigationUrlBuilder>(),
            Unused<IHostApplicationLifetime>());

        var exportAudit = new RecordingExportAudit();
        var file = Assert.IsType<FileContentResult>(await page.OnGetFailureReportAsync(runId, exportAudit, default));
        Assert.Equal(Encoding.UTF8.GetPreamble(), file.FileContents.Take(3));

        var lines = Encoding.UTF8.GetString(file.FileContents, 3, file.FileContents.Length - 3)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Source,Source item,File name,Result", lines[0]);
        Assert.Equal("\"'\t=HYPERLINK(\"\"x\"\")\",\"  '+42\",\"'\rcmd.pdf\",\"Said \"\"no\"\", -1\"", lines[1]);

        // The one admin export that had no audit record now has one, like the others (C8).
        var export = Assert.Single(exportAudit.Records);
        Assert.Same(AdminExportKind.PdfIngestionFailures, export.Kind);
        Assert.Equal(1, export.RowCount);
        Assert.Equal(runId.ToString("N"), export.Filters["Run"]);
    }

    // No file leaves unrecorded: if the export audit cannot be written, the download fails.
    [Fact]
    public async Task PdfIngestionFailureReport_ReturnsNoFile_WhenTheAuditFails()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var history = new PdfIngestionRunHistory(db);
        var runId = Guid.NewGuid();
        await history.RecordAsync(new PdfIngestionRunRecord(
            runId,
            new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 6, 6, 5, 0, TimeSpan.Zero),
            "admin",
            "Partially completed",
            1, 0, 0, 0, 1,
            Array.Empty<PdfIngestionSourceSummary>(),
            new[] { new PdfIngestionFailure("FFC", "7", "a.pdf", "Source file is missing.") },
            null));

        var page = new IngestExternalPdfsModel(
            Unused<IPdfIngestionCoordinator>(),
            history,
            new AdminTimeService(FakeClock.AtUtc(new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero))),
            Unused<IAdminNavigationUrlBuilder>(),
            Unused<IHostApplicationLifetime>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            page.OnGetFailureReportAsync(runId, new RecordingExportAudit { Fail = true }, default));
    }

    private sealed class RecordingExportAudit : IAdminExportAuditService
    {
        public bool Fail { get; init; }

        public List<AdminExportRecord> Records { get; } = new();

        public Task RecordAsync(AdminExportRecord export, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("Audit store unavailable.");
            Records.Add(export);
            return Task.CompletedTask;
        }
    }

    // Admin searches build ILIKE patterns. A raw $"%{search}%" turns a typed % or _ into a
    // wildcard; SearchLikePattern.Contains escapes them. AdminSearchEscapingPostgresTests checks
    // the behaviour on PostgreSQL; this check keeps the raw form from coming back.
    [Fact]
    public void AdminServices_DoNotBuildUnescapedLikePatterns()
    {
        var root = RepositoryRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "Services", "Admin"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Areas", "Admin", "Pages"), "*.cs", SearchOption.AllDirectories))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\$""%\{"))
            .Select(file => Path.GetRelativePath(root, file))
            .ToArray();

        Assert.True(offenders.Length == 0, "Use SearchLikePattern.Contains in: " + string.Join(", ", offenders));
    }

    private static ProjectCategory Category(string name) =>
        new() { Name = name, CreatedByUserId = "admin", IsActive = true };

    private static T Unused<T>() where T : class => DispatchProxy.Create<T, UnusedProxy>();

    public class UnusedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"{targetMethod?.Name} should not be called by the failure report.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
