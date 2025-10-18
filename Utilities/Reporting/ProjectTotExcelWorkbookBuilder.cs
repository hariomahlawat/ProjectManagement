using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Utilities;

namespace ProjectManagement.Utilities.Reporting;

public interface IProjectTotExcelWorkbookBuilder
{
    byte[] Build(ProjectTotExcelWorkbookContext context);
}

public sealed record ProjectTotExcelWorkbookContext(
    IReadOnlyList<ProjectTotTrackerRow> Rows,
    DateTimeOffset GeneratedAtUtc,
    ProjectTotTrackerFilter Filter);

public sealed class ProjectTotExcelWorkbookBuilder : IProjectTotExcelWorkbookBuilder
{
    private const string DateFormat = "dd-MMM-yyyy";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm\" IST\"";

    public byte[] Build(ProjectTotExcelWorkbookContext context)
    {
        if (context.Rows is null)
        {
            throw new ArgumentNullException(nameof(context.Rows));
        }

        if (context.Filter is null)
        {
            throw new ArgumentNullException(nameof(context.Filter));
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("ToT Tracker");
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
            "Lead project officer",
            "ToT status",
            "ToT started on",
            "ToT completed on",
            "MET details",
            "MET completed on",
            "First production model manufactured",
            "First production model manufactured on",
            "Last approved by",
            "Last approved on",
            "Request state",
            "Requested status",
            "Requested started on",
            "Requested completed on",
            "Requested MET details",
            "Requested MET completed on",
            "Requested first production model manufactured",
            "Requested first production model manufactured on",
            "Request submitted by",
            "Request submitted on",
            "Request decided by",
            "Request decided on",
            "Latest external remark",
            "Latest internal remark"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            var cell = worksheet.Cell(1, column + 1);
            cell.Value = headers[column];
        }

        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(233, 236, 239);
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.SheetView.FreezeRows(1);
    }

    private static void WriteRows(IXLWorksheet worksheet, IReadOnlyList<ProjectTotTrackerRow> rows)
    {
        var rowNumber = 2;
        for (var index = 0; index < rows.Count; index++, rowNumber++)
        {
            var row = rows[index];

            worksheet.Cell(rowNumber, 1).Value = index + 1;
            worksheet.Cell(rowNumber, 2).Value = row.ProjectName;
            worksheet.Cell(rowNumber, 3).Value = row.SponsoringUnit ?? string.Empty;
            worksheet.Cell(rowNumber, 4).Value = row.LeadProjectOfficerUserId ?? string.Empty;
            worksheet.Cell(rowNumber, 5).Value = FormatTotStatus(row.TotStatus);
            SetDateOnlyCell(worksheet.Cell(rowNumber, 6), row.TotStartedOn);
            SetDateOnlyCell(worksheet.Cell(rowNumber, 7), row.TotCompletedOn);
            var metDetailsCell = worksheet.Cell(rowNumber, 8);
            metDetailsCell.Value = row.TotMetDetails ?? string.Empty;
            SetDateOnlyCell(worksheet.Cell(rowNumber, 9), row.TotMetCompletedOn);
            worksheet.Cell(rowNumber, 10).Value = FormatBoolean(row.TotFirstProductionModelManufactured);
            SetDateOnlyCell(worksheet.Cell(rowNumber, 11), row.TotFirstProductionModelManufacturedOn);
            worksheet.Cell(rowNumber, 12).Value = row.TotLastApprovedBy ?? string.Empty;
            SetDateTimeCell(worksheet.Cell(rowNumber, 13), row.TotLastApprovedOnUtc);
            worksheet.Cell(rowNumber, 14).Value = FormatRequestState(row.RequestState);
            worksheet.Cell(rowNumber, 15).Value = FormatTotStatus(row.RequestedStatus);
            SetDateOnlyCell(worksheet.Cell(rowNumber, 16), row.RequestedStartedOn);
            SetDateOnlyCell(worksheet.Cell(rowNumber, 17), row.RequestedCompletedOn);
            var requestedMetCell = worksheet.Cell(rowNumber, 18);
            requestedMetCell.Value = row.RequestedMetDetails ?? string.Empty;
            SetDateOnlyCell(worksheet.Cell(rowNumber, 19), row.RequestedMetCompletedOn);
            worksheet.Cell(rowNumber, 20).Value = FormatBoolean(row.RequestedFirstProductionModelManufactured);
            SetDateOnlyCell(worksheet.Cell(rowNumber, 21), row.RequestedFirstProductionModelManufacturedOn);
            worksheet.Cell(rowNumber, 22).Value = row.RequestedBy ?? string.Empty;
            SetDateTimeCell(worksheet.Cell(rowNumber, 23), row.RequestedOnUtc);
            worksheet.Cell(rowNumber, 24).Value = row.DecidedBy ?? string.Empty;
            SetDateTimeCell(worksheet.Cell(rowNumber, 25), row.DecidedOnUtc);
            var externalRemarkCell = worksheet.Cell(rowNumber, 26);
            externalRemarkCell.Value = FormatRemark(row.LatestExternalRemark);
            var internalRemarkCell = worksheet.Cell(rowNumber, 27);
            internalRemarkCell.Value = FormatRemark(row.LatestInternalRemark);

            metDetailsCell.Style.Alignment.WrapText = true;
            requestedMetCell.Style.Alignment.WrapText = true;
            externalRemarkCell.Style.Alignment.WrapText = true;
            internalRemarkCell.Style.Alignment.WrapText = true;
        }

        worksheet.Column(8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        worksheet.Column(18).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        worksheet.Column(26).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        worksheet.Column(27).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    private static void ApplyMetadata(IXLWorksheet worksheet, ProjectTotExcelWorkbookContext context)
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

        worksheet.Column(4).Width = Math.Min(worksheet.Column(4).Width, 40);

        var metadataRow = context.Rows.Count + 3;
        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());

        worksheet.Cell(metadataRow, 1).Value = "Export generated";
        worksheet.Cell(metadataRow, 2).Value = generatedAtIst.DateTime;
        worksheet.Cell(metadataRow, 2).Style.DateFormat.Format = DateTimeFormat;

        worksheet.Cell(metadataRow + 1, 1).Value = "ToT status filter";
        worksheet.Cell(metadataRow + 1, 2).Value = FormatStatusFilter(context.Filter.TotStatus);

        worksheet.Cell(metadataRow + 2, 1).Value = "Request filter";
        worksheet.Cell(metadataRow + 2, 2).Value = FormatRequestFilter(context.Filter);

        worksheet.Cell(metadataRow + 3, 1).Value = "Started from";
        worksheet.Cell(metadataRow + 3, 2).Value = FormatDateOnly(context.Filter.StartedFrom);

        worksheet.Cell(metadataRow + 4, 1).Value = "Started to";
        worksheet.Cell(metadataRow + 4, 2).Value = FormatDateOnly(context.Filter.StartedTo);

        worksheet.Cell(metadataRow + 5, 1).Value = "Completed from";
        worksheet.Cell(metadataRow + 5, 2).Value = FormatDateOnly(context.Filter.CompletedFrom);

        worksheet.Cell(metadataRow + 6, 1).Value = "Completed to";
        worksheet.Cell(metadataRow + 6, 2).Value = FormatDateOnly(context.Filter.CompletedTo);

        worksheet.Cell(metadataRow + 7, 1).Value = "Search term";
        worksheet.Cell(metadataRow + 7, 2).Value = string.IsNullOrWhiteSpace(context.Filter.SearchTerm)
            ? "(not set)"
            : context.Filter.SearchTerm;

        worksheet.Range(metadataRow, 1, metadataRow + 7, 1).Style.Font.Bold = true;
    }

