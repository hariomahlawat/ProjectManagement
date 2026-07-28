using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Application.Training.Dtos;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports.Training;

public sealed class TrainingExcelWorkbookBuilderTests
{
    [Fact]
    public void Build_CreatesProfessionalNormalizedWorkbook()
    {
        var trainingId = Guid.NewGuid();
        var detail = CreateDetail(trainingId, includeRoster: false);
        var dataset = new TrainingExportDataset(
            new[] { detail },
            new[]
            {
                new TrainingProjectExportRow(trainingId, 42, "Project Atlas", "AR / VR", "Ongoing")
            });

        var builder = new TrainingExcelWorkbookBuilder();
        var bytes = builder.Build(CreateContext(dataset, includeRoster: false));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));

        Assert.Equal(3, workbook.Worksheets.Count);
        Assert.Equal("TRAINING TRACKER", workbook.Worksheet("Summary").Cell("A1").GetString());
        Assert.Equal("All projects", workbook.Worksheet("Summary").Cell("F7").GetString());

        var trainings = workbook.Worksheet("Trainings");
        Assert.Equal("Training ID", trainings.Cell(1, 2).GetString());
        Assert.Equal(trainingId.ToString(), trainings.Cell(2, 2).GetString());
        Assert.Equal(new DateTime(2024, 4, 5), trainings.Cell(2, 4).GetDateTime());
        Assert.Equal(8, trainings.Cell(2, 7).GetValue<int>());
        Assert.Equal(12, trainings.Cell(2, 12).GetValue<int>());
        Assert.True(trainings.Column(15).Width <= 42.1);
        Assert.Equal("TrainingsTable", workbook.Table("TrainingsTable").Name);

        var projects = workbook.Worksheet("Training Projects");
        Assert.Equal("Project Atlas", projects.Cell(2, 5).GetString());
        Assert.Equal("TrainingProjectsTable", workbook.Table("TrainingProjectsTable").Name);
        Assert.False(workbook.Worksheets.TryGetWorksheet("Roster", out _));
    }

    [Fact]
    public void Build_CreatesRosterWithoutRepeatingProjectLists()
    {
        var trainingId = Guid.NewGuid();
        var detail = CreateDetail(trainingId, includeRoster: true);
        var dataset = new TrainingExportDataset(
            new[] { detail },
            new[] { new TrainingProjectExportRow(trainingId, 7, "Project X", "AI", "Completed") });

        var builder = new TrainingExcelWorkbookBuilder();
        var bytes = builder.Build(CreateContext(dataset, includeRoster: true));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var roster = workbook.Worksheet("Roster");

        Assert.Equal("S.No.", roster.Cell(1, 1).GetString());
        Assert.Equal(trainingId.ToString(), roster.Cell(2, 2).GetString());
        Assert.Equal("A123", roster.Cell(2, 6).GetString());
        Assert.Equal("Officer", roster.Cell(2, 10).GetString());
        Assert.DoesNotContain("Project", roster.Row(1).CellsUsed().Select(cell => cell.GetString()));
        Assert.Equal("RosterTable", workbook.Table("RosterTable").Name);
        Assert.Contains(
            workbook.Worksheet("Summary").CellsUsed().Select(cell => cell.GetString()),
            value => value.Contains("personnel details", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_UsesTypedDatesAndNumericCells()
    {
        var trainingId = Guid.NewGuid();
        var dataset = new TrainingExportDataset(
            new[] { CreateDetail(trainingId, includeRoster: false) },
            Array.Empty<TrainingProjectExportRow>());

        var builder = new TrainingExcelWorkbookBuilder();
        var bytes = builder.Build(CreateContext(dataset, includeRoster: false));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Trainings");

        Assert.True(sheet.Cell(2, 4).TryGetValue<DateTime>(out _));
        Assert.True(sheet.Cell(2, 7).TryGetValue<int>(out _));
        Assert.True(sheet.Cell(2, 12).TryGetValue<int>(out _));
        Assert.Equal(XLPageOrientation.Landscape, sheet.PageSetup.PageOrientation);
        Assert.Equal(XLPaperSize.A4Paper, sheet.PageSetup.PaperSize);
    }

    private static TrainingExcelWorkbookContext CreateContext(TrainingExportDataset dataset, bool includeRoster)
        => new(
            dataset,
            new TrainingKpiDto
            {
                TotalTrainings = 1,
                TotalTrainees = 12,
                ByType = new[]
                {
                    new TrainingKpiByTypeDto(Guid.NewGuid(), "Simulator", 1, 12, 5, 4, 3)
                },
                ByTechnicalCategory = new[]
                {
                    new TrainingKpiByTechnicalCategoryDto(1, "AR / VR", 1, 12, 5, 4, 3)
                },
                ByTrainingYear = new[]
                {
                    new TrainingYearBucketDto("2024–25", 12, 0, 1, 12)
                }
            },
            new DateTimeOffset(2024, 5, 1, 8, 30, 0, TimeSpan.Zero),
            "Export User",
            "https://prism.local",
            new DateOnly(2024, 4, 1),
            new DateOnly(2024, 4, 30),
            "Atlas",
            includeRoster,
            TrainingRosterScope.AllTraineesInMatchingEvents,
            "Simulator",
            "Officers",
            null,
            "Engineering > AR / VR");

    private static TrainingExportDetail CreateDetail(Guid trainingId, bool includeRoster)
    {
        var summary = new TrainingExportRow(
            trainingId,
            Guid.NewGuid(),
            "Simulator",
            new DateOnly(2024, 4, 5),
            new DateOnly(2024, 4, 12),
            8,
            "2024–25",
            "2024-04-05 – 2024-04-12 (8 days)",
            Officers: 5,
            JuniorCommissionedOfficers: 4,
            OtherRanks: 3,
            Total: 12,
            Source: TrainingCounterSource.Roster,
            Projects: new[] { "Project Atlas" },
            Notes: "Field readiness drills.");

        var roster = includeRoster
            ? new[]
            {
                new TrainingRosterRow
                {
                    Id = 1,
                    ArmyNumber = "A123",
                    Rank = "Capt",
                    Name = "R. Iyer",
                    UnitName = "45 Signals",
                    Category = (byte)TrainingCategory.Officer
                }
            }
            : Array.Empty<TrainingRosterRow>();

        return new TrainingExportDetail(summary, roster);
    }
}
