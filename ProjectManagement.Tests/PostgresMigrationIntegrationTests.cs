using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Executes the real PostgreSQL migration chain when the guarded integration-test
/// connection is supplied. The test is skipped during ordinary local unit-test runs.
/// </summary>
public sealed class PostgresMigrationIntegrationTests
{
    [PostgresMigrationFact]
    public async Task CompleteStartupBoundary_MigratesAndValidatesBothContexts()
    {
        var connectionString = Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!;
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = Assert.IsType<string>(builder.Database);
        Assert.StartsWith(
            "prism_test_",
            databaseName,
            StringComparison.OrdinalIgnoreCase);

        await AssertDatabaseIsEmptyAsync(connectionString);

        var applicationOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var mediaOptions = new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(MediaLibraryDbContext.MigrationsHistoryTable))
            .Options;

        await using var applicationDb = new ApplicationDbContext(applicationOptions);
        await using var mediaDb = new MediaLibraryDbContext(mediaOptions);
        var mediaSchema = new MediaLibrarySchemaService(
            mediaDb,
            new MediaLibrarySchemaStatusCache(),
            NullLogger<MediaLibrarySchemaService>.Instance);
        var applicationManifest = ReadManifest("application-migration-ids.txt");
        var mediaManifest = ReadManifest("media-migration-ids.txt");

