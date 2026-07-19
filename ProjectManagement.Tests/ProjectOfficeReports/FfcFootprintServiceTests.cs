using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Ffc;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcFootprintServiceTests
{
    [Fact]
    public async Task GetAsync_AggregatesMultipleCountryYearsAndBatchResolvesProgress()
    {
        await using var scope = await TestDbScope.CreateAsync();
        var db = scope.Db;
        var country = new FfcCountry { Name = "Myanmar", IsoCode = "MMR", IsActive = true };
        var linked = new Project
        {
            Name = "Universal Driving Simulator",
            CreatedByUserId = "user",
            LifecycleStatus = ProjectLifecycleStatus.Active
        };
        db.AddRange(country, linked);
        await db.SaveChangesAsync();

        var current = new FfcRecord
        {
            CountryId = country.Id,
            Year = 2026,
            OverallRemarks = "Current year position",
            UpdatedAt = new DateTimeOffset(2026, 7, 19, 0, 0, 0, TimeSpan.Zero)
        };
        var historic = new FfcRecord
        {
            CountryId = country.Id,
            Year = 2024,
            UpdatedAt = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero)
        };
        db.FfcRecords.AddRange(current, historic);
        await db.SaveChangesAsync();

        var installed = new FfcProject
        {
            FfcRecordId = current.Id,
            Name = "UDS Myanmar",
            LinkedProjectId = linked.Id,
            Quantity = 2,
            IsDelivered = true,
            IsInstalled = true
        };
        var planned = new FfcProject
        {
            FfcRecordId = current.Id,
            Name = "Planned item",
            Remarks = "Awaiting sanction",
            Quantity = 3
        };
        var delivered = new FfcProject
        {
            FfcRecordId = historic.Id,
            Name = "Historic delivery",
            Quantity = 1,
            IsDelivered = true
        };
        db.FfcProjects.AddRange(installed, planned, delivered);
        await db.SaveChangesAsync();

        var progress = new StubProgressService(new Dictionary<long, string?>
        {
            [installed.Id] = "Canonical Project external progress"
        });
        var service = new FfcFootprintService(db, progress);

        var result = await service.GetAsync(new FfcFootprintRequest());

        Assert.Equal(1, result.Summary.CountryCount);
        Assert.Equal(2, result.Summary.RecordCount);
        Assert.Equal(3, result.Summary.ProjectCount);
        Assert.Equal(2, result.Summary.InstalledUnits);
        Assert.Equal(1, result.Summary.DeliveredNotInstalledUnits);
        Assert.Equal(3, result.Summary.PlannedUnits);

        var countryRow = Assert.Single(result.Countries);
        Assert.Equal(6, countryRow.TotalUnits);
        Assert.Equal(2, countryRow.RecordCount);
        Assert.Equal(new short[] { 2026, 2024 }, countryRow.Years.Select(year => year.Year).ToArray());

        var currentYear = countryRow.Years.First();
        var linkedRow = Assert.Single(currentYear.Projects.Where(project => project.LinkedProjectId.HasValue));
        Assert.Equal("Universal Driving Simulator", linkedRow.DisplayName);
        Assert.Equal("Canonical Project external progress", linkedRow.CurrentProgress);
        Assert.Equal(1, progress.ReadCallCount);
        Assert.Equal(3, progress.LastTargetCount);
    }

    [Fact]
    public async Task GetAsync_AppliesYearCountryAndProjectSearchFilters()
    {
        await using var scope = await TestDbScope.CreateAsync();
        var db = scope.Db;
        var alpha = new FfcCountry { Name = "Alpha", IsoCode = "ALP", IsActive = true };
        var beta = new FfcCountry { Name = "Beta", IsoCode = "BET", IsActive = true };
        var linked = new Project { Name = "Swarm Algorithm", CreatedByUserId = "user" };
        db.AddRange(alpha, beta, linked);
        await db.SaveChangesAsync();

        var alphaRecord = new FfcRecord { CountryId = alpha.Id, Year = 2026 };
        var betaRecord = new FfcRecord { CountryId = beta.Id, Year = 2025 };
        db.FfcRecords.AddRange(alphaRecord, betaRecord);
        await db.SaveChangesAsync();
        db.FfcProjects.AddRange(
            new FfcProject { FfcRecordId = alphaRecord.Id, Name = "Country title", LinkedProjectId = linked.Id, Quantity = 4 },
            new FfcProject { FfcRecordId = betaRecord.Id, Name = "Other project", Quantity = 7 });
        await db.SaveChangesAsync();

        var service = new FfcFootprintService(db, new StubProgressService());
        var result = await service.GetAsync(new FfcFootprintRequest(
            Year: 2026,
            CountryId: alpha.Id,
            Search: "swarm"));

        var country = Assert.Single(result.Countries);
        Assert.Equal("Alpha", country.CountryName);
        Assert.Equal(4, country.PlannedUnits);
        Assert.Equal(new short[] { 2026, 2025 }, result.AvailableYears);
        Assert.Equal(2, result.CountryOptions.Count);
    }

    [Fact]
    public async Task GetAsync_ReturnsDistinctOrderedCountryOptionsOnRelationalProvider()
    {
        await using var scope = await TestDbScope.CreateAsync();
        var db = scope.Db;
        var zulu = new FfcCountry { Name = "Zulu", IsoCode = "ZUL", IsActive = true };
        var alpha = new FfcCountry { Name = "Alpha", IsoCode = "ALP", IsActive = true };
        db.AddRange(zulu, alpha);
        await db.SaveChangesAsync();

        db.FfcRecords.AddRange(
            new FfcRecord { CountryId = zulu.Id, Year = 2026 },
            new FfcRecord { CountryId = zulu.Id, Year = 2025 },
            new FfcRecord { CountryId = alpha.Id, Year = 2026 });
        await db.SaveChangesAsync();

        var service = new FfcFootprintService(db, new StubProgressService());
        var result = await service.GetAsync(new FfcFootprintRequest());

        Assert.Equal(new[] { "Alpha", "Zulu" }, result.CountryOptions.Select(option => option.CountryName).ToArray());
        Assert.Equal(2, result.CountryOptions.Count);
    }

    [Fact]
    public async Task GetAsync_ExcludesArchivedRecordsAndInactiveCountries()
    {
        await using var scope = await TestDbScope.CreateAsync();
        var db = scope.Db;
        var active = new FfcCountry { Name = "Active", IsoCode = "ACT", IsActive = true };
        var inactive = new FfcCountry { Name = "Inactive", IsoCode = "INA", IsActive = false };
        db.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var visible = new FfcRecord { CountryId = active.Id, Year = 2026 };
        var archived = new FfcRecord { CountryId = active.Id, Year = 2025, IsDeleted = true };
        var hidden = new FfcRecord { CountryId = inactive.Id, Year = 2026 };
        db.FfcRecords.AddRange(visible, archived, hidden);
        await db.SaveChangesAsync();
        db.FfcProjects.AddRange(
            new FfcProject { FfcRecordId = visible.Id, Name = "Visible", Quantity = 1 },
            new FfcProject { FfcRecordId = archived.Id, Name = "Archived", Quantity = 5 },
            new FfcProject { FfcRecordId = hidden.Id, Name = "Hidden", Quantity = 7 });
        await db.SaveChangesAsync();

        var service = new FfcFootprintService(db, new StubProgressService());
        var result = await service.GetAsync(new FfcFootprintRequest());

        var country = Assert.Single(result.Countries);
        Assert.Equal("Active", country.CountryName);
        Assert.Equal(1, country.TotalUnits);
        Assert.Single(result.CountryOptions);
        Assert.Equal("Active", result.CountryOptions[0].CountryName);
    }

    [Fact]
    public async Task GetAsync_ReturnsEmptyFilteredResultButRetainsAvailableFilters()
    {
        await using var scope = await TestDbScope.CreateAsync();
        var db = scope.Db;
        var country = new FfcCountry { Name = "France", IsoCode = "FRA", IsActive = true };
        db.FfcCountries.Add(country);
        await db.SaveChangesAsync();
        db.FfcRecords.Add(new FfcRecord { CountryId = country.Id, Year = 2026 });
        await db.SaveChangesAsync();

        var service = new FfcFootprintService(db, new StubProgressService());
        var result = await service.GetAsync(new FfcFootprintRequest(Search: "does-not-exist"));

        Assert.Empty(result.Countries);
        Assert.Equal(0, result.Summary.CountryCount);
        Assert.Equal(new short[] { 2026 }, result.AvailableYears);
        Assert.Single(result.CountryOptions);
        Assert.Equal("France", result.CountryOptions[0].CountryName);
    }

    private sealed class TestDbScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDbScope(ApplicationDbContext db, SqliteConnection connection)
        {
            Db = db;
            _connection = connection;
        }

        public ApplicationDbContext Db { get; }

        public static async Task<TestDbScope> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .EnableDetailedErrors()
                .Options;

            var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new TestDbScope(db, connection);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class StubProgressService : IFfcProgressService
    {
        private readonly IReadOnlyDictionary<long, string?> _overrides;

        public StubProgressService(IReadOnlyDictionary<long, string?>? overrides = null)
        {
            _overrides = overrides ?? new Dictionary<long, string?>();
        }

        public int ReadCallCount { get; private set; }
        public int LastTargetCount { get; private set; }

        public Task<IReadOnlyDictionary<long, FfcProgressSnapshot>> GetCurrentProgressAsync(
            IReadOnlyCollection<FfcProgressTarget> targets,
            CancellationToken cancellationToken = default)
        {
            ReadCallCount++;
            LastTargetCount = targets.Count;
            IReadOnlyDictionary<long, FfcProgressSnapshot> result = targets.ToDictionary(
                target => target.FfcProjectId,
                target => new FfcProgressSnapshot(
                    target.FfcProjectId,
                    _overrides.TryGetValue(target.FfcProjectId, out var value) ? value : target.FfcProjectRemarks,
                    null,
                    target.LinkedProjectId.HasValue
                        ? FfcProgressSource.ExternalProjectRemark
                        : FfcProgressSource.FfcProjectRemark,
                    target.LinkedProjectId.HasValue));
            return Task.FromResult(result);
        }

        public Task<FfcProgressUpdateResult> UpdateProgressAsync(
            FfcProgressUpdateCommand command,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
