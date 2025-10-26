using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports.Training;

public sealed class TrainingExcelWorkbookBuilderTests
{
    [Fact]
    public void Build_CreatesSummaryWorksheet_WhenRosterNotIncluded()
    {
        var summary = new TrainingExportRow(
            Guid.NewGuid(),
            "Signals Refresher",
            "2024-01-01 – 2024-01-05",
            Officers: 5,
            JuniorCommissionedOfficers: 3,
            OtherRanks: 2,
            Total: 10,
            Source: TrainingCounterSource.Legacy,
            Projects: new List<string> { "Project A", "Project B" },
            Notes: "Quarterly report");

        var context = new TrainingExcelWorkbookContext(
            new[] { new TrainingExportDetail(summary, Array.Empty<TrainingRosterRow>()) },
            DateTimeOffset.UtcNow,
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            "Signals",
            IncludeRoster: false,
            TrainingTypeName: "Signals Refresher",
            CategoryName: "Officers",
            ProjectTechnicalCategoryName: "Networking",
            ProjectTechnicalCategoryDisplayName: "Engineering > Networking");

        var builder = new TrainingExcelWorkbookBuilder();

        var workbookBytes = builder.Build(context);
        using var workbook = new XLWorkbook(new MemoryStream(workbookBytes));

        var summarySheet = workbook.Worksheet("Trainings");
        Assert.NotNull(summarySheet);
        Assert.Equal("S.No.", summarySheet.Cell(1, 1).GetString());
        Assert.Equal("Signals Refresher", summarySheet.Cell(2, 2).GetString());
        Assert.Equal("Include roster", summarySheet.Cell(8, 1).GetString());
        Assert.Equal("No", summarySheet.Cell(8, 2).GetString());
        Assert.False(workbook.Worksheets.TryGetWorksheet("Roster", out _));
    }

    [Fact]
    public void Build_CreatesRosterWorksheet_WhenRosterIncluded()
    {
        var summary = new TrainingExportRow(
            Guid.NewGuid(),
            "Advanced Signals",
            "2024-02-01 – 2024-02-10",
            Officers: 2,
            JuniorCommissionedOfficers: 1,
            OtherRanks: 0,
            Total: 3,
            Source: TrainingCounterSource.Roster,
            Projects: new List<string> { "Project X" },
            Notes: null);

        var roster = new List<TrainingRosterRow>
        {
            new() { Id = 1, ArmyNumber = "12345", Rank = "Maj", Name = "Alice", UnitName = "Unit 1", Category = (byte)TrainingCategory.Officer },
            new() { Id = 2, ArmyNumber = "", Rank = "Capt", Name = "Bob", UnitName = "Unit 2", Category = (byte)TrainingCategory.JuniorCommissionedOfficer }
        };

        var context = new TrainingExcelWorkbookContext(
            new[] { new TrainingExportDetail(summary, roster) },
            DateTimeOffset.UtcNow,
            new DateOnly(2024, 2, 1),
            new DateOnly(2024, 2, 29),
            null,
            IncludeRoster: true,
            TrainingTypeName: "Advanced Signals",
            CategoryName: null,
            ProjectTechnicalCategoryName: null,
            ProjectTechnicalCategoryDisplayName: null);

        var builder = new TrainingExcelWorkbookBuilder();

        var workbookBytes = builder.Build(context);
        using var workbook = new XLWorkbook(new MemoryStream(workbookBytes));

        var rosterSheet = workbook.Worksheet("Roster");
        Assert.NotNull(rosterSheet);
        Assert.Equal("Training type", rosterSheet.Cell(1, 1).GetString());
        Assert.Equal("Advanced Signals", rosterSheet.Cell(2, 1).GetString());
        Assert.Equal("2024-02-01 – 2024-02-10", rosterSheet.Cell(2, 2).GetString());
        Assert.Equal("12345", rosterSheet.Cell(2, 3).GetString());
        Assert.Equal("Maj", rosterSheet.Cell(2, 4).GetString());
        Assert.Equal("Alice", rosterSheet.Cell(2, 5).GetString());
        Assert.Equal("Unit 1", rosterSheet.Cell(2, 6).GetString());
        Assert.Equal("Officer", rosterSheet.Cell(2, 7).GetString());

        Assert.True(workbook.Worksheets.TryGetWorksheet("Trainings", out var summarySheet));
        Assert.NotNull(summarySheet);
        Assert.Equal("Include roster", summarySheet!.Cell(8, 1).GetString());
        Assert.Equal("Yes", summarySheet.Cell(8, 2).GetString());
    }
}
