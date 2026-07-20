using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Remarks;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcProjectTransactionTests
{
    [Fact]
    public async Task LinkedProjectSave_CommitsProjectAndNestedProgressOperationTogether()
    {
        await using var scope = await TestScope.CreateAsync();
        var seeded = await scope.SeedAsync();
        var progress = new NestedProgressService(scope.Db, throwAfterNestedCommit: false);
        var service = CreateService(scope.Db, progress);

        var result = await service.SaveAsync(CreateCommand(seeded.Record.Id, seeded.Project.Id));

        Assert.True(result.Success, result.Message);
        Assert.Single(await scope.Db.FfcProjects.AsNoTracking().ToListAsync());
        Assert.True(await scope.Db.FfcCountries.AsNoTracking().AnyAsync(country => country.IsoCode == "PG1"));
        Assert.Equal(1, progress.AfterCommitCount);
    }

    [Fact]
    public async Task LinkedProjectSave_WhenNestedProgressFails_RollsBackAllChangesAndClearsTrackedGhosts()
    {
        await using var scope = await TestScope.CreateAsync();
        var seeded = await scope.SeedAsync();
        var progress = new NestedProgressService(scope.Db, throwAfterNestedCommit: true);
        var service = CreateService(scope.Db, progress);

        var result = await service.SaveAsync(CreateCommand(seeded.Record.Id, seeded.Project.Id));

        Assert.False(result.Success);
        Assert.Contains("No changes were committed", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reference: FFC-", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(scope.Db.ChangeTracker.Entries<FfcProject>());
        Assert.Empty(await scope.Db.FfcProjects.AsNoTracking().ToListAsync());
        Assert.False(await scope.Db.FfcCountries.AsNoTracking().AnyAsync(country => country.IsoCode == "PG1"));
        Assert.Equal(0, progress.AfterCommitCount);
    }

    private static FfcProjectCommandService CreateService(
        ApplicationDbContext db,
        IFfcProgressService progressService)
        => new(
            db,
            progressService,
            new NoOpAuditService(),
            new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "ffc-transaction-test"
                }
            },
            NullLogger<FfcProjectCommandService>.Instance);

    private static FfcProjectSaveCommand CreateCommand(long recordId, int projectId)
        => new(
            RecordId: recordId,
            ProjectId: null,
            IsLinkedProject: true,
            DisplayName: "Linked simulator",
            LinkedProjectId: projectId,
            Quantity: 1,
            Position: FfcUnitPosition.Planned,
            DeliveredOn: null,
            InstalledOn: null,
            ProgressText: "Initial external progress.",
            RowVersion: null,
            Actor: new RemarkActorContext(
                "admin",
                RemarkActorRole.Administrator,
                new[] { RemarkActorRole.Administrator }));

    private sealed class NestedProgressService(
        ApplicationDbContext db,
        bool throwAfterNestedCommit) : IFfcProgressService
    {
        public int AfterCommitCount { get; private set; }

        public Task<IReadOnlyDictionary<long, FfcProgressSnapshot>> GetCurrentProgressAsync(
            IReadOnlyCollection<FfcProgressTarget> targets,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<long, FfcProgressSnapshot>>(
                new Dictionary<long, FfcProgressSnapshot>());

        public async Task<FfcProgressUpdateResult> UpdateProgressAsync(
            FfcProgressUpdateCommand command,
            CancellationToken cancellationToken = default)
        {
            await using var nested = await RelationalTransactionScope.CreateAsync(
                db.Database,
                cancellationToken);

            db.FfcCountries.Add(new FfcCountry
            {
                Name = "Nested progress side effect",
                IsoCode = "PG1",
                IsActive = true
            });
            await db.SaveChangesAsync(cancellationToken);

            nested.RegisterAfterCommit(_ =>
            {
                AfterCommitCount++;
                return Task.CompletedTask;
            });

            await nested.CommitAsync(cancellationToken);

            if (throwAfterNestedCommit)
            {
                throw new InvalidOperationException("Forced failure after nested progress savepoint commit.");
            }

            return new FfcProgressUpdateResult(
                command.FfcProjectId,
                command.ProgressText ?? string.Empty,
                ExternalRemarkId: 1,
                FfcProgressSource.ExternalProjectRemark,
                DateTimeOffset.UtcNow);
        }
    }

    private sealed class NoOpAuditService : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
            => Task.CompletedTask;
    }

    private sealed class TestScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestScope(ApplicationDbContext db, SqliteConnection connection)
        {
            Db = db;
            _connection = connection;
        }

        public ApplicationDbContext Db { get; }

        public static async Task<TestScope> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new TestScope(db, connection);
        }

        public async Task<(FfcRecord Record, Project Project)> SeedAsync()
        {
            var country = new FfcCountry
            {
                Name = "Seedland",
                IsoCode = "SED",
                IsActive = true
            };
            var project = new Project
            {
                Name = "Linked PRISM Project",
                CreatedByUserId = "admin",
                LifecycleStatus = ProjectLifecycleStatus.Active
            };
            Db.AddRange(country, project);
            await Db.SaveChangesAsync();

            var record = new FfcRecord
            {
                CountryId = country.Id,
                Year = 2026
            };
            Db.FfcRecords.Add(record);
            await Db.SaveChangesAsync();
            return (record, project);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
