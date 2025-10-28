using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ProjectManagement.Contracts.Activities;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityExportService : IActivityExportService
{
    private readonly IActivityRepository _activityRepository;
    private readonly IActivityAttachmentManager _attachmentManager;
    private static readonly TimeZoneInfo IndiaTimeZone = GetIndiaTimeZone();

    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public ActivityExportService(IActivityRepository activityRepository,
                                 IActivityAttachmentManager attachmentManager)
    {
        _activityRepository = activityRepository;
        _attachmentManager = attachmentManager;
    }

    public async Task<ActivityExportResult?> ExportAsync(ActivityExportRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var listRequest = new ActivityListRequest(1,
            0,
            request.Sort,
            request.SortDescending,
            request.FromDate,
            request.ToDate,
            request.ActivityTypeId,
            request.CreatedByUserId,
            request.CreatedBySearch,
            request.AttachmentType);

        var result = await _activityRepository.ListAsync(listRequest, cancellationToken);

        if (result.TotalCount == 0)
        {
            return null;
        }

        var metadata = await LoadAttachmentMetadataAsync(result.Items, cancellationToken);
        var generatedAt = DateTime.UtcNow;

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Activities");

        WriteHeader(worksheet);
        WriteRows(worksheet, result.Items, metadata);

        worksheet.Columns().AdjustToContents();
        worksheet.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"MiscActivities_{generatedAt:yyyyMMdd_HHmm}.xlsx";
        return new ActivityExportResult(fileName, ExcelContentType, stream.ToArray());
    }

    private async Task<Dictionary<int, IReadOnlyList<ActivityAttachmentMetadata>>> LoadAttachmentMetadataAsync(
        IReadOnlyList<ActivityListItem> items,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<int, IReadOnlyList<ActivityAttachmentMetadata>>(items.Count);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var activity = await _activityRepository.GetByIdAsync(item.Id, cancellationToken);
            if (activity is null || activity.IsDeleted)
            {
                metadata[item.Id] = Array.Empty<ActivityAttachmentMetadata>();
                continue;
            }

            var attachments = _attachmentManager.CreateMetadata(activity)
                .OrderBy(a => a.UploadedAtUtc)
                .ThenBy(a => a.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            metadata[item.Id] = attachments;
        }

        return metadata;
    }

    private static void WriteHeader(IXLWorksheet worksheet)
    {
        var headers = new[]
        {
            "Title",
            "Activity type",
            "Scheduled start date",
            "Scheduled end date",
            "Created date",
            "Created by",
            "PDF attachments",
            "Photo attachments",
            "Video attachments",
            "Total attachments",
            "Attachment links"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            var cell = worksheet.Cell(1, column + 1);
            cell.Value = headers[column];
            cell.Style.Font.Bold = true;
        }
    }

    private static void WriteRows(IXLWorksheet worksheet,
                                  IReadOnlyList<ActivityListItem> items,
                                  IReadOnlyDictionary<int, IReadOnlyList<ActivityAttachmentMetadata>> metadata)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var rowNumber = index + 2;

            worksheet.Cell(rowNumber, 1).Value = item.Title;
            worksheet.Cell(rowNumber, 2).Value = item.ActivityTypeName;

            SetDateCell(worksheet.Cell(rowNumber, 3), item.ScheduledStartUtc);
            SetDateCell(worksheet.Cell(rowNumber, 4), item.ScheduledEndUtc);
            SetDateCell(worksheet.Cell(rowNumber, 5), item.CreatedAtUtc);

            worksheet.Cell(rowNumber, 6).Value = ResolveCreatedBy(item);
            worksheet.Cell(rowNumber, 7).Value = item.PdfAttachmentCount;
            worksheet.Cell(rowNumber, 8).Value = item.PhotoAttachmentCount;
            worksheet.Cell(rowNumber, 9).Value = item.VideoAttachmentCount;
            worksheet.Cell(rowNumber, 10).Value = item.AttachmentCount;

            if (!metadata.TryGetValue(item.Id, out var attachments) || attachments.Count == 0)
            {
                continue;
            }

            var formula = BuildAttachmentFormula(attachments);
            if (string.IsNullOrEmpty(formula))
            {
                continue;
            }

            var cell = worksheet.Cell(rowNumber, 11);
            cell.SetFormulaA1(formula);
            cell.Style.Alignment.WrapText = true;
        }
    }

    private static void SetDateCell(IXLCell cell, DateTimeOffset? value)
    {
        if (value is null)
        {
            cell.Clear();
            return;
        }

        var localDate = TimeZoneInfo.ConvertTime(value.Value.UtcDateTime, IndiaTimeZone).Date;
        cell.Value = localDate;
        cell.Style.DateFormat.Format = "yyyy-mm-dd";
    }

    private static TimeZoneInfo GetIndiaTimeZone()
    {
        string[] timeZoneIds = { "India Standard Time", "Asia/Kolkata" };

        foreach (var timeZoneId in timeZoneIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        var offset = TimeSpan.FromHours(5.5);
        return TimeZoneInfo.CreateCustomTimeZone("Asia/Kolkata", offset, "India Standard Time", "India Standard Time");
    }

    private static string ResolveCreatedBy(ActivityListItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.CreatedByDisplayName))
        {
            return item.CreatedByDisplayName;
        }

        if (!string.IsNullOrWhiteSpace(item.CreatedByEmail))
        {
            return item.CreatedByEmail;
        }

        return item.CreatedByUserId;
    }

    private static string BuildAttachmentFormula(IReadOnlyList<ActivityAttachmentMetadata> attachments)
    {
        if (attachments.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < attachments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append("&CHAR(10)&");
            }

            var attachment = attachments[i];
            builder.Append("HYPERLINK(\"")
                   .Append(EscapeForFormula(attachment.DownloadUrl))
                   .Append("\",\"")
                   .Append(EscapeForFormula(attachment.FileName))
                   .Append("\")");
        }

        return builder.ToString();
    }

    private static string EscapeForFormula(string value)
    {
        return value.Replace("\"", "\"\"");
    }
}
