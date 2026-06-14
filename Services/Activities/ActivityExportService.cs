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

    // SECTION: Spreadsheet column indexes
    private const int ActivityTitleColumn = 1;
    private const int ActivityTypeColumn = 2;
    private const int RemarksColumn = 3;
    private const int EventDateColumn = 4;
    private const int EndDateColumn = 5;
    private const int CreatedDateColumn = 6;
    private const int CreatedByColumn = 7;
    private const int PdfAttachmentsColumn = 8;
    private const int PhotoAttachmentsColumn = 9;
    private const int VideoAttachmentsColumn = 10;
    private const int TotalAttachmentsColumn = 11;
    private const int AttachmentLinksColumn = 12;

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

        // SECTION: Export follows the Activities index media-filter behavior
        var listRequest = new ActivityListRequest(1,
            0,
            request.Sort,
            request.SortDescending,
            request.FromDate,
            request.ToDate,
            request.ActivityTypeId,
            request.CreatedByUserId,
            ActivityAttachmentTypeFilter.Any,
            request.Search,
            request.MediaFilter);

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
            "Activity title",
            "Activity type",
            "Remarks / Brief",
            "Event date",
            "End date",
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

            // SECTION: Core activity details
            worksheet.Cell(rowNumber, ActivityTitleColumn).Value = item.Title;
            worksheet.Cell(rowNumber, ActivityTypeColumn).Value = item.ActivityTypeName;
            worksheet.Cell(rowNumber, RemarksColumn).Value = item.RemarksPreview ?? string.Empty;
            worksheet.Cell(rowNumber, RemarksColumn).Style.Alignment.WrapText = true;

            // SECTION: Activity dates and ownership
            SetDateCell(worksheet.Cell(rowNumber, EventDateColumn), item.ScheduledStartUtc);
            SetDateCell(worksheet.Cell(rowNumber, EndDateColumn), item.ScheduledEndUtc);
            SetDateCell(worksheet.Cell(rowNumber, CreatedDateColumn), item.CreatedAtUtc);

            worksheet.Cell(rowNumber, CreatedByColumn).Value = ResolveCreatedBy(item);

            // SECTION: Attachment counts
            worksheet.Cell(rowNumber, PdfAttachmentsColumn).Value = item.PdfAttachmentCount;
            worksheet.Cell(rowNumber, PhotoAttachmentsColumn).Value = item.PhotoAttachmentCount;
            worksheet.Cell(rowNumber, VideoAttachmentsColumn).Value = item.VideoAttachmentCount;
            worksheet.Cell(rowNumber, TotalAttachmentsColumn).Value = item.AttachmentCount;

            if (!metadata.TryGetValue(item.Id, out var attachments) || attachments.Count == 0)
            {
                continue;
            }

            var formula = BuildAttachmentFormula(attachments);
            if (string.IsNullOrEmpty(formula))
            {
                continue;
            }

            var cell = worksheet.Cell(rowNumber, AttachmentLinksColumn);
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
