using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Utilities.Reporting;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProliferationProfessionalWorkbookTests
{
    private static readonly DateTimeOffset GeneratedAtUtc =
        new(2026, 7, 28, 7, 30, 45, TimeSpan.Zero);

    [Fact]
    public void ProjectTotalsWorkbook_CreatesProfessionalSummaryTableAndQualityDisclosure()
    {
        var summary = CreateSummary();
        var metadata = CreateMetadata(allTime: true);
        var builder = new ProliferationProjectsCardExcelWorkbookBuilder();

        using var workbook = Open(builder.Build(summary, metadata));

        Assert.True(workbook.Worksheets.TryGetWorksheet("Summary", out var summarySheet));
        Assert.True(workbook.Worksheets.TryGetWorksheet("Project totals", out var projectSheet));
        Assert.True(workbook.Worksheets.TryGetWorksheet("Data quality", out _));
        Assert.NotNull(summarySheet);
        Assert.NotNull(projectSheet);
        Assert.Equal("Proliferation project totals", summarySheet!.Cell(1, 1).GetString());
        Assert.Contains("All-time totals include", summarySheet.Cell(6, 1).GetString());

        Assert.Equal("S.No.", projectSheet!.Cell(6, 1).GetString());
        Assert.Equal("Project", projectSheet.Cell(6, 2).GetString());
        Assert.Equal("SDD", projectSheet.Cell(6, 3).GetString());
        Assert.Equal("515 ABW", projectSheet.Cell(6, 4).GetString());
        Assert.Equal("Total proliferation", projectSheet.Cell(6, 5).GetString());
        Assert.DoesNotContain("Code", projectSheet.Row(6).CellsUsed().Select(cell => cell.GetString()));
        Assert.Equal("AURA", projectSheet.Cell(7, 2).GetString());
        Assert.Equal(150, projectSheet.Cell(7, 5).GetValue<int>());
        Assert.Single(projectSheet.Tables);
    }

    [Fact]
    public void YearWorkbook_UsesNormalizedYearAndProjectYearSheets()
    {
        var builder = new ProliferationYearBreakdownCardExcelWorkbookBuilder();

        using var workbook = Open(builder.Build(CreateSummary(), CreateMetadata(allTime: false)));

        Assert.True(workbook.Worksheets.TryGetWorksheet("Year totals", out var yearSheet));
        Assert.True(workbook.Worksheets.TryGetWorksheet("Project-year data", out var projectYearSheet));
        Assert.False(workbook.Worksheets.TryGetWorksheet("2026", out _));
        Assert.NotNull(yearSheet);
        Assert.NotNull(projectYearSheet);
        Assert.Equal("Year", yearSheet!.Cell(6, 1).GetString());
        Assert.Equal(2026, yearSheet.Cell(7, 1).GetValue<int>());
        Assert.Equal("Year", projectYearSheet!.Cell(6, 1).GetString());
        Assert.Equal("AURA", projectYearSheet.Cell(7, 2).GetString());
        Assert.DoesNotContain("Code", projectYearSheet.Row(6).CellsUsed().Select(cell => cell.GetString()));
        Assert.Single(yearSheet.Tables);
        Assert.Single(projectYearSheet.Tables);
    }

    [Fact]
    public void AnalysisWorkbook_OnlyIncludesUnitSummaryWhenRequested()
    {
        var builder = new ProliferationAnalysisExcelBuilder();
        var report = CreateAnalysisReport();

        using var withoutUnits = Open(builder.Build(report, CreateMetadata(allTime: true) with
        {
            IncludesUnitSummary = false
        }));
        Assert.True(withoutUnits.Worksheets.TryGetWorksheet("Simulator breakdown", out var simulatorSheet));
        Assert.False(withoutUnits.Worksheets.TryGetWorksheet("Unit summary", out _));
        Assert.NotNull(simulatorSheet);
        Assert.Single(simulatorSheet!.Tables);
        Assert.DoesNotContain("Code", simulatorSheet.Row(6).CellsUsed().Select(cell => cell.GetString()));
        Assert.Equal("Report total", simulatorSheet.Cell(9, 1).GetString());

        using var withUnits = Open(builder.Build(report, CreateMetadata(allTime: true) with
        {
            IncludesUnitSummary = true
        }));
        Assert.True(withUnits.Worksheets.TryGetWorksheet("Unit summary", out var unitSheet));
        Assert.NotNull(unitSheet);
        Assert.Single(unitSheet!.Tables);
        Assert.Equal("Receiving unit", unitSheet.Cell(8, 1).GetString());
        Assert.DoesNotContain("Code", unitSheet.Row(8).CellsUsed().Select(cell => cell.GetString()));
        Assert.Equal(new DateTime(2026, 1, 10), unitSheet.Cell(9, 6).GetDateTime().Date);
    }

    [Fact]
    public void AnalysisWorkbook_WritesInvalidChronologyDatesAsTextWithoutFailing()
    {
        var builder = new ProliferationAnalysisExcelBuilder();
        var report = CreateAnalysisReport(new DateOnly(1, 1, 1), new DateOnly(2026, 2, 15));

        using var workbook = Open(builder.Build(report, CreateMetadata(allTime: true) with
        {
            IncludesUnitSummary = true
        }));

        var unitSheet = workbook.Worksheet("Unit summary");
        Assert.Equal("01-Jan-0001", unitSheet.Cell(9, 6).GetString());
        Assert.Equal(new DateTime(2026, 2, 15), unitSheet.Cell(9, 7).GetDateTime().Date);
    }

    [Fact]
    public void ChronologyDisclosure_UsesSingularQuantityWording()
    {
        var quality = new ProliferationChronologyQualitySummary(1, 1, 1, 2000, 2027);

        var message = ProliferationChronologyQualityService.BuildDisclosure(quality, allTimeReport: true);

        Assert.Contains("1 reported unit from 1 approved record", message);
        Assert.DoesNotContain("1 reported units", message);
    }

    private static ProliferationSummaryViewModel CreateSummary()
    {
        var byProject = new[]
        {
            new ProliferationSummaryProjectRow(
                1,
                "AURA",
                "AURA-01",
                new ProliferationSummarySourceTotals(150, 100, 50)),
            new ProliferationSummaryProjectRow(
                2,
                "ASTRAE",
                "AST-01",
                new ProliferationSummarySourceTotals(80, 80, 0))
        };
        var byYear = new[]
        {
            new ProliferationSummaryYearRow(
                2026,
                new ProliferationSummarySourceTotals(160, 110, 50)),
            new ProliferationSummaryYearRow(
                2025,
                new ProliferationSummarySourceTotals(50, 50, 0))
        };
        var byProjectYear = new[]
        {
            new ProliferationSummaryProjectYearRow(
                1,
                "AURA",
                "AURA-01",
                2026,
                new ProliferationSummarySourceTotals(120, 70, 50)),
            new ProliferationSummaryProjectYearRow(
                2,
                "ASTRAE",
                "AST-01",
                2026,
                new ProliferationSummarySourceTotals(40, 40, 0)),
            new ProliferationSummaryProjectYearRow(
                1,
                "AURA",
                "AURA-01",
                2025,
                new ProliferationSummarySourceTotals(50, 50, 0))
        };

        return new ProliferationSummaryViewModel(byProject, byYear, byProjectYear);
    }

    private static ProliferationAnalysisResultDto CreateAnalysisReport(
        DateOnly? firstDate = null,
        DateOnly? lastDate = null)
        => new()
        {
            ScopeLabel = "All proliferation",
            PeriodLabel = "All time",
            SourceLabel = "All sources",
            CalculationBasis = "Configured counting rule.",
            CoverageMessage = "Unit names are available from approved detailed entries.",
            DataQualityMessage = "All-time totals include 20 reported units from 2 approved records assigned to invalid years.",
            InvalidChronologyRecordCount = 2,
            InvalidChronologyPositionCount = 1,
            InvalidChronologyReportedQuantity = 20,
            MinimumValidYear = 2000,
            MaximumValidYear = 2027,
            Summary = new ProliferationAnalysisSummaryDto
            {
                TotalProliferation = 150,
                SddTotal = 100,
                Abw515Total = 50,
                ProjectCount = 1,
                TechnicalCategoryCount = 1,
                ReceivingUnitCount = 1,
                ApprovedAnnualQuantity = 100,
                ApprovedDetailedQuantity = 50,
                UnitBreakdownQuantity = 50,
                HasUnitBreakdown = true,
                UnitDataLoaded = true
            },
            Projects = new[]
            {
                new ProliferationAnalysisProjectRowDto
                {
                    ProjectId = 1,
                    ProjectName = "AURA",
                    ProjectCode = "AURA-01",
                    TechnicalCategory = "AR/VR",
                    SddQuantity = 100,
                    Abw515Quantity = 50,
                    TotalQuantity = 150
                }
            },
            Units = new[]
            {
                new ProliferationAnalysisUnitRowDto
                {
                    UnitName = "Unit 1",
                    ProjectId = 1,
                    ProjectName = "AURA",
                    ProjectCode = "AURA-01",
                    SourceLabel = "SDD",
                    Quantity = 50,
                    EntryCount = 2,
                    FirstDate = firstDate ?? new DateOnly(2026, 1, 10),
                    LastDate = lastDate ?? new DateOnly(2026, 2, 15)
                }
            }
        };

    private static ProliferationExportMetadata CreateMetadata(bool allTime)
    {
        var quality = new ProliferationChronologyQualitySummary(2, 1, 20, 2000, 2027);
        return new ProliferationExportMetadata(
            GeneratedAtUtc,
            "Test User",
            quality,
            ProliferationChronologyQualityService.BuildDisclosure(quality, allTime));
    }

    private static XLWorkbook Open(byte[] content)
        => new(new MemoryStream(content));
}
