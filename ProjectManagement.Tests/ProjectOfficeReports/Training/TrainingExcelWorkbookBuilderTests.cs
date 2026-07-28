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
    public void Build_CreatesReadableWorkbookWithoutInternalIdentifiersOrNavigationColumns()
    {
        var trainingId = Guid.NewGuid();
        var detail = CreateDetail(trainingId, includeRoster: false, notes: "Field readiness drills.");
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
        Assert.Contains(
            workbook.Worksheet("Summary").CellsUsed().Select(cell => cell.GetString()),
            value => value.Contains("not additive", StringComparison.OrdinalIgnoreCase));

        var trainings = workbook.Worksheet("Trainings");
        var trainingHeaders = trainings.Row(1).CellsUsed().Select(cell => cell.GetString()).ToArray();
        Assert.Equal("S.No.", trainingHeaders[0]);
        Assert.Equal("Training type", trainingHeaders[1]);
        Assert.DoesNotContain("Training ID", trainingHeaders);
        Assert.DoesNotContain("Open in PRISM", trainingHeaders);
        Assert.Equal(1, trainings.Cell(2, 1).GetValue<int>());
        Assert.Equal("Simulator", trainings.Cell(2, 2).GetString());
        Assert.Equal(new DateTime(2024, 4, 5), trainings.Cell(2, 3).GetDateTime());
        Assert.Equal(8, trainings.Cell(2, 6).GetValue<int>());
        Assert.Equal(12, trainings.Cell(2, 11).GetValue<int>());
        Assert.Equal("Field readiness drills.", trainings.Cell(2, 15).GetString());
        Assert.True(trainings.Column(14).Width <= 42.1);
        Assert.Equal("TrainingsTable", workbook.Table("TrainingsTable").Name);

        var projects = workbook.Worksheet("Training Projects");
        var projectHeaders = projects.Row(1).CellsUsed().Select(cell => cell.GetString()).ToArray();
        Assert.Equal(new[] { "Training S.No.", "Training type", "Project name", "Technical category", "Project status" }, projectHeaders);
        Assert.DoesNotContain("Training ID", projectHeaders);
        Assert.DoesNotContain("Project ID", projectHeaders);
        Assert.DoesNotContain("Open project", projectHeaders);
        Assert.Equal(1, projects.Cell(2, 1).GetValue<int>());
        Assert.Equal("Project Atlas", projects.Cell(2, 3).GetString());
        Assert.Equal("TrainingProjectsTable", workbook.Table("TrainingProjectsTable").Name);
        Assert.False(workbook.Worksheets.TryGetWorksheet("Roster", out _));
    }

    [Fact]
    public void Build_CreatesRosterWithWorkbookLocalTrainingSerials()
    {
        var trainingId = Guid.NewGuid();
        var detail = CreateDetail(trainingId, includeRoster: true, notes: null);
        var dataset = new TrainingExportDataset(
            new[] { detail },
            new[] { new TrainingProjectExportRow(trainingId, 7, "Project X", "AI", "Completed") });

        var builder = new TrainingExcelWorkbookBuilder();
        var bytes = builder.Build(CreateContext(dataset, includeRoster: true));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var roster = workbook.Worksheet("Roster");
        var headers = roster.Row(1).CellsUsed().Select(cell => cell.GetString()).ToArray();

        Assert.Equal("S.No.", headers[0]);
        Assert.Equal("Training S.No.", headers[1]);
        Assert.DoesNotContain("Training ID", headers);
        Assert.DoesNotContain("Open training", headers);
        Assert.Equal(1, roster.Cell(2, 1).GetValue<int>());
        Assert.Equal(1, roster.Cell(2, 2).GetValue<int>());
        Assert.Equal("A123", roster.Cell(2, 6).GetString());
        Assert.Equal("Officer", roster.Cell(2, 10).GetString());
        Assert.DoesNotContain("Project", headers);
        Assert.Equal("RosterTable", workbook.Table("RosterTable").Name);
        Assert.Contains(
            workbook.Worksheet("Summary").CellsUsed().Select(cell => cell.GetString()),
            value => value.Contains("personnel details", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_SuppressesNumericOnlyLegacyNotePlaceholders()
    {
        var trainingId = Guid.NewGuid();
        var dataset = new TrainingExportDataset(
            new[] { CreateDetail(trainingId, includeRoster: false, notes: "68") },
            Array.Empty<TrainingProjectExportRow>());

        var builder = new TrainingExcelWorkbookBuilder();
        var bytes = builder.Build(CreateContext(dataset, includeRoster: false));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var headers = workbook.Worksheet("Trainings").Row(1).CellsUsed().Select(cell => cell.GetString()).ToArray();

        Assert.DoesNotContain("Notes", headers);
        Assert.DoesNotContain("68", workbook.Worksheet("Trainings").CellsUsed().Select(cell => cell.GetString()));
    }

    [Fact]
    public void Build_VisuallyGroupsRelatedProjectAndRosterRowsWithoutMergedDataCells()
    {
        var firstTrainingId = Guid.NewGuid();
        var secondTrainingId = Guid.NewGuid();
        var dataset = new TrainingExportDataset(
            new[]
            {
                CreateDetail(firstTrainingId, includeRoster: true, notes: null),
                CreateDetail(secondTrainingId, includeRoster: true, notes: null)
            },
            new[]
            {
                new TrainingProjectExportRow(firstTrainingId, 1, "Alpha", "AI", "Ongoing"),
                new TrainingProjectExportRow(firstTrainingId, 2, "Bravo", "AR / VR", "Ongoing"),
                new TrainingProjectExportRow(secondTrainingId, 3, "Charlie", "Misc", "Completed")
            });

        var builder = new TrainingExcelWorkbookBuilder();
        var bytes = builder.Build(CreateContext(dataset, includeRoster: true));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var projects = workbook.Worksheet("Training Projects");
        Assert.Equal(1, projects.Cell(2, 1).GetValue<int>());
        Assert.Equal(1, projects.Cell(3, 1).GetValue<int>());
        Assert.Equal(2, projects.Cell(4, 1).GetValue<int>());
        Assert.Equal(XLBorderStyleValues.Medium, projects.Cell(4, 1).Style.Border.TopBorder);
        Assert.Empty(projects.MergedRanges);

        var roster = workbook.Worksheet("Roster");
        Assert.Equal(1, roster.Cell(2, 2).GetValue<int>());
        Assert.Equal(2, roster.Cell(3, 2).GetValue<int>());
        Assert.Equal(XLBorderStyleValues.Medium, roster.Cell(3, 1).Style.Border.TopBorder);
        Assert.Empty(roster.MergedRanges);
    }

    [Fact]
    public void Build_UsesTypedDatesAndNumericCells()
    {
        var trainingId = Guid.NewGuid();
        var dataset = new TrainingExportDataset(
            new[] { CreateDetail(trainingId, includeRoster: false, notes: null) },
            Array.Empty<TrainingProjectExportRow>());

        var builder = new TrainingExcelWorkbookBuilder();
        var bytes = builder.Build(CreateContext(dataset, includeRoster: false));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Trainings");

        Assert.True(sheet.Cell(2, 3).TryGetValue<DateTime>(out _));
        Assert.True(sheet.Cell(2, 6).TryGetValue<int>(out _));
        Assert.True(sheet.Cell(2, 11).TryGetValue<int>(out _));
        Assert.Equal(XLPageOrientation.Landscape, sheet.PageSetup.PageOrientation);
        Assert.Equal(XLPaperSize.A4Paper, sheet.PageSetup.PaperSize);
    }

    private static TrainingExcelWorkbookContext CreateContext(TrainingExportDataset dataset, bool includeRoster)
        => new(
            dataset,
            new TrainingKpiDto
            {
                TotalTrainings = dataset.Trainings.Count,
                TotalTrainees = dataset.Trainings.Sum(item => item.Summary.Total),
                ByType = new[]
                {
                    new TrainingKpiByTypeDto(Guid.NewGuid(), "Simulator", dataset.Trainings.Count, 12, 5, 4, 3)
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

    private static TrainingExportDetail CreateDetail(Guid trainingId, bool includeRoster, string? notes)
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
            Notes: notes);

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
