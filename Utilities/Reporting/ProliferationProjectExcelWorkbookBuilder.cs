using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Utilities;

namespace ProjectManagement.Utilities.Reporting;

// SECTION: Export context
public sealed record ProliferationProjectExcelWorkbookContext(
    ProliferationProjectAggregationResult Aggregates,
    DateTimeOffset GeneratedAtUtc,
    string RequestedByUserId,
    ProliferationProjectExportFilterMetadata Filters);

public sealed record ProliferationProjectExportFilterMetadata(
    IReadOnlyList<int> Years,
    DateOnly? FromDate,
    DateOnly? ToDate,
    string SourceLabel,
    string ProjectCategoryLabel,
    string TechnicalCategoryLabel,
    string? SearchTerm);

// SECTION: Builder contract
public interface IProliferationProjectExcelWorkbookBuilder
{
    byte[] Build(ProliferationProjectExcelWorkbookContext context);
}

// SECTION: Builder implementation
public sealed class ProliferationProjectExcelWorkbookBuilder : IProliferationProjectExcelWorkbookBuilder
{
    public byte[] Build(ProliferationProjectExcelWorkbookContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Aggregates);

        using var workbook = new XLWorkbook();

        WriteAllProjectsSheet(workbook, context);
        WriteProjectYearSheet(workbook, context);
        WriteProjectUnitSheet(workbook, context);
        WriteUnitProjectsSheet(workbook, context);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // SECTION: All projects sheet
    private static void WriteAllProjectsSheet(XLWorkbook workbook, ProliferationProjectExcelWorkbookContext context)
    {
        var worksheet = workbook.Worksheets.Add("All Projects");
        var headers = new[] { "Project", "Project code", "Total", "SDD", "ABW 515" };
        WriteHeader(worksheet, headers);

        var rowNumber = 2;
        foreach (var row in context.Aggregates.AllProjectTotals)
        {
            WriteProjectRow(worksheet, rowNumber, 3, row.ProjectName, row.ProjectCode, row.Totals);
            rowNumber++;
        }

        ApplyMetadata(worksheet, context.Aggregates.AllProjectTotals.Count, headers.Length, context);
    }

    // SECTION: Project by year sheet
    private static void WriteProjectYearSheet(XLWorkbook workbook, ProliferationProjectExcelWorkbookContext context)
    {
        var worksheet = workbook.Worksheets.Add("Project By Year");
        var headers = new[] { "Project", "Project code", "Year", "Total", "SDD", "ABW 515" };
        WriteHeader(worksheet, headers);

        var rowNumber = 2;
        foreach (var row in context.Aggregates.ProjectYearTotals)
        {
            WriteProjectRow(worksheet, rowNumber, 4, row.ProjectName, row.ProjectCode, row.Totals);
            worksheet.Cell(rowNumber, 3).Value = row.Year;
            rowNumber++;
        }

        ApplyMetadata(worksheet, context.Aggregates.ProjectYearTotals.Count, headers.Length, context);
    }

    // SECTION: Project by unit sheet
    private static void WriteProjectUnitSheet(XLWorkbook workbook, ProliferationProjectExcelWorkbookContext context)
    {
        var worksheet = workbook.Worksheets.Add("Project By Unit");
        var headers = new[] { "Project", "Project code", "Unit", "Total", "SDD", "ABW 515" };
        WriteHeader(worksheet, headers);

        var rowNumber = 2;
        foreach (var row in context.Aggregates.ProjectUnitTotals)
        {
            WriteProjectRow(worksheet, rowNumber, 4, row.ProjectName, row.ProjectCode, row.Totals);
            worksheet.Cell(rowNumber, 3).Value = row.UnitName;
            rowNumber++;
        }

        worksheet.Column(3).Style.Alignment.WrapText = true;
        worksheet.Column(3).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        ApplyMetadata(worksheet, context.Aggregates.ProjectUnitTotals.Count, headers.Length, context);
    }

