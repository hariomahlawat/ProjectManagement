using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
                    LifecycleStatus INTEGER NOT NULL
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
                VALUES (101, 'Project Null Cover Version', NULL, 2025, NULL, NULL, 'Navy', NULL, NULL, 50, 0, 0, 2);
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
            new ZeroProliferationMetricsService());

        // SECTION: Act
        var projects = await service.GetEligibleProjectsAsync(CancellationToken.None);

        // SECTION: Assert
        var project = Assert.Single(projects);
        Assert.Equal(101, project.ProjectId);
        Assert.Null(project.CoverPhotoVersion);
    }

    [Fact]
    public async Task GetEligibleProjectsAsync_AllowsLegacyNullTotStatusFromDatabase()
    {
        // SECTION: Arrange sqlite schema with nullable TOT status column and legacy null value
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
                    LifecycleStatus INTEGER NOT NULL
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
                VALUES (201, 'Project Null TOT Status', NULL, 2024, NULL, NULL, 'Army', NULL, NULL, 75, 0, 0, 2);
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
            new ZeroProliferationMetricsService());

        // SECTION: Act
        var projects = await service.GetEligibleProjectsAsync(CancellationToken.None);

        // SECTION: Assert
        var project = Assert.Single(projects);
        Assert.Equal(201, project.ProjectId);
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
}
