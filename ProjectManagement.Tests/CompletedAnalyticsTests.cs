using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Analytics;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>The Completed mode's yearly-average rule and its headline figures.</summary>
public sealed class CompletedAnalyticsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly PortfolioAnalyticsService _service;

    public CompletedAnalyticsTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _service = new PortfolioAnalyticsService(_db, new WorkflowStageMetadataProvider());
    }

    private static IReadOnlyList<CompletedPerYearPoint> PerYear(params (int Year, int Count)[] points) =>
        points.Select(point => new CompletedPerYearPoint(point.Year, point.Count)).ToList();

    [Fact]
    public void YearlyAverages_UseTheLastFiveCompleteYears_AndTheFiveBefore()
    {
        var perYear = PerYear((2014, 1), (2016, 6), (2017, 8), (2018, 13), (2019, 27), (2020, 18), (2021, 6), (2022, 5), (2023, 5), (2024, 7), (2025, 20), (2026, 7));

        var (recent, previous) = PortfolioAnalyticsService.BuildYearlyAverages(perYear, currentYear: 2026);

        Assert.NotNull(recent);
        Assert.Equal((2021, 2025, 43), (recent!.FromYear, recent.ToYear, recent.Count)); // 2026 is partial and left out
        Assert.Equal(8.6, recent.PerYear, 3);
        Assert.NotNull(previous);
        Assert.Equal((2016, 2020, 72), (previous!.FromYear, previous.ToYear, previous.Count));
        Assert.Equal(14.4, previous.PerYear, 3);
    }

    [Fact]
    public void YearlyAverages_CountAYearWithNoCompletionsAsZero()
    {
        var perYear = PerYear((2015, 2), (2021, 10), (2025, 5), (2026, 1));

        var (recent, previous) = PortfolioAnalyticsService.BuildYearlyAverages(perYear, currentYear: 2026);

        Assert.Equal(5, recent!.Years);
        Assert.Equal(3.0, recent.PerYear, 3); // 10 + 0 + 0 + 0 + 5 over five years
        Assert.Equal((2016, 2020), (previous!.FromYear, previous.ToYear));
        Assert.Equal(0, previous.Count);
        Assert.Equal(0.0, previous.PerYear, 3);
    }

    [Fact]
    public void YearlyAverages_ShortHistoryAveragesOverTheYearsItHas()
    {
        var perYear = PerYear((2024, 4), (2025, 6), (2026, 2));

        var (recent, previous) = PortfolioAnalyticsService.BuildYearlyAverages(perYear, currentYear: 2026);

        Assert.Equal((2024, 2025, 10, 2), (recent!.FromYear, recent.ToYear, recent.Count, recent.Years));
        Assert.Equal(5.0, recent.PerYear, 3);
        Assert.Null(previous); // nothing was completed before the recent window
    }

    [Fact]
    public void YearlyAverages_PreviousWindowClipsToTheFirstYear()
    {
        var perYear = PerYear((2019, 3), (2020, 5), (2021, 1), (2022, 1), (2023, 1), (2024, 1), (2025, 1));

        var (_, previous) = PortfolioAnalyticsService.BuildYearlyAverages(perYear, currentYear: 2026);

        Assert.Equal((2019, 2020, 8, 2), (previous!.FromYear, previous.ToYear, previous.Count, previous.Years));
        Assert.Equal(4.0, previous.PerYear, 3);
    }

    [Fact]
    public void YearlyAverages_NoCompleteYearYet_IsNull()
    {
        Assert.Equal((null, null), PortfolioAnalyticsService.BuildYearlyAverages(PerYear((2026, 3)), currentYear: 2026));
        Assert.Equal((null, null), PortfolioAnalyticsService.BuildYearlyAverages(PerYear(), currentYear: 2026));
    }

    [Fact]
    public async Task BuildCompletedAsync_ReportsTheAverageFromTheCompletedProjects()
    {
        foreach (var (year, count) in new[] { (2023, 2), (2025, 4), (2026, 1) })
        {
            for (var i = 0; i < count; i++)
            {
                _db.Projects.Add(new Project
                {
                    Name = $"Done {year}-{i}",
                    CreatedByUserId = "seed",
                    LifecycleStatus = ProjectLifecycleStatus.Completed,
                    CompletedYear = year,
                    CompletedOn = new DateOnly(year, 3, 1)
                });
            }
        }
        await _db.SaveChangesAsync();

        var result = await _service.BuildCompletedAsync(new DateOnly(2026, 10, 26), CancellationToken.None);

        Assert.Equal(7, result.TotalCompletedProjects);
        Assert.Equal(1, result.CompletedThisYear);
        Assert.Equal((2023, 2025, 6), (result.RecentYearlyAverage!.FromYear, result.RecentYearlyAverage.ToYear, result.RecentYearlyAverage.Count));
        Assert.Equal(2.0, result.RecentYearlyAverage.PerYear, 3);
        Assert.Null(result.PreviousYearlyAverage);
    }

    [Fact]
    public async Task BuildCompletedAsync_MedianTimeToComplete_CountsAnInferredFirstStart()
    {
        // No stage start was ever recorded: SoW is taken to start on 11 Jan, the day after FS finished.
        _db.Projects.Add(new Project
        {
            Name = "Completed, starts not recorded",
            CreatedByUserId = "seed",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            WorkflowVersion = ProcurementWorkflow.VersionV2,
            CompletedYear = 2026,
            CompletedOn = new DateOnly(2026, 3, 1),
            ProjectStages =
            {
                new ProjectStage { StageCode = StageCodes.FS, SortOrder = 10, Status = StageStatus.Completed, CompletedOn = new DateOnly(2026, 1, 10) },
                new ProjectStage { StageCode = StageCodes.SOW, SortOrder = 20, Status = StageStatus.Completed, CompletedOn = new DateOnly(2026, 1, 20) }
            }
        });
        await _db.SaveChangesAsync();

        var result = await _service.BuildCompletedAsync(new DateOnly(2026, 10, 26), CancellationToken.None);

        Assert.Equal(50, result.MedianDaysToComplete); // 11 Jan to 1 Mar, inclusive
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
