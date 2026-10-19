using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.Tests.Stages;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Phase D-2 on PostgreSQL, after the full migration chain: the physical
/// Actual-correction proposal schema, its constraints as PostgreSQL enforces them,
/// and project purge with linked revisions and items.
/// </summary>
internal static class ActualCorrectionSchemaPostgresFixture
{
    private const string Seeder = "d2-seed";

    internal static async Task RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using (var seed = new ApplicationDbContext(options))
        {
            seed.Users.Add(new ApplicationUser { Id = Seeder, UserName = Seeder });
            await seed.SaveChangesAsync();
        }

        await PhysicalSchemaMatchesTheApprovedDesignAsync(options);
        await ConstraintsAreEnforcedByPostgresAsync(options);
        await PurgeRemovesLinkedRevisionsAndItemsAsync(options);
    }

    private static async Task PhysicalSchemaMatchesTheApprovedDesignAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);

        var revisionColumns = await ColumnsAsync(db, "ActualCorrectionRevisions");
        Assert.Equal(
            new Dictionary<string, (bool, string, int?)>
            {
                ["Id"] = (false, "integer", null),
                ["ProjectId"] = (false, "integer", null),
                ["RevisionNo"] = (false, "integer", null),
                ["Status"] = (false, "character varying", 16),
                ["EntryPoint"] = (false, "character varying", 16),
                ["SubmittedByUserId"] = (false, "character varying", 450),
                ["SubmittedOn"] = (false, "timestamp with time zone", null),
                ["Note"] = (true, "character varying", 1024),
                ["ContentHash"] = (false, "character varying", 64),
                ["SupersedesRevisionId"] = (true, "integer", null),
                ["SupersededByRevisionId"] = (true, "integer", null),
                ["ClosedByUserId"] = (true, "character varying", 450),
                ["ClosedOn"] = (true, "timestamp with time zone", null),
                ["DecisionNote"] = (true, "character varying", 2000)
            },
            revisionColumns);

        var itemColumns = await ColumnsAsync(db, "ActualCorrectionItems");
        Assert.Equal(
            new Dictionary<string, (bool, string, int?)>
            {
                ["Id"] = (false, "integer", null),
                ["RevisionId"] = (false, "integer", null),
                ["StageCode"] = (false, "character varying", 16),
                ["Kind"] = (false, "character varying", 16),
                ["BeforeStatus"] = (false, "character varying", 32),
                ["BeforeActualStart"] = (true, "date", null),
                ["BeforeCompletedOn"] = (true, "date", null),
                ["BeforeRequiresBackfill"] = (false, "boolean", null),
                ["ProposedActualStart"] = (true, "date", null),
                ["ProposedCompletedOn"] = (true, "date", null),
                ["ClearsActualStart"] = (false, "boolean", null),
                ["Note"] = (true, "character varying", 1024)
            },
            itemColumns);

        var pendingIndex = await db.Database
            .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'ux_actualcorrectionrevisions_pending'")
            .SingleAsync();
        Assert.Contains("CREATE UNIQUE INDEX", pendingIndex, StringComparison.Ordinal);
        Assert.Contains("(\"ProjectId\")", pendingIndex, StringComparison.Ordinal);
        Assert.Contains("WHERE ((\"Status\")::text = 'Pending'::text)", pendingIndex, StringComparison.Ordinal);
        foreach (var unique in new[] { "ux_actualcorrectionrevisions_project_revision", "ux_actualcorrectionitems_revision_stage" })
        {
            var definition = await db.Database
                .SqlQueryRaw<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = '{unique}'")
                .SingleAsync();
            Assert.Contains("CREATE UNIQUE INDEX", definition, StringComparison.Ordinal);
        }

        var deleteRules = (await db.Database
                .SqlQueryRaw<string>(
                    "SELECT constraint_name || '=' || delete_rule AS \"Value\" FROM information_schema.referential_constraints " +
                    "WHERE constraint_name LIKE 'FK_ActualCorrection%'")
                .ToListAsync())
            .OrderBy(rule => rule, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "FK_ActualCorrectionItems_ActualCorrectionRevisions_RevisionId=RESTRICT",
                "FK_ActualCorrectionRevisions_Projects_ProjectId=CASCADE",
                "FK_ActualCorrectionRevisions_SupersededByRevision=RESTRICT",
                "FK_ActualCorrectionRevisions_SupersedesRevision=RESTRICT"
            },
            deleteRules);

        var checks = (await db.Database
                .SqlQueryRaw<string>(
                    "SELECT conname AS \"Value\" FROM pg_constraint WHERE contype = 'c' AND conname LIKE 'CK_ActualCorrection%'")
                .ToListAsync())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "CK_ActualCorrectionItems_ClearsActualStart", "CK_ActualCorrectionItems_Kind",
                "CK_ActualCorrectionRevisions_Closure", "CK_ActualCorrectionRevisions_EntryPoint",
                "CK_ActualCorrectionRevisions_RevisionNo", "CK_ActualCorrectionRevisions_Status"
            },
            checks);
    }

    private static async Task ConstraintsAreEnforcedByPostgresAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-2 constraints", deleted: false);
        await using (var db = new ApplicationDbContext(options))
        {
            await ActualCorrectionSchemaTests.SeedSupersededPairAsync(db, projectId);
        }

        // One pending revision per project.
        await AssertPostgresErrorAsync(options, PostgresErrorCodes.UniqueViolation,
            db => db.ActualCorrectionRevisions.Add(ActualCorrectionSchemaTests.Revision(projectId, 3)));
        // Approved status values only.
        await AssertPostgresErrorAsync(options, PostgresErrorCodes.CheckViolation, db =>
        {
            var revision = ActualCorrectionSchemaTests.Revision(projectId, 4);
            revision.Status = "Draft";
            db.ActualCorrectionRevisions.Add(revision);
        });
        // A terminal revision records who closed it and when.
        await AssertPostgresErrorAsync(options, PostgresErrorCodes.CheckViolation, db =>
        {
            var revision = ActualCorrectionSchemaTests.Revision(projectId, 4);
            revision.Status = ActualCorrectionRevisionStatus.Rejected;
            db.ActualCorrectionRevisions.Add(revision);
        });
        // Items are evidence: a revision with items cannot be deleted on its own.
        await using (var db = new ApplicationDbContext(options))
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"ActualCorrectionRevisions\" WHERE \"ProjectId\" = {projectId} AND \"Status\" = 'Pending'"));
            Assert.Contains(failure.SqlState, new[] { PostgresErrorCodes.RestrictViolation, PostgresErrorCodes.ForeignKeyViolation });
        }

        await using (var verify = new ApplicationDbContext(options))
        {
            Assert.Equal(2, await verify.ActualCorrectionRevisions.CountAsync(revision => revision.ProjectId == projectId));
            Assert.Equal(2, await verify.ActualCorrectionItems.CountAsync(item => item.Revision!.ProjectId == projectId));
        }
    }

    private static async Task PurgeRemovesLinkedRevisionsAndItemsAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-2 purge", deleted: true);
        await using (var db = new ApplicationDbContext(options))
        {
            await ActualCorrectionSchemaTests.SeedSupersededPairAsync(db, projectId);
        }

        await using (var db = new ApplicationDbContext(options))
        {
            var service = new ProjectModerationService(
                db,
                FakeClock.AtUtc(DateTimeOffset.UtcNow),
                NullLogger<ProjectModerationService>.Instance,
                new NoUploadRoot(),
                Options.Create(new ProjectDocumentOptions()),
                new NoAudit());
            var result = await service.PurgeAsync(projectId, Seeder, includeAssets: false);
            Assert.Equal(ProjectModerationStatus.Success, result.Status);
        }

        await using var verify = new ApplicationDbContext(options);
        Assert.False(await verify.Projects.AnyAsync(project => project.Id == projectId));
        Assert.False(await verify.ActualCorrectionRevisions.AnyAsync(revision => revision.ProjectId == projectId));
    }

    // SECTION: Helpers
    private static async Task<Dictionary<string, (bool Nullable, string Type, int? Length)>> ColumnsAsync(
        ApplicationDbContext db,
        string table)
    {
        var rows = await db.Database
            .SqlQueryRaw<string>(
                "SELECT column_name || '|' || is_nullable || '|' || data_type || '|' || COALESCE(character_maximum_length::text, '') AS \"Value\" " +
                "FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = {0}",
                table)
            .ToListAsync();
        return rows
            .Select(row => row.Split('|'))
            .ToDictionary(
                parts => parts[0],
                parts => (parts[1] == "YES", parts[2], string.IsNullOrEmpty(parts[3]) ? (int?)null : int.Parse(parts[3])));
    }

    private static async Task<int> SeedProjectAsync(DbContextOptions<ApplicationDbContext> options, string name, bool deleted)
    {
        await using var db = new ApplicationDbContext(options);
        var project = new Project
        {
            Name = name,
            CreatedByUserId = Seeder,
            WorkflowVersion = ProcurementWorkflow.VersionV1,
            RowVersion = Array.Empty<byte>(),
            IsDeleted = deleted,
            DeletedAt = deleted ? DateTimeOffset.UtcNow : null,
            DeleteReason = deleted ? "D-2 purge test" : null
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static async Task AssertPostgresErrorAsync(
        DbContextOptions<ApplicationDbContext> options,
        string sqlState,
        Action<ApplicationDbContext> write)
    {
        await using var db = new ApplicationDbContext(options);
        write(db);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(sqlState, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    /// <summary>Purge runs with includeAssets: false, so no path is ever touched.</summary>
    private sealed class NoUploadRoot : IUploadRootProvider
    {
        private static readonly string Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "prism-d2-purge");

        public string RootPath => Root;

        public string GetProjectRoot(int projectId) => System.IO.Path.Combine(Root, projectId.ToString());

        public string GetProjectPhotosRoot(int projectId) => System.IO.Path.Combine(GetProjectRoot(projectId), "photos");

        public string GetProjectDocumentsRoot(int projectId) => System.IO.Path.Combine(GetProjectRoot(projectId), "docs");

        public string GetProjectCommentsRoot(int projectId) => System.IO.Path.Combine(GetProjectRoot(projectId), "comments");

        public string GetProjectVideosRoot(int projectId) => System.IO.Path.Combine(GetProjectRoot(projectId), "videos");

        public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => System.IO.Path.Combine(Root, "social", eventId.ToString());
    }

    private sealed class NoAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null,
            string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null) => Task.CompletedTask;
    }
}
