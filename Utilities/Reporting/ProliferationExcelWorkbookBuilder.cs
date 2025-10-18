using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Utilities;

namespace ProjectManagement.Utilities.Reporting;

public interface IProliferationExcelWorkbookBuilder
{
    byte[] Build(ProliferationExcelWorkbookContext context);
}

public sealed record ProliferationExcelWorkbookContext(
    IReadOnlyList<ProliferationTrackerRow> Rows,
    DateTimeOffset GeneratedAtUtc,
    ProliferationExportRequest Request);

public sealed class ProliferationExcelWorkbookBuilder : IProliferationExcelWorkbookBuilder
{
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm\" IST\"";

    public byte[] Build(ProliferationExcelWorkbookContext context)
    {
        if (context.Rows is null)
        {
            throw new ArgumentNullException(nameof(context.Rows));
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Proliferation Tracker");
        WriteHeader(worksheet);
        WriteRows(worksheet, context.Rows);
        ApplyMetadata(worksheet, context);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteHeader(IXLWorksheet worksheet)
    {
        var headers = new[]
        {
            "S.No.",
            "Project",
            "Sponsoring unit",
            "Simulator",
            "Source",
            "Year",
            "Yearly direct",
            "Yearly indirect",
            "Yearly investment",
            "Granular direct",
            "Granular indirect",
            "Granular investment",
            "Effective direct",
            "Effective indirect",
            "Effective investment",
            "Variance direct",
            "Variance indirect",
            "Variance investment",
            "Preference mode",
            "Preferred year",
            "Preferred year matches"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = headers[column];
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(233, 236, 239);
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.SheetView.FreezeRows(1);
    }

    private static void WriteRows(IXLWorksheet worksheet, IReadOnlyList<ProliferationTrackerRow> rows)
    {
        var rowNumber = 2;
        for (var index = 0; index < rows.Count; index++, rowNumber++)
        {
            var row = rows[index];
            worksheet.Cell(rowNumber, 1).Value = index + 1;
            worksheet.Cell(rowNumber, 2).Value = row.ProjectName;
            worksheet.Cell(rowNumber, 3).Value = row.SponsoringUnitName ?? string.Empty;
            worksheet.Cell(rowNumber, 4).Value = row.SimulatorDisplayName
                ?? row.SimulatorUserId
                ?? string.Empty;
            worksheet.Cell(rowNumber, 5).Value = FormatSource(row.Source);
            worksheet.Cell(rowNumber, 6).Value = row.Year;

            WriteMetricSet(worksheet, rowNumber, 7, row.Yearly);
            WriteMetricSet(worksheet, rowNumber, 10, row.GranularSum);
            WriteMetricSet(worksheet, rowNumber, 13, row.Effective);
            WriteMetricSet(worksheet, rowNumber, 16, row.Variance);

            worksheet.Cell(rowNumber, 19).Value = FormatMode(row.Preference.Mode);
            worksheet.Cell(rowNumber, 20).Value = row.Preference.PreferredYear?.ToString(CultureInfo.InvariantCulture)
                ?? "—";
            worksheet.Cell(rowNumber, 21).Value = row.Preference.PreferredYearMatches ? "Yes" : "No";
        }

        worksheet.Columns(2, 4).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    private static void ApplyMetadata(IXLWorksheet worksheet, ProliferationExcelWorkbookContext context)
    {
        var lastRow = Math.Max(2, context.Rows.Count + 1);
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        worksheet.Columns(1, lastColumn).AdjustToContents(1, lastRow);

        foreach (var column in worksheet.Columns(1, lastColumn))
        {
            if (column.Width > 60)
            {
                column.Width = 60;
            }
        }

        worksheet.Column(2).Width = Math.Min(worksheet.Column(2).Width, 50);
        worksheet.Column(4).Width = Math.Min(worksheet.Column(4).Width, 40);

        var metadataRow = context.Rows.Count + 3;
        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());

        worksheet.Cell(metadataRow, 1).Value = "Export generated";
        worksheet.Cell(metadataRow, 2).Value = generatedAtIst.DateTime;
        worksheet.Cell(metadataRow, 2).Style.DateFormat.Format = DateTimeFormat;

        worksheet.Cell(metadataRow + 1, 1).Value = "Source";
        worksheet.Cell(metadataRow + 1, 2).Value = context.Request.Source?.ToString() ?? "All";

        worksheet.Cell(metadataRow + 2, 1).Value = "Year from";
        worksheet.Cell(metadataRow + 2, 2).Value = context.Request.YearFrom?.ToString(CultureInfo.InvariantCulture) ?? "(not set)";

        worksheet.Cell(metadataRow + 3, 1).Value = "Year to";
        worksheet.Cell(metadataRow + 3, 2).Value = context.Request.YearTo?.ToString(CultureInfo.InvariantCulture) ?? "(not set)";

        worksheet.Cell(metadataRow + 4, 1).Value = "Sponsoring unit";
        worksheet.Cell(metadataRow + 4, 2).Value = context.Request.SponsoringUnitId?.ToString(CultureInfo.InvariantCulture) ?? "All";

        worksheet.Cell(metadataRow + 5, 1).Value = "Simulator";
        worksheet.Cell(metadataRow + 5, 2).Value = context.Request.SimulatorUserId ?? "All";

        worksheet.Cell(metadataRow + 6, 1).Value = "Search term";
        worksheet.Cell(metadataRow + 6, 2).Value = string.IsNullOrWhiteSpace(context.Request.SearchTerm)
            ? "(not set)"
            : context.Request.SearchTerm;

        worksheet.Range(metadataRow, 1, metadataRow + 6, 1).Style.Font.Bold = true;
    }

    private static void WriteMetricSet(IXLWorksheet worksheet, int rowNumber, int startColumn, ProliferationMetricsDto? metrics)
    {
        WriteMetricCell(worksheet.Cell(rowNumber, startColumn), metrics?.DirectBeneficiaries);
        WriteMetricCell(worksheet.Cell(rowNumber, startColumn + 1), metrics?.IndirectBeneficiaries);
        WriteMetricCell(worksheet.Cell(rowNumber, startColumn + 2), metrics?.InvestmentValue);
    }

    private static void WriteMetricCell(IXLCell cell, int? value)
    {
        if (value.HasValue)
        {
            cell.Value = value.Value;
            cell.Style.NumberFormat.Format = "#,##0";
        }
        else
        {
            cell.Value = "—";
        }
    }

    private static void WriteMetricCell(IXLCell cell, decimal? value)
    {
        if (value.HasValue)
        {
            cell.Value = value.Value;
            cell.Style.NumberFormat.Format = "#,##0.00";
        }
        else
        {
            cell.Value = "—";
        }
    }

    private static string FormatSource(ProliferationSource source)
    {
        return source switch
        {
            ProliferationSource.Sdd => "SDD",
            ProliferationSource.Abw515 => "515 ABW",
            _ => source.ToString() ?? "Unknown"
        };
    }

    private static string FormatMode(ProliferationPreferenceMode mode)
    {
        return mode switch
        {
            ProliferationPreferenceMode.UseYearly => "Yearly",
            ProliferationPreferenceMode.UseGranular => "Granular",
            _ => "Auto"
        };
    }
}