        // Preflight must validate every migration lineage before mutating either context.
        // A database-ahead Media history must therefore prevent even the pending Application
        // migration chain from beginning.
        await SeedUnknownMediaHistoryAsync(connectionString);
        var preflightException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseStartupMigrator.ApplyDeploymentBoundaryAsync(
                applicationDb,
                NullLogger.Instance,
                new[]
                {
                    new DatabaseStartupMigrationPlan(
                        applicationDb,
                        "ApplicationDbContext",
                        true,
                        ExpectedMigrationIds: applicationManifest),
                    new DatabaseStartupMigrationPlan(
                        mediaDb,
                        "MediaLibraryDbContext",
                        true,
                        ExpectedMigrationIds: mediaManifest)
                }));
        Assert.Contains("not present in the deployed assembly", preflightException.Message);
        Assert.False(await HistoryTableExistsAsync(connectionString, "__EFMigrationsHistory"));
        await DropMediaHistoryAsync(connectionString);

        // Reproduce the July 2026 production incident: every earlier migration, including
        // the reconciliation migration, is recorded as applied, but legacy startup SQL
        // subsequently recreates the obsolete constraint. The final forward migration must
        // repair that physical drift even though the earlier repair IDs are already present.
        var migrator = applicationDb.GetService<IMigrator>();
        await migrator.MigrateAsync(
            "20261201150000_ReconcileProjectStageCompletionConstraint",
            CancellationToken.None);
        await SeedLegacyProjectStageConstraintDriftAsync(applicationDb);

        var results = await DatabaseStartupMigrator.ApplyDeploymentBoundaryAsync(
            applicationDb,
            NullLogger.Instance,
            new[]
            {
                new DatabaseStartupMigrationPlan(
                    applicationDb,
                    "ApplicationDbContext",
                    true,
                    async cancellationToken =>
                    {
                        await ApplicationDatabaseSchemaValidator.ValidateAsync(
                            applicationDb,
                            NullLogger.Instance,
                            cancellationToken);
                        await ProjectDocumentSearchVectorMaintenance.ValidateAsync(
                            applicationDb,
                            cancellationToken);
                    },
                    applicationManifest),
                new DatabaseStartupMigrationPlan(
                    mediaDb,
                    "MediaLibraryDbContext",
                    true,
                    async cancellationToken =>
                    {
                        var status = await mediaSchema.GetStatusAsync(cancellationToken);
                        Assert.True(status.IsCurrent, status.Error);
                    },
                    mediaManifest)
            });

        Assert.Equal(2, results.Count);
        Assert.Empty(await applicationDb.Database.GetPendingMigrationsAsync());
        Assert.Empty(await mediaDb.Database.GetPendingMigrationsAsync());
        Assert.Equal(
            applicationDb.Database.GetMigrations().Last(),
            results[0].LatestAppliedMigration);
        Assert.Equal(
            mediaDb.Database.GetMigrations().Last(),
            results[1].LatestAppliedMigration);

        await VerifyProjectStageCompletionConstraintAsync(applicationDb);
    }


    private static async Task SeedLegacyProjectStageConstraintDriftAsync(
        ApplicationDbContext applicationDb)
    {
        var project = new Project
        {
            Name = "Legacy constraint drift project",
            CreatedByUserId = "migration-test",
            WorkflowVersion = "v1",
            RowVersion = Array.Empty<byte>()
        };
        applicationDb.Projects.Add(project);
        await applicationDb.SaveChangesAsync();

        applicationDb.ProjectStages.Add(new ProjectStage
        {
            ProjectId = project.Id,
            StageCode = "TEC",
            SortOrder = 1,
            Status = StageStatus.Completed,
            ActualStart = null,
            CompletedOn = new DateOnly(2026, 6, 23),
            RequiresBackfill = false
        });
        await applicationDb.SaveChangesAsync();

        await applicationDb.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "ProjectStages"
                DROP CONSTRAINT IF EXISTS "CK_ProjectStages_CompletedHasDate";

            ALTER TABLE "ProjectStages"
                ALTER COLUMN "RequiresBackfill" DROP NOT NULL;

            UPDATE "ProjectStages"
            SET "RequiresBackfill" = NULL
            WHERE "ProjectId" = {0}
              AND "StageCode" = 'TEC';

            ALTER TABLE "ProjectStages"
                ADD CONSTRAINT "CK_ProjectStages_CompletedHasDate"
                CHECK (
                    "Status" <> 'Completed'
                    OR (
                        "CompletedOn" IS NOT NULL
                        AND "ActualStart" IS NOT NULL
                    )
                    OR "RequiresBackfill" IS TRUE
                );
            """,
            project.Id);

        applicationDb.ChangeTracker.Clear();
    }

    private static async Task VerifyProjectStageCompletionConstraintAsync(
        ApplicationDbContext applicationDb)
    {
        var project = new Project
        {
            Name = "Migration integration project",
            CreatedByUserId = "migration-test",
            WorkflowVersion = "v1",
            RowVersion = Array.Empty<byte>()
        };
        applicationDb.Projects.Add(project);
        await applicationDb.SaveChangesAsync();

        applicationDb.ProjectStages.Add(new ProjectStage
        {
            ProjectId = project.Id,
            StageCode = "TEC",
            SortOrder = 1,
            Status = StageStatus.Completed,
            ActualStart = null,
            CompletedOn = new DateOnly(2026, 6, 23),
            RequiresBackfill = false
        });
        await applicationDb.SaveChangesAsync();

        applicationDb.ProjectStages.Add(new ProjectStage
        {
            ProjectId = project.Id,
            StageCode = "BM",
            SortOrder = 2,
            Status = StageStatus.Completed,
            ActualStart = null,
            CompletedOn = null,
            RequiresBackfill = false
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => applicationDb.SaveChangesAsync());
        applicationDb.ChangeTracker.Clear();
    }

    private static IReadOnlyList<string> ReadManifest(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        Assert.True(File.Exists(path), $"Migration manifest is missing: {path}");
        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
    }

    private static async Task SeedUnknownMediaHistoryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE "{MediaLibraryDbContext.MigrationsHistoryTable}" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK_{MediaLibraryDbContext.MigrationsHistoryTable}" PRIMARY KEY ("MigrationId")
            );
            INSERT INTO "{MediaLibraryDbContext.MigrationsHistoryTable}" ("MigrationId", "ProductVersion")
            VALUES ('20990101000000_UnknownFutureMigration', '8.0.19');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropMediaHistoryAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS \"{MediaLibraryDbContext.MigrationsHistoryTable}\";";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> HistoryTableExistsAsync(
        string connectionString,
        string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT to_regclass(format('%I.%I', current_schema(), @tableName)) IS NOT NULL;
            """;
        command.Parameters.AddWithValue("tableName", tableName);
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    private static async Task AssertDatabaseIsEmptyAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = current_schema()
              AND table_type = 'BASE TABLE';
            """;
        var tableCount = Convert.ToInt32(await command.ExecuteScalarAsync());
        Assert.True(
            tableCount == 0,
            "PRISM_TEST_POSTGRES_CONNECTION must point to a dedicated empty database. " +
            $"The selected database already contains {tableCount} table(s). No destructive reset was attempted.");
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class PostgresMigrationFactAttribute : FactAttribute
{
    public PostgresMigrationFactAttribute()
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable("PRISM_RUN_POSTGRES_MIGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var connection = Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION");

        if (!enabled || string.IsNullOrWhiteSpace(connection))
        {
            Skip =
                "Set PRISM_RUN_POSTGRES_MIGRATION_TESTS=true and provide PRISM_TEST_POSTGRES_CONNECTION to run the production migration-chain test.";
        }
    }
}
