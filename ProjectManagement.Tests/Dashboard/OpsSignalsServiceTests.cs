using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Services.Dashboard;
using Xunit;

namespace ProjectManagement.Tests.Dashboard;

public sealed class OpsSignalsServiceTests
{
    private static readonly DateOnly From = new(2025, 11, 1);
    private static readonly DateOnly To = new(2026, 10, 23);

    [Fact]
    public async Task Tiles_SayTheirTotalsAreAllTime_AndWhatTheChangeCompares()
    {
        await using var db = NewDb();

        var signals = await NewService(db).GetAsync(From, To, "hod", CancellationToken.None);

        Assert.All(signals.Tiles, tile =>
        {
            Assert.Equal("All time", tile.Caption);
            Assert.Equal("Aug–Oct 2026 compared with May–Jul 2026", tile.DeltaPeriods);
        });
        // No activity at all: the worded label says the period itself, so no "vs previous 3 months".
        Assert.All(signals.Tiles.Where(tile => tile.Key != "ipr"), tile =>
        {
            Assert.Equal("None in the last 6 months", tile.DeltaLabel);
            Assert.Null(tile.DeltaBasis);
        });
        Assert.Equal("No filings in the last 6 months", signals.Tiles.Single(tile => tile.Key == "ipr").DeltaLabel);
        Assert.Equal("Nov 2025", signals.Tiles[0].SparklineLabels![0]);
    }

    [Fact]
    public async Task Proliferation_CountsApprovedUnits_NeverEntries()
    {
        await using var db = NewDb();
        db.ProliferationGranularEntries.AddRange(
            Entry(new DateOnly(2026, 9, 10), 5, ApprovalStatus.Approved),
            Entry(new DateOnly(2026, 9, 12), 9, ApprovalStatus.Pending),
            Entry(new DateOnly(2026, 10, 2), 4, ApprovalStatus.Rejected));
        await db.SaveChangesAsync();

        // The summary has no approved totals: the headline is 0 units, not 3 entries.
        var signals = await NewService(db).GetAsync(From, To, "hod", CancellationToken.None);
        var proliferation = signals.Tiles.Single(tile => tile.Key == "proliferation");

        Assert.Equal(0, proliferation.Value);
        Assert.Equal(5, proliferation.Sparkline!.Sum());
        // Units in Aug–Oct against none in May–Jul.
        Assert.Equal("New in the last 3 months", proliferation.DeltaLabel);
        Assert.Null(proliferation.DeltaBasis);
    }

    private static ProliferationGranular Entry(DateOnly on, int quantity, ApprovalStatus status) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = 1,
        Source = ProliferationSource.Sdd,
        UnitName = "Unit",
        ProliferationDate = on,
        Quantity = quantity,
        ApprovalStatus = status,
        SubmittedByUserId = "po",
        CreatedOnUtc = DateTime.UtcNow,
        LastUpdatedOnUtc = DateTime.UtcNow,
        RowVersion = new byte[] { 1 }
    };

    private static ApplicationDbContext NewDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static OpsSignalsService NewService(ApplicationDbContext db)
        => new(db, NullLogger<OpsSignalsService>.Instance, new MemoryCache(new MemoryCacheOptions()), new EmptySummary());

    private sealed class EmptySummary : IProliferationSummaryReadService
    {
        public Task<ProliferationSummaryViewModel> GetSummaryAsync(CancellationToken cancellationToken)
            => Task.FromResult(ProliferationSummaryViewModel.Empty);

        public Task<ProliferationOperationalSnapshot> GetOperationalSnapshotAsync(int recentProliferationLimit, int recentActivityLimit, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
