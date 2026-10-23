using System;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Ffc;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcProjectsViewsTests
{
    [Fact]
    public void OldMapTableBookmark_RedirectsToTheCountryCards()
    {
        var result = Assert.IsType<RedirectToPageResult>(new MapTableModel().OnGet());

        // Area-relative page name: "/ProjectOfficeReports/FFC/MapBoard" named no page and answered 500.
        Assert.Equal("/FFC/Footprint", result.PageName);
        Assert.Equal("ProjectOfficeReports", result.RouteValues!["area"]);
        Assert.Equal("cards", result.RouteValues["view"]);
    }

    [Fact]
    public void ViewTitles_UseTheModuleName()
    {
        Assert.Equal("FFC Projects – Overview", FfcViewHeader.PageTitle(FfcView.Overview));
        Assert.Equal("FFC Projects – Footprint", FfcViewHeader.PageTitle(FfcView.Footprint));
        Assert.Equal("FFC Projects – Detailed table", FfcViewHeader.PageTitle(FfcView.DetailedTable));
    }

    [Fact]
    public void StageSummary_SaysSkippedAndNotRecorded_NeverMissed()
    {
        var summary = FfcProjectStageSummaryFormatter.Format(new[]
        {
            new FfcProjectStageSnapshot(1, StageCodes.FS, 1, StageStatus.NotStarted, null),
            new FfcProjectStageSnapshot(1, StageCodes.IPA, 2, StageStatus.Skipped, null),
            new FfcProjectStageSnapshot(1, StageCodes.SOW, 3, StageStatus.Completed, new DateOnly(2025, 11, 29)),
            new FfcProjectStageSnapshot(1, StageCodes.AON, 4, StageStatus.InProgress, null),
        });

        Assert.NotNull(summary);
        Assert.DoesNotContain("missed", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"not recorded: {StageCodes.DisplayNameOf(StageCodes.FS)}", summary, StringComparison.Ordinal);
        Assert.Contains($"skipped: {StageCodes.DisplayNameOf(StageCodes.IPA)}", summary, StringComparison.Ordinal);
        Assert.DoesNotContain($"not recorded: {StageCodes.DisplayNameOf(StageCodes.IPA)}", summary, StringComparison.Ordinal);
    }
}