    private static void SetDateOnlyCell(IXLCell cell, DateOnly? value)
    {
        if (value.HasValue)
        {
            cell.Value = value.Value.ToDateTime(TimeOnly.MinValue);
            cell.Style.DateFormat.Format = DateFormat;
        }
        else
        {
            cell.Value = string.Empty;
        }
    }

    private static void SetDateTimeCell(IXLCell cell, DateTime? utcValue)
    {
        if (utcValue.HasValue)
        {
            var utc = DateTime.SpecifyKind(utcValue.Value, DateTimeKind.Utc);
            var ist = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneHelper.GetIst());
            cell.Value = ist;
            cell.Style.DateFormat.Format = DateTimeFormat;
        }
        else
        {
            cell.Value = string.Empty;
        }
    }

    private static string FormatTotStatus(ProjectTotStatus? status)
    {
        return status switch
        {
            ProjectTotStatus.NotRequired => "Not required",
            ProjectTotStatus.NotStarted => "Not started",
            ProjectTotStatus.InProgress => "In progress",
            ProjectTotStatus.Completed => "Completed",
            null => "—",
            _ => status.ToString() ?? "—"
        };
    }

    private static string FormatStatusFilter(ProjectTotStatus? status)
    {
        return status.HasValue ? FormatTotStatus(status) : "All";
    }

    private static string FormatRequestState(ProjectTotRequestDecisionState? state)
    {
        return state switch
        {
            ProjectTotRequestDecisionState.Pending => "Pending approval",
            ProjectTotRequestDecisionState.Approved => "Approved",
            ProjectTotRequestDecisionState.Rejected => "Rejected",
            null => "—",
            _ => state.ToString() ?? "—"
        };
    }

    private static string FormatRequestFilter(ProjectTotTrackerFilter filter)
    {
        if (filter.OnlyPendingRequests)
        {
            return "Only pending approvals";
        }

        return filter.RequestState.HasValue ? FormatRequestState(filter.RequestState) : "All";
    }

    private static string FormatBoolean(bool? value)
    {
        return value switch
        {
            true => "Yes",
            false => "No",
            _ => "—"
        };
    }

    private static string FormatDateOnly(DateOnly? value)
    {
        return value.HasValue
            ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "(not set)";
    }

    private static string FormatRemark(ProjectTotRemarkSummary? remark)
    {
        if (remark is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append(remark.Body);

        if (remark.EventDate.HasValue)
        {
            builder.AppendLine();
            builder.Append("Event: ");
            builder.Append(remark.EventDate.Value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture));
        }

        var createdUtc = DateTime.SpecifyKind(remark.CreatedAtUtc, DateTimeKind.Utc);
        var createdIst = TimeZoneInfo.ConvertTimeFromUtc(createdUtc, TimeZoneHelper.GetIst());
        builder.AppendLine();
        builder.Append("Posted: ");
        builder.Append(createdIst.ToString("dd-MMM-yyyy HH:mm 'IST'", CultureInfo.InvariantCulture));

        return builder.ToString();
    }
}