    // SECTION: Unit to projects sheet
    private static void WriteUnitProjectsSheet(XLWorkbook workbook, ProliferationProjectExcelWorkbookContext context)
    {
        var worksheet = workbook.Worksheets.Add("Unit Projects");
        var headers = new[] { "Unit", "Project", "Project code" };
        WriteHeader(worksheet, headers);

        var rowNumber = 2;
        foreach (var row in context.Aggregates.UnitProjectMap)
        {
            foreach (var project in row.Projects)
            {
                worksheet.Cell(rowNumber, 1).Value = row.UnitName;
                worksheet.Cell(rowNumber, 2).Value = project.ProjectName;
                worksheet.Cell(rowNumber, 3).Value = project.ProjectCode ?? string.Empty;
                rowNumber++;
            }
        }

        worksheet.Columns(1, 2).Style.Alignment.WrapText = true;
        worksheet.Columns(1, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        ApplyMetadata(worksheet, Math.Max(0, rowNumber - 2), headers.Length, context);
    }

    // SECTION: Worksheet helpers
    private static void WriteHeader(IXLWorksheet worksheet, IReadOnlyList<string> headers)
    {
        for (var column = 0; column < headers.Count; column++)
        {
            var cell = worksheet.Cell(1, column + 1);
            cell.Value = headers[column];
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Count);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(233, 236, 239);
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.SheetView.FreezeRows(1);
    }

    private static void WriteProjectRow(
        IXLWorksheet worksheet,
        int rowNumber,
        int totalsColumnStart,
        string projectName,
        string? projectCode,
        ProliferationSummarySourceTotals totals)
    {
        worksheet.Cell(rowNumber, 1).Value = projectName;
        worksheet.Cell(rowNumber, 2).Value = projectCode ?? string.Empty;
        worksheet.Cell(rowNumber, totalsColumnStart).Value = totals.Total;
        worksheet.Cell(rowNumber, totalsColumnStart + 1).Value = totals.Sdd;
        worksheet.Cell(rowNumber, totalsColumnStart + 2).Value = totals.Abw515;
    }

    private static void ApplyMetadata(
        IXLWorksheet worksheet,
        int rowCount,
        int columnCount,
        ProliferationProjectExcelWorkbookContext context)
    {
        var lastRow = Math.Max(2, rowCount + 1);
        worksheet.Columns(1, columnCount).AdjustToContents(1, lastRow);

        foreach (var column in worksheet.Columns(1, columnCount))
        {
            if (column.Width > 60)
            {
                column.Width = 60;
            }
        }

        var metadataRow = rowCount + 3;
        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());

        worksheet.Cell(metadataRow, 1).Value = "Export generated";
        worksheet.Cell(metadataRow, 2).Value = generatedAtIst.DateTime;
        worksheet.Cell(metadataRow, 2).Style.DateFormat.Format = "yyyy-MM-dd HH:mm\" IST\"";

        worksheet.Cell(metadataRow + 1, 1).Value = "Requested by";
        worksheet.Cell(metadataRow + 1, 2).Value = string.IsNullOrWhiteSpace(context.RequestedByUserId)
            ? "(unknown)"
            : context.RequestedByUserId;

        var filters = context.Filters;
        worksheet.Cell(metadataRow + 2, 1).Value = "Years";
        worksheet.Cell(metadataRow + 2, 2).Value = filters.Years.Count > 0
            ? string.Join(", ", filters.Years)
            : "(all years)";

        worksheet.Cell(metadataRow + 3, 1).Value = "Date range";
        worksheet.Cell(metadataRow + 3, 2).Value = filters.FromDate.HasValue || filters.ToDate.HasValue
            ? $"{filters.FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "start"} \u2192 {filters.ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "end"}"
            : "(not set)";

        worksheet.Cell(metadataRow + 4, 1).Value = "Source";
        worksheet.Cell(metadataRow + 4, 2).Value = string.IsNullOrWhiteSpace(filters.SourceLabel)
            ? "(all sources)"
            : filters.SourceLabel;

        worksheet.Cell(metadataRow + 5, 1).Value = "Project category";
        worksheet.Cell(metadataRow + 5, 2).Value = string.IsNullOrWhiteSpace(filters.ProjectCategoryLabel)
            ? "(all categories)"
            : filters.ProjectCategoryLabel;

        worksheet.Cell(metadataRow + 6, 1).Value = "Technical category";
        worksheet.Cell(metadataRow + 6, 2).Value = string.IsNullOrWhiteSpace(filters.TechnicalCategoryLabel)
            ? "(all technical)"
            : filters.TechnicalCategoryLabel;

        worksheet.Cell(metadataRow + 7, 1).Value = "Search";
        worksheet.Cell(metadataRow + 7, 2).Value = string.IsNullOrWhiteSpace(filters.SearchTerm)
            ? "(not set)"
            : filters.SearchTerm;

        worksheet.Range(metadataRow, 1, metadataRow + 7, 1).Style.Font.Bold = true;
    }
}
