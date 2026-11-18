using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services.ProjectBriefings;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

/// <summary>
/// The Add projects list runs on PostgreSQL in production; the in-memory tests cannot run ILIKE,
/// the id-list filters or the ARPP and proliferation subqueries (cold review 4). Creates, migrates
/// and drops its own prism_test_briefingfilter_* database on the PRISM_TEST_POSTGRES_CONNECTION
/// server.
/// </summary>
public sealed class ProjectBriefingFilterPostgresTests
{
    [BriefingFilterPostgresFact]
    public async Task Search_AndEveryFilter_RunOnPostgres()
    {
        await WithDatabaseAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.Users.AddRange(
                new ApplicationUser { Id = "admin", UserName = "admin", NormalizedUserName = "ADMIN" },
                new ApplicationUser { Id = "po", UserName = "po", NormalizedUserName = "PO", FullName = "Anupam Porwal", Rank = "Lt Col" });
            var vr = new TechnicalCategory { Name = "PR4 VR", IsActive = true };
            var coe = new ProjectCategory { Name = "PR4 CoE", IsActive = true };
            db.AddRange(vr, coe);
            await db.SaveChangesAsync();
            var vrMedical = new TechnicalCategory { Name = "PR4 VR medical", ParentId = vr.Id, IsActive = false };
            db.Add(vrMedical);
            await db.SaveChangesAsync();

            Project Row(string name, ProjectLifecycleStatus status, int? technical = null, string? lead = null)
                => new() { Name = name, CreatedByUserId = "admin", CreatedAt = DateTime.UtcNow, LifecycleStatus = status, TechnicalCategoryId = technical, CategoryId = coe.Id, LeadPoUserId = lead };
            var share = Row("Share 50% sim", ProjectLifecycleStatus.Active, vr.Id, "po");
            var sharePer = Row("Share 50 per sim", ProjectLifecycleStatus.Active);
            var radar = Row("Radar_Mk2", ProjectLifecycleStatus.Completed, vrMedical.Id);
            var radarX = Row("RadarXMk2", ProjectLifecycleStatus.Completed);
            radar.CompletedOn = new DateOnly(2025, 5, 10);
            radarX.CompletedOn = new DateOnly(2024, 5, 10);
            db.Projects.AddRange(share, sharePer, radar, radarX);
            await db.SaveChangesAsync();

            db.ProjectStages.Add(new ProjectStage { ProjectId = share.Id, StageCode = "FS", SortOrder = 1, Status = StageStatus.InProgress });
            db.ProjectTechStatuses.Add(new ProjectTechStatus { ProjectId = radar.Id, AvailableForProliferation = true, MarkedAtUtc = now, MarkedByUserId = "admin" });
            var issue = new ArppIssue { Name = "ARPP 2026-27", FinancialYearStart = 2026, Kind = ArppIssueKind.Original, IssueSequence = 0, IssueDate = new DateOnly(2026, 4, 1), CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = "admin", UpdatedByUserId = "admin" };
            db.Add(issue);
            await db.SaveChangesAsync();
            db.ArppPublishedIssues.Add(new ArppPublishedIssue
            {
                ArppIssueId = issue.Id, RevisionNumber = 1, FinancialYearStart = 2026, Kind = ArppIssueKind.Original, IssueSequence = 0, Name = issue.Name,
                IssueDate = issue.IssueDate, PublishedAtUtc = now, PublishedByUserId = "admin",
                AttachmentStorageKey = "k", AttachmentOriginalFileName = "a.pdf", AttachmentSizeBytes = 1, AttachmentSha256 = new string('0', 64)
            });
            db.ArppPublishedEntries.Add(new ArppPublishedEntry
            {
                ArppIssueId = issue.Id, SourceEntryId = 1, SerialNumber = "1", ProjectId = sharePer.Id, ProjectReference = "P-1", Category = ArppCategory.New, Cfa = "CFA", Fund = "Capital", DfpdsSchedule = "Sch 4"
            });
            await db.SaveChangesAsync();

            var service = new ProjectBriefingSelectionService(db, new FixedClock());
            async Task<string[]> Names(ProjectBriefingProjectFilter filter)
                => (await service.FilterAsync(filter)).Select(row => row.ProjectName).ToArray();

            // "%" and "_" match themselves.
            Assert.Equal(new[] { "Share 50% sim" }, await Names(new ProjectBriefingProjectFilter { Query = "50%" }));
            Assert.Equal(new[] { "Radar_Mk2" }, await Names(new ProjectBriefingProjectFilter { Query = "r_M" }));
            // The officer's name and rank are searched.
            Assert.Equal(new[] { "Share 50% sim" }, await Names(new ProjectBriefingProjectFilter { Query = "porwal" }));
            // A parent category takes in its inactive sub-category; chips combine.
            Assert.Equal(new[] { "Radar_Mk2", "Share 50% sim" }, await Names(new ProjectBriefingProjectFilter { TechnicalCategoryIds = new[] { vr.Id } }));
            Assert.Equal(new[] { "Radar_Mk2" }, await Names(new ProjectBriefingProjectFilter
            {
                TechnicalCategoryIds = new[] { vr.Id },
                Statuses = new[] { ProjectLifecycleStatus.Completed },
                CompletedFromFinancialYear = 2025,
                AvailableForProliferation = true
            }));
            Assert.Equal(new[] { "Share 50 per sim" }, await Names(new ProjectBriefingProjectFilter { ArppFinancialYears = new[] { 2026 } }));

            var rows = await service.FilterAsync(new ProjectBriefingProjectFilter { Query = "porwal" });
            Assert.Equal("Lt Col Anupam Porwal", rows.Single().ProjectOfficer);

            var options = await service.GetOptionsAsync();
            Assert.Equal(2, options.TechnicalCategories.Single(category => category.Name == "PR4 VR").MatchCount);
            Assert.Equal(1, options.ArppYears.Single(year => year.FinancialYearStart == 2026).ProjectCount);
        });
    }

    private static async Task WithDatabaseAsync(Func<ApplicationDbContext, Task> test)
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_briefingfilter_{Guid.NewGuid():N}"[..44];
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

    private sealed class FixedClock : ProjectManagement.Services.IClock
    {
        public DateTimeOffset UtcNow => new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class BriefingFilterPostgresFactAttribute : FactAttribute
{
    public BriefingFilterPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")))
        {
            Skip = "Provide PRISM_TEST_POSTGRES_CONNECTION to run the briefing filter PostgreSQL test.";
        }
    }
}
