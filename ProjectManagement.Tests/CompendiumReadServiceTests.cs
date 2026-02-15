using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.Compendiums.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Tests;

public sealed class CompendiumReadServiceTests
{
    [Fact]
    public async Task GetEligibleProjectsAsync_AllowsNullCoverPhotoVersionFromDatabase()
    {
        // SECTION: Arrange sqlite schema with nullable cover photo version column
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var schemaContext = CreateSqliteContext(connection))
        {
            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE Projects (
                    Id INTEGER NOT NULL CONSTRAINT PK_Projects PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Description TEXT NULL,
                    CompletedYear INTEGER NULL,
                    CompletedOn TEXT NULL,
                    SponsoringLineDirectorateId INTEGER NULL,
                    ArmService TEXT NULL,
                    CoverPhotoId INTEGER NULL,
                    CoverPhotoVersion INTEGER NULL,
                    CostLakhs TEXT NULL,
                    IsDeleted INTEGER NOT NULL,
                    IsArchived INTEGER NOT NULL,
                    LifecycleStatus TEXT NOT NULL
                );
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE ProjectTechStatuses (
                    ProjectId INTEGER NOT NULL CONSTRAINT PK_ProjectTechStatuses PRIMARY KEY,
                    TechStatus TEXT NOT NULL,
                    AvailableForProliferation INTEGER NOT NULL,
                    NotAvailableReason TEXT NULL,
                    Remarks TEXT NULL,
                    MarkedAtUtc TEXT NOT NULL,
                    MarkedByUserId TEXT NOT NULL
                );
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE ProjectProductionCostFacts (
                    ProjectId INTEGER NOT NULL CONSTRAINT PK_ProjectProductionCostFacts PRIMARY KEY,
                    ApproxProductionCost TEXT NULL,
                    Remarks TEXT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    UpdatedByUserId TEXT NOT NULL
                );
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE ProjectTots (
                    Id INTEGER NOT NULL CONSTRAINT PK_ProjectTots PRIMARY KEY,
                    ProjectId INTEGER NOT NULL,
                    Status INTEGER NOT NULL,
                    CompletedOn TEXT NULL
                );
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Projects (
                    Id, Name, Description, CompletedYear, CompletedOn, SponsoringLineDirectorateId,
                    ArmService, CoverPhotoId, CoverPhotoVersion, CostLakhs, IsDeleted, IsArchived, LifecycleStatus)
                VALUES (101, 'Project Null Cover Version', NULL, 2025, NULL, NULL, 'Navy', NULL, NULL, 50, 0, 0, 'Completed');
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO ProjectTechStatuses (
                    ProjectId, TechStatus, AvailableForProliferation, NotAvailableReason, Remarks, MarkedAtUtc, MarkedByUserId)
                VALUES (101, 'Current', 1, NULL, NULL, '2026-01-01T00:00:00Z', 'seed-user');
                """);
        }

        await using var assertionContext = CreateSqliteContext(connection);
        var service = new CompendiumReadService(
            assertionContext,
            new NoOpProjectPhotoService(),
            new ZeroProliferationMetricsService(),
            NullLogger<CompendiumReadService>.Instance);

        // SECTION: Act
        var projects = await service.GetEligibleProjectsAsync(CancellationToken.None);

        // SECTION: Assert
        var project = Assert.Single(projects);
        Assert.Equal(101, project.ProjectId);
        Assert.Null(project.CoverPhotoVersion);
    }

    [Fact]
    public async Task GetEligibleProjectsAsync_LoadsIndexListWhenLegacyProjectTotStatusIsNull()
    {
        // SECTION: Arrange sqlite schema with legacy null TOT status value in ProjectTots
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var schemaContext = CreateSqliteContext(connection))
        {
            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE Projects (
                    Id INTEGER NOT NULL CONSTRAINT PK_Projects PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Description TEXT NULL,
                    CompletedYear INTEGER NULL,
                    CompletedOn TEXT NULL,
                    SponsoringLineDirectorateId INTEGER NULL,
                    ArmService TEXT NULL,
                    CoverPhotoId INTEGER NULL,
                    CoverPhotoVersion INTEGER NULL,
                    CostLakhs TEXT NULL,
                    IsDeleted INTEGER NOT NULL,
                    IsArchived INTEGER NOT NULL,
                    LifecycleStatus TEXT NOT NULL
                );
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE ProjectTechStatuses (
                    ProjectId INTEGER NOT NULL CONSTRAINT PK_ProjectTechStatuses PRIMARY KEY,
                    TechStatus TEXT NOT NULL,
                    AvailableForProliferation INTEGER NOT NULL,
                    NotAvailableReason TEXT NULL,
                    Remarks TEXT NULL,
                    MarkedAtUtc TEXT NOT NULL,
                    MarkedByUserId TEXT NOT NULL
                );
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE ProjectProductionCostFacts (
                    ProjectId INTEGER NOT NULL CONSTRAINT PK_ProjectProductionCostFacts PRIMARY KEY,
                    ApproxProductionCost TEXT NULL,
                    Remarks TEXT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    UpdatedByUserId TEXT NOT NULL
                );
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE ProjectTots (
                    Id INTEGER NOT NULL CONSTRAINT PK_ProjectTots PRIMARY KEY,
                    ProjectId INTEGER NOT NULL,
                    Status INTEGER NULL,
                    CompletedOn TEXT NULL
                );
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Projects (
                    Id, Name, Description, CompletedYear, CompletedOn, SponsoringLineDirectorateId,
                    ArmService, CoverPhotoId, CoverPhotoVersion, CostLakhs, IsDeleted, IsArchived, LifecycleStatus)
                VALUES (201, 'Project Null TOT Status', NULL, 2024, NULL, NULL, 'Army', NULL, NULL, 75, 0, 0, 'Completed');
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO ProjectTechStatuses (
                    ProjectId, TechStatus, AvailableForProliferation, NotAvailableReason, Remarks, MarkedAtUtc, MarkedByUserId)
                VALUES (201, 'Current', 1, NULL, NULL, '2026-01-01T00:00:00Z', 'seed-user');
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO ProjectTots (Id, ProjectId, Status, CompletedOn)
                VALUES (1, 201, NULL, NULL);
                """);
        }

        await using var assertionContext = CreateSqliteContext(connection);
        var service = new CompendiumReadService(
            assertionContext,
            new NoOpProjectPhotoService(),
            new ZeroProliferationMetricsService(),
            NullLogger<CompendiumReadService>.Instance);

        // SECTION: Act
        var projects = await service.GetEligibleProjectsAsync(CancellationToken.None);

        // SECTION: Assert
        var project = Assert.Single(projects);
        Assert.Equal(201, project.ProjectId);
        Assert.Equal("Project Null TOT Status", project.Name);
    }

    [Fact]
    public async Task GetEligibleProjectsAsync_AllowsNullCompletedYearAndCompletedOn()
    {
        // SECTION: Arrange sqlite schema with null completion fields for ordering fallback path
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var schemaContext = CreateSqliteContext(connection))
        {
            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE Projects (
                    Id INTEGER NOT NULL CONSTRAINT PK_Projects PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Description TEXT NULL,
                    CompletedYear INTEGER NULL,
                    CompletedOn TEXT NULL,
                    SponsoringLineDirectorateId INTEGER NULL,
                    ArmService TEXT NULL,
                    CoverPhotoId INTEGER NULL,
                    CoverPhotoVersion INTEGER NULL,
                    CostLakhs TEXT NULL,
                    IsDeleted INTEGER NOT NULL,
                    IsArchived INTEGER NOT NULL,
                    LifecycleStatus TEXT NOT NULL
                );
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE ProjectTechStatuses (
                    ProjectId INTEGER NOT NULL CONSTRAINT PK_ProjectTechStatuses PRIMARY KEY,
                    TechStatus TEXT NOT NULL,
                    AvailableForProliferation INTEGER NOT NULL,
                    NotAvailableReason TEXT NULL,
                    Remarks TEXT NULL,
                    MarkedAtUtc TEXT NOT NULL,
                    MarkedByUserId TEXT NOT NULL
                );
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE ProjectProductionCostFacts (
                    ProjectId INTEGER NOT NULL CONSTRAINT PK_ProjectProductionCostFacts PRIMARY KEY,
                    ApproxProductionCost TEXT NULL,
                    Remarks TEXT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    UpdatedByUserId TEXT NOT NULL
                );
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE ProjectTots (
                    Id INTEGER NOT NULL CONSTRAINT PK_ProjectTots PRIMARY KEY,
                    ProjectId INTEGER NOT NULL,
                    Status INTEGER NOT NULL,
                    CompletedOn TEXT NULL
                );
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Projects (
                    Id, Name, Description, CompletedYear, CompletedOn, SponsoringLineDirectorateId,
                    ArmService, CoverPhotoId, CoverPhotoVersion, CostLakhs, IsDeleted, IsArchived, LifecycleStatus)
                VALUES (301, 'Project Null Completion Fields', NULL, NULL, NULL, NULL, 'Air Force', NULL, NULL, 90, 0, 0, 'Completed');
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO ProjectTechStatuses (
                    ProjectId, TechStatus, AvailableForProliferation, NotAvailableReason, Remarks, MarkedAtUtc, MarkedByUserId)
                VALUES (301, 'Current', 1, NULL, NULL, '2026-01-01T00:00:00Z', 'seed-user');
                """);
        }

        await using var assertionContext = CreateSqliteContext(connection);
        var service = new CompendiumReadService(
            assertionContext,
            new NoOpProjectPhotoService(),
            new ZeroProliferationMetricsService(),
            NullLogger<CompendiumReadService>.Instance);

        // SECTION: Act
        var projects = await service.GetEligibleProjectsAsync(CancellationToken.None);

        // SECTION: Assert
        var project = Assert.Single(projects);
        Assert.Equal(301, project.ProjectId);
        Assert.Equal("Not recorded", project.CompletionYearText);
    }

    [Fact]
    public async Task GetEligibleProjectsAsync_ToleratesLegacyNullsAndPreservesEligibilityFiltering()
    {
        // SECTION: Arrange sqlite schema and legacy-style nullable compendium columns
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var schemaContext = CreateSqliteContext(connection))
        {
            await CreateLegacyCompatibleCompendiumSchemaAsync(schemaContext);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Projects (
                    Id, Name, Description, CompletedYear, CompletedOn, SponsoringLineDirectorateId,
                    ArmService, CoverPhotoId, CoverPhotoVersion, CostLakhs, IsDeleted, IsArchived, LifecycleStatus)
                VALUES
                    (401, 'Eligible Legacy Null Flags', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 100, NULL, NULL, 'Completed'),
                    (402, 'Skip Deleted', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 100, 1, 0, 'Completed'),
                    (403, 'Skip Null Lifecycle', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 100, 0, 0, NULL),
                    (404, 'Skip Unknown Lifecycle', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 100, 0, 0, 'UnknownFutureValue');
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO ProjectTechStatuses (
                    ProjectId, TechStatus, AvailableForProliferation, NotAvailableReason, Remarks, MarkedAtUtc, MarkedByUserId)
                VALUES
                    (401, 'Current', 1, NULL, NULL, '2026-01-01T00:00:00Z', 'seed-user'),
                    (402, 'Current', 1, NULL, NULL, '2026-01-01T00:00:00Z', 'seed-user'),
                    (403, 'Current', NULL, NULL, NULL, '2026-01-01T00:00:00Z', 'seed-user'),
                    (404, 'Current', 1, NULL, NULL, '2026-01-01T00:00:00Z', 'seed-user');
                """);
        }

        await using var assertionContext = CreateSqliteContext(connection);
        var service = new CompendiumReadService(
            assertionContext,
            new NoOpProjectPhotoService(),
            new ZeroProliferationMetricsService(),
            NullLogger<CompendiumReadService>.Instance);

        // SECTION: Act + assert no nullable materialization failures
        var projects = await service.GetEligibleProjectsAsync(CancellationToken.None);

        // SECTION: Assert expected filtering and display normalization
        var project = Assert.Single(projects);
        Assert.Equal(401, project.ProjectId);
        Assert.Equal("Not recorded", project.ArmService);
    }

    [Fact]
    public async Task CompendiumPages_LoadSuccessfullyWithLegacyNullDataset()
    {
        // SECTION: Arrange shared service with legacy-like null values
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var schemaContext = CreateSqliteContext(connection))
        {
            await CreateLegacyCompatibleCompendiumSchemaAsync(schemaContext);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Projects (
                    Id, Name, Description, CompletedYear, CompletedOn, SponsoringLineDirectorateId,
                    ArmService, CoverPhotoId, CoverPhotoVersion, CostLakhs, IsDeleted, IsArchived, LifecycleStatus)
                VALUES
                    (501, 'Eligible Page Row', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 110, NULL, NULL, 'Completed'),
                    (502, 'Archived Page Row', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 120, 0, 1, 'Completed');
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO ProjectTechStatuses (
                    ProjectId, TechStatus, AvailableForProliferation, NotAvailableReason, Remarks, MarkedAtUtc, MarkedByUserId)
                VALUES
                    (501, 'Current', 1, NULL, NULL, '2026-01-01T00:00:00Z', 'seed-user'),
                    (502, 'Current', 1, NULL, NULL, '2026-01-01T00:00:00Z', 'seed-user');
                """);

            await schemaContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO ProjectTots (Id, ProjectId, Status, CompletedOn)
                VALUES (51, 501, NULL, NULL);
                """);
        }

        await using var assertionContext = CreateSqliteContext(connection);
        var service = new CompendiumReadService(
            assertionContext,
            new NoOpProjectPhotoService(),
            new ZeroProliferationMetricsService(),
            NullLogger<CompendiumReadService>.Instance);

        var proliferationPage = new ProjectManagement.Areas.Compendiums.Pages.Proliferation.IndexModel(
            service,
            new NoOpProliferationCompendiumPdfBuilder(),
            new TestWebHostEnvironment());

        var historicalPage = new ProjectManagement.Areas.Compendiums.Pages.Historical.IndexModel(
            service,
            new NoOpHistoricalCompendiumPdfBuilder(),
            new TestWebHostEnvironment());

        // SECTION: Act
        await proliferationPage.OnGetAsync(CancellationToken.None);
        await historicalPage.OnGetAsync(CancellationToken.None);

        // SECTION: Assert
        var proliferationProject = Assert.Single(proliferationPage.Projects);
        Assert.Equal(501, proliferationProject.ProjectId);
        var historicalProject = Assert.Single(historicalPage.Projects);
        Assert.Equal(501, historicalProject.ProjectId);
        Assert.True(historicalPage.TotalsByProject.ContainsKey(501));
    }

    // SECTION: Shared schema helper for legacy-null compendium coverage
    private static async Task CreateLegacyCompatibleCompendiumSchemaAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE Projects (
                Id INTEGER NOT NULL CONSTRAINT PK_Projects PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NULL,
                CompletedYear INTEGER NULL,
                CompletedOn TEXT NULL,
                SponsoringLineDirectorateId INTEGER NULL,
                ArmService TEXT NULL,
                CoverPhotoId INTEGER NULL,
                CoverPhotoVersion INTEGER NULL,
                CostLakhs TEXT NULL,
                IsDeleted INTEGER NULL,
                IsArchived INTEGER NULL,
                LifecycleStatus TEXT NULL
            );
            """);

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE ProjectTechStatuses (
                ProjectId INTEGER NOT NULL CONSTRAINT PK_ProjectTechStatuses PRIMARY KEY,
                TechStatus TEXT NOT NULL,
                AvailableForProliferation INTEGER NULL,
                NotAvailableReason TEXT NULL,
                Remarks TEXT NULL,
                MarkedAtUtc TEXT NOT NULL,
                MarkedByUserId TEXT NOT NULL
            );
            """);

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE ProjectProductionCostFacts (
                ProjectId INTEGER NOT NULL CONSTRAINT PK_ProjectProductionCostFacts PRIMARY KEY,
                ApproxProductionCost TEXT NULL,
                Remarks TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                UpdatedByUserId TEXT NOT NULL
            );
            """);

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE ProjectTots (
                Id INTEGER NOT NULL CONSTRAINT PK_ProjectTots PRIMARY KEY,
                ProjectId INTEGER NOT NULL,
                Status INTEGER NULL,
                CompletedOn TEXT NULL
            );
            """);
    }

    // SECTION: Shared sqlite context helper
    private static ApplicationDbContext CreateSqliteContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        return new ApplicationDbContext(options);
    }

    // SECTION: Test double for photo service (unused in list query path)
    private sealed class NoOpProjectPhotoService : IProjectPhotoService
    {
        public Task<ProjectPhoto> AddAsync(int projectId, Stream content, string originalFileName, string? contentType, string userId, bool setAsCover, string? caption, int? totId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectPhoto> AddAsync(int projectId, Stream content, string originalFileName, string? contentType, string userId, bool setAsCover, string? caption, ProjectPhotoCrop crop, int? totId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectPhoto?> ReplaceAsync(int projectId, int photoId, Stream content, string originalFileName, string? contentType, string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectPhoto?> ReplaceAsync(int projectId, int photoId, Stream content, string originalFileName, string? contentType, string userId, ProjectPhotoCrop crop, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectPhoto?> UpdateCaptionAsync(int projectId, int photoId, string? caption, string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectPhoto?> UpdateCropAsync(int projectId, int photoId, ProjectPhotoCrop crop, string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectPhoto?> UpdateTotAsync(int projectId, int photoId, int? totId, string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(int projectId, int photoId, string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReorderAsync(int projectId, IReadOnlyList<int> orderedPhotoIds, string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(Stream Stream, string ContentType)?> OpenDerivativeAsync(int projectId, int photoId, string sizeKey, bool preferWebp, CancellationToken cancellationToken) => Task.FromResult< (Stream Stream, string ContentType)?>(null);
        public Task<(Stream Stream, string ContentType)?> OpenDerivativeAsync(int projectId, int photoId, string sizeKey, string requestedFormat, CancellationToken cancellationToken) => Task.FromResult< (Stream Stream, string ContentType)?>(null);
        public string GetDerivativePath(ProjectPhoto photo, string sizeKey, bool preferWebp) => string.Empty;
    }

    // SECTION: Test double for metrics service (unused in list query path)
    private sealed class ZeroProliferationMetricsService : IProliferationMetricsService
    {
        public Task<int> GetAllTimeTotalAsync(int projectId, ProliferationSource source, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    // SECTION: Minimal PDF builder test doubles for page model construction
    private sealed class NoOpProliferationCompendiumPdfBuilder : Utilities.Reporting.IProliferationCompendiumPdfBuilder
    {
        public byte[] Build(Utilities.Reporting.ProliferationCompendiumPdfContext context) => Array.Empty<byte>();
    }

    private sealed class NoOpHistoricalCompendiumPdfBuilder : Utilities.Reporting.IHistoricalCompendiumPdfBuilder
    {
        public byte[] Build(Utilities.Reporting.HistoricalCompendiumPdfContext context) => Array.Empty<byte>();
    }

    // SECTION: Minimal hosting environment for page model constructor requirements
    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ProjectManagement.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
