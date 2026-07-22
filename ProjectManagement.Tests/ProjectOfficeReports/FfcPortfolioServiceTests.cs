using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Ffc;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcPortfolioServiceTests
{
    [Fact]
    public async Task GetSummaryAsync_ReturnsFilteredProjectUnitTotals()
    {
        await using var db = CreateDbContext();
        var alpha = new FfcCountry { Name = "Alpha", IsoCode = "ALP" };
        var beta = new FfcCountry { Name = "Beta", IsoCode = "BET" };
        db.FfcCountries.AddRange(alpha, beta);
        await db.SaveChangesAsync();

        var linkedProject = new Project { Name = "Linked simulator", CreatedByUserId = "user" };
        db.Projects.Add(linkedProject);
        await db.SaveChangesAsync();

        var alphaRecord = new FfcRecord { CountryId = alpha.Id, Year = 2026 };
        var betaRecord = new FfcRecord { CountryId = beta.Id, Year = 2026 };
        db.FfcRecords.AddRange(alphaRecord, betaRecord);
        await db.SaveChangesAsync();

        db.FfcProjects.AddRange(
            new FfcProject
            {
                FfcRecordId = alphaRecord.Id,
                Name = "Installed",
                Quantity = 2,
                IsDelivered = true,
                IsInstalled = true,
                LinkedProjectId = linkedProject.Id
            },
            new FfcProject
            {
                FfcRecordId = alphaRecord.Id,
                Name = "Delivered",
                Quantity = 3,
                IsDelivered = true
            },
            new FfcProject
            {
                FfcRecordId = alphaRecord.Id,
                Name = "Planned",
                Quantity = 5
            },
            new FfcProject
            {
                FfcRecordId = betaRecord.Id,
                Name = "Other",
                Quantity = 7
            });
        await db.SaveChangesAsync();

        var service = new FfcPortfolioService(db, new StubProgressService());
        var summary = await service.GetSummaryAsync(new FfcPortfolioFilter(CountryId: alpha.Id));

        Assert.Equal(1, summary.RecordCount);
        Assert.Equal(1, summary.CountryCount);
        Assert.Equal(3, summary.ProjectCount);
        Assert.Equal(1, summary.LinkedProjectCount);
        Assert.Equal(2, summary.InstalledUnits);
        Assert.Equal(3, summary.DeliveredNotInstalledUnits);
        Assert.Equal(5, summary.PlannedUnits);
        Assert.Equal(10, summary.TotalUnits);
        Assert.Equal(50, summary.DeliveryPercent);
        Assert.Equal(20, summary.InstallationPercent);
    }

    [Fact]
    public async Task GetPageAsync_PaginatesRowsButKeepsSummaryGlobal()
    {
        await using var db = CreateDbContext();
        var country = new FfcCountry { Name = "Alpha", IsoCode = "ALP" };
        db.FfcCountries.Add(country);
        await db.SaveChangesAsync();

        for (short year = 2026; year >= 2000; year--)
        {
            var record = new FfcRecord
            {
                CountryId = country.Id,
                Year = year,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.FfcRecords.Add(record);
            await db.SaveChangesAsync();

            db.FfcProjects.Add(new FfcProject
            {
                FfcRecordId = record.Id,
                Name = $"Project {year}",
                Quantity = 2,
                IsDelivered = true,
                IsInstalled = year % 2 == 0
            });
        }

        await db.SaveChangesAsync();

        var progress = new StubProgressService();
        var service = new FfcPortfolioService(db, progress);
        var result = await service.GetPageAsync(
            new FfcPortfolioPageRequest(new FfcPortfolioFilter(), PageNumber: 2, PageSize: 25));

        Assert.Equal(27, result.TotalRecordCount);
        Assert.Equal(27, result.Summary.RecordCount);
        Assert.Equal(27, result.Summary.ProjectCount);
        Assert.Equal(54, result.Summary.TotalUnits);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(2, result.Records.Count);
        Assert.Equal(new short[] { 2001, 2000 }, result.Records.Select(row => row.Year).ToArray());
        Assert.Equal(1, progress.ReadCallCount);
        Assert.Equal(2, progress.LastTargetCount);
    }

    [Fact]
    public async Task GetPageAsync_SearchesLinkedProjectNameAndBatchResolvesCanonicalProgress()
    {
        await using var db = CreateDbContext();
        var country = new FfcCountry { Name = "Ethiopia", IsoCode = "ETH" };
        var linkedProject = new Project
        {
            Name = "AURA Reconnaissance System",
            CreatedByUserId = "user",
            LifecycleStatus = ProjectLifecycleStatus.Active
        };
        db.AddRange(country, linkedProject);
        await db.SaveChangesAsync();

        var record = new FfcRecord
        {
            CountryId = country.Id,
            Year = 2026,
            OverallRemarks = "Current record position",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.FfcRecords.Add(record);
        await db.SaveChangesAsync();

        var linkedFfcProject = new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "Country display entry",
            LinkedProjectId = linkedProject.Id,
            Quantity = 4
        };
        var unlinkedFfcProject = new FfcProject
        {
            FfcRecordId = record.Id,
            Name = "Local support item",
            Remarks = "Local progress",
            Quantity = 1
        };
        db.FfcProjects.AddRange(linkedFfcProject, unlinkedFfcProject);
        await db.SaveChangesAsync();

        var progress = new StubProgressService(new Dictionary<long, string?>
        {
            [linkedFfcProject.Id] = "Canonical external project remark"
        });
        var service = new FfcPortfolioService(db, progress);

        var result = await service.GetPageAsync(
            new FfcPortfolioPageRequest(new FfcPortfolioFilter(Query: "aura")));

        var row = Assert.Single(result.Records);
        Assert.Equal("Ethiopia", row.CountryName);
        Assert.Equal(2, row.ProjectCount);
        Assert.Equal(2, row.Projects.Count);

        var linkedRow = Assert.Single(row.Projects.Where(project => project.LinkedProjectId.HasValue));
        Assert.Equal("AURA Reconnaissance System", linkedRow.DisplayName);
        Assert.Equal("Country display entry", linkedRow.FfcName);
        Assert.Equal("Canonical external project remark", linkedRow.CurrentProgress);

        var unlinkedRow = Assert.Single(row.Projects.Where(project => !project.LinkedProjectId.HasValue));
        Assert.Equal("Local progress", unlinkedRow.CurrentProgress);
        Assert.Equal(1, progress.ReadCallCount);
        Assert.Equal(2, progress.LastTargetCount);
    }

    [Theory]
    [InlineData(0, 0, FfcCompletionState.Pending)]
    [InlineData(0, 5, FfcCompletionState.Pending)]
    [InlineData(2, 5, FfcCompletionState.Partial)]
    [InlineData(5, 5, FfcCompletionState.Complete)]
    public void CompletionStateResolvers_ReturnExpectedState(
        int completed,
        int total,
        FfcCompletionState expected)
    {
        var summary = new FfcProjectQuantitySummary(
            Installed: completed,
            DeliveredNotInstalled: 0,
            Planned: Math.Max(0, total - completed));

        Assert.Equal(expected, FfcPortfolioQuery.ResolveInstallationState(summary));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
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

            var result = targets.ToDictionary(
                target => target.FfcProjectId,
                target =>
                {
                    var text = _overrides.TryGetValue(target.FfcProjectId, out var value)
                        ? value
                        : target.FfcProjectRemarks;
                    var source = target.LinkedProjectId.HasValue
                        ? FfcProgressSource.ExternalProjectRemark
                        : FfcProgressSource.FfcProjectRemark;

                    return new FfcProgressSnapshot(
                        target.FfcProjectId,
                        text,
                        null,
                        source,
                        target.LinkedProjectId.HasValue);
                });

            return Task.FromResult<IReadOnlyDictionary<long, FfcProgressSnapshot>>(result);
        }

        public Task<FfcProgressUpdateResult> UpdateProgressAsync(
            FfcProgressUpdateCommand command,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
