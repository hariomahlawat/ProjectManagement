using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Reports.ArppFyProjectUpdate;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>Analytics → ARPP mode: the rules the service owns (year choice, cost bands, supply-order placement, PDC milestones).</summary>
public sealed class ArppAnalyticsServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 25); // FY 2026-27

    [Fact]
    public async Task BuildAsync_OpensOnTheCurrentYearWhenPublished_ElseLatestWithANote()
    {
        await using var db = CreateDb();
        var onCurrent = new ArppAnalyticsService(db, new FakeLibrary(2026), new FakeFyUpdate(new[] { 2026, 2025 }));
        var current = await onCurrent.BuildAsync(null, Today);
        Assert.Equal(2026, current.FinancialYearStart);
        Assert.True(current.IsCurrentFinancialYear);
        Assert.False(current.IsFinancialYearClosed);
        Assert.Null(current.FallbackNote);

        var onlyOld = new ArppAnalyticsService(db, new FakeLibrary(2025), new FakeFyUpdate(new[] { 2025, 2024 }));
        var fallback = await onlyOld.BuildAsync(null, Today);
        Assert.Equal(2025, fallback.FinancialYearStart);
        Assert.False(fallback.IsCurrentFinancialYear);
        Assert.True(fallback.IsFinancialYearClosed);
        Assert.Contains("showing FY 2025-26", fallback.FallbackNote);

        var requested = await onlyOld.BuildAsync(2024, Today);
        Assert.Equal(2024, requested.FinancialYearStart);
        Assert.Null(requested.FallbackNote); // the user asked for it: no note
    }

    [Fact]
    public async Task BuildAsync_WithoutAnyPublishedYear_HasNoData()
    {
        await using var db = CreateDb();
        var service = new ArppAnalyticsService(db, new FakeLibrary(2026), new FakeFyUpdate(Array.Empty<int>()));

        var vm = await service.BuildAsync(null, Today);

        Assert.False(vm.HasData);
        Assert.Empty(vm.AvailableFinancialYears);
        Assert.Equal(2026, vm.FinancialYearStart);
    }

    [Fact]
    public async Task BuildAsync_GroupsListedProjectsByCostBandCategoryAndStanding()
    {
        await using var db = CreateDb();
        var library = new FakeLibrary(2026,
            Row(1, "Small", ArppCategory.New, 5_000_000m),            // under 1 Cr
            Row(2, "Medium", ArppCategory.CommittedLiability, 30_000_000m), // 1 to 5 Cr
            Row(3, "Large", ArppCategory.CarryForward, 150_000_000m),  // 5 to 20 Cr
            Row(4, "Huge", ArppCategory.New, 400_000_000m));           // 20 Cr and above
        var fy = new FakeFyUpdate(new[] { 2026 },
            Update(1, "Small", ProjectLifecycleStatus.Active, StageCodes.DEVP),
            Update(2, "Medium", ProjectLifecycleStatus.Completed, null),
            Update(3, "Large", ProjectLifecycleStatus.Cancelled, null),
            Update(4, "Huge", ProjectLifecycleStatus.Active, null));

        var vm = await new ArppAnalyticsService(db, library, fy).BuildAsync(2026, Today);

        Assert.Equal(4, vm.ListedCount);
        Assert.Equal(585_000_000m, vm.ListedIpaValue);
        Assert.Equal(2, vm.NewCount);
        Assert.Equal(1, vm.CommittedLiabilityCount);
        Assert.Equal(1, vm.CarryForwardCount);

        Assert.Equal(new[] { "lt1", "1to5", "5to20", "ge20" }, vm.ByCostBand.Select(group => group.Key).ToArray());
        Assert.All(vm.ByCostBand, group => Assert.Equal(1, group.Count));

        Assert.Equal(2, vm.OngoingCount);
        Assert.Equal(1, vm.CompletedCount);
        Assert.Equal(1, vm.CancelledCount);

        // Standing board: Completed first, then stages (Development, then Unassigned for the one without a stage), then Cancelled.
        Assert.Equal(new[] { "COMPLETED", StageCodes.DEVP, "UNASSIGNED", "CANCELLED" }, vm.StandingBoard.Select(group => group.Key).ToArray());
        Assert.Equal(1, vm.ByStage.Single(stage => stage.StageCode == StageCodes.DEVP).Count);
    }

    [Fact]
    public async Task BuildAsync_CountsASupplyOrderOnlyOnEvidenceOfPlacement()
    {
        await using var db = CreateDb();
        var library = new FakeLibrary(2026, Row(1, "Dated", ArppCategory.New, 10_000_000m), Row(2, "Beyond SO", ArppCategory.New, 10_000_000m), Row(3, "At SO", ArppCategory.New, 10_000_000m), Row(4, "Amount only", ArppCategory.New, 10_000_000m), Row(5, "Done", ArppCategory.New, 10_000_000m));
        var fy = new FakeFyUpdate(new[] { 2026 },
            Update(1, "Dated", ProjectLifecycleStatus.Active, StageCodes.AON, supplyOrderDate: new DateOnly(2026, 6, 1), supplyOrderAmount: 8_000_000m),
            Update(2, "Beyond SO", ProjectLifecycleStatus.Active, StageCodes.DEVP),
            Update(3, "At SO", ProjectLifecycleStatus.Active, StageCodes.SO),
            Update(4, "Amount only", ProjectLifecycleStatus.Active, StageCodes.PNC, supplyOrderAmount: 9_000_000m),
            Update(5, "Done", ProjectLifecycleStatus.Completed, null));

        var vm = await new ArppAnalyticsService(db, library, fy).BuildAsync(2026, Today);

        var placed = vm.MilestoneBoard.Single(group => group.Key == "SO");
        Assert.Equal(new[] { "Beyond SO", "Dated", "Done" }, placed.Projects.Select(project => project.ProjectName).ToArray());
        Assert.Equal(3, vm.SupplyOrderCount);
        Assert.Equal(8_000_000m, vm.SupplyOrderValue); // only the dated order carries an amount
    }

    [Fact]
    public async Task BuildAsync_PdcMilestones_CountOnlyProjectsStillInWork()
    {
        await using var db = CreateDb();
        var library = new FakeLibrary(2026,
            Row(1, "Ahead", ArppCategory.New, 10_000_000m),
            Row(2, "Late", ArppCategory.New, 10_000_000m),
            Row(3, "Cancelled late", ArppCategory.New, 10_000_000m),
            Row(4, "Completed late", ArppCategory.New, 10_000_000m),
            Row(5, "Archived late", ArppCategory.New, 10_000_000m),
            Row(6, "Next year", ArppCategory.New, 10_000_000m));
        var fy = new FakeFyUpdate(new[] { 2026 },
            Update(1, "Ahead", ProjectLifecycleStatus.Active, StageCodes.DEVP, pdc: new DateOnly(2027, 1, 15)),
            Update(2, "Late", ProjectLifecycleStatus.Active, StageCodes.DEVP, pdc: new DateOnly(2026, 9, 1)),
            Update(3, "Cancelled late", ProjectLifecycleStatus.Cancelled, null, pdc: new DateOnly(2026, 9, 1)),
            Update(4, "Completed late", ProjectLifecycleStatus.Completed, null, pdc: new DateOnly(2026, 9, 1)),
            Update(5, "Archived late", ProjectLifecycleStatus.Active, StageCodes.DEVP, pdc: new DateOnly(2026, 9, 1), archived: true),
            Update(6, "Next year", ProjectLifecycleStatus.Active, StageCodes.DEVP, pdc: new DateOnly(2027, 6, 1)));

        var vm = await new ArppAnalyticsService(db, library, fy).BuildAsync(2026, Today);

        Assert.Equal(1, vm.PdcDueInYearCount);
        Assert.Equal("Ahead", vm.MilestoneBoard.Single(group => group.Key == "PDC_DUE").Projects.Single().ProjectName);
        Assert.Equal(1, vm.PdcOverdueCount);
        Assert.Equal("Late", vm.MilestoneBoard.Single(group => group.Key == "PDC_OVERDUE").Projects.Single().ProjectName);
        Assert.Equal("PDC due by 31 Mar 2027", vm.MilestoneBoard.Single(group => group.Key == "PDC_DUE").Name);
    }

    [Fact]
    public async Task BuildAsync_ClosedYear_SaysSoInsteadOfAnEmptyDueWindow()
    {
        await using var db = CreateDb();
        var library = new FakeLibrary(2024, Row(1, "Old", ArppCategory.New, 10_000_000m));
        var fy = new FakeFyUpdate(new[] { 2026, 2024 },
            Update(1, "Old", ProjectLifecycleStatus.Active, StageCodes.DEVP, pdc: new DateOnly(2025, 1, 1)));

        var vm = await new ArppAnalyticsService(db, library, fy).BuildAsync(2024, Today);

        Assert.True(vm.IsFinancialYearClosed);
        Assert.Equal(0, vm.PdcDueInYearCount);
        Assert.Equal("PDC due by 31 Mar 2025 (year closed)", vm.MilestoneBoard.Single(group => group.Key == "PDC_DUE").Name);
        Assert.Equal(1, vm.PdcOverdueCount);
    }

    // SECTION: Fixtures
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ArppLibraryCurrentRow Row(int projectId, string name, ArppCategory category, decimal ipaCost, string cfa = "HQ", string fund = "Revenue", string dfpds = "Schedule I") =>
        new(projectId, projectId, $"P-{projectId}", name, "Active", projectId.ToString(), null, category, ipaCost, cfa, fund, dfpds, 2026, 1, "ARPP", ArppIssueKind.Original, 0, new DateOnly(2026, 4, 1));

    private static ArppFyProjectUpdateRow Update(
        int projectId,
        string name,
        ProjectLifecycleStatus lifecycle,
        string? stageCode,
        DateOnly? pdc = null,
        DateOnly? supplyOrderDate = null,
        decimal? supplyOrderAmount = null,
        bool archived = false) =>
        new(projectId, projectId, null, name, null, null, null, "Est", null, supplyOrderAmount, ProjectSupplyOrderValueBasis.None, supplyOrderDate, pdc, ArppCategory.New, null, null, lifecycle, archived, stageCode, stageCode ?? "—", 0);

    private sealed class FakeLibrary : IArppLibraryService
    {
        private readonly int _year;
        private readonly IReadOnlyList<ArppLibraryCurrentRow> _rows;
        public FakeLibrary(int year, params ArppLibraryCurrentRow[] rows) { _year = year; _rows = rows; }

        public Task<ArppLibraryCurrentPosition?> GetCurrentPositionAsync(int financialYearStart, string? query, CancellationToken cancellationToken = default) =>
            Task.FromResult<ArppLibraryCurrentPosition?>(new ArppLibraryCurrentPosition(financialYearStart, _rows, Array.Empty<ArppLibraryCurrentRow>(), Array.Empty<ArppLibraryUnlinkedRow>(), 0));

        public Task<ArppLibraryNavigation> GetNavigationAsync(string? query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ArppLibraryDocument?> GetDocumentAsync(long issueId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ArppLibraryProjectHistory?> GetProjectHistoryAsync(int projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ArppLibraryAttachmentDownload?> OpenAttachmentAsync(long issueId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeFyUpdate : IArppFyProjectUpdateService
    {
        private readonly IReadOnlyList<int> _years;
        private readonly IReadOnlyList<ArppFyProjectUpdateRow> _rows;
        public FakeFyUpdate(IReadOnlyList<int> years, params ArppFyProjectUpdateRow[] rows) { _years = years; _rows = rows; }

        public Task<IReadOnlyList<int>> GetAvailableFinancialYearsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_years);

        public Task<ArppFyProjectUpdateReport?> BuildAsync(int financialYearStart, CancellationToken cancellationToken = default) =>
            Task.FromResult<ArppFyProjectUpdateReport?>(new ArppFyProjectUpdateReport(financialYearStart, DateTimeOffset.UtcNow, _rows, Array.Empty<ArppFyReportWarning>(), 0));
    }
    // END SECTION
}
