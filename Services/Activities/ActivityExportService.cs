using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Contracts.Activities;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityExportService : IActivityExportService
{
    private readonly IActivityRepository _activityRepository;
    private readonly IActivityTypeRepository _activityTypeRepository;
    private readonly IActivityAttachmentManager _attachmentManager;

    public ActivityExportService(IActivityRepository activityRepository,
                                 IActivityTypeRepository activityTypeRepository,
                                 IActivityAttachmentManager attachmentManager)
    {
        _activityRepository = activityRepository;
        _activityTypeRepository = activityTypeRepository;
        _attachmentManager = attachmentManager;
    }

    public async Task<ActivityExportResult> ExportByTypeAsync(int activityTypeId, CancellationToken cancellationToken = default)
    {
        var type = await _activityTypeRepository.GetByIdAsync(activityTypeId, cancellationToken);
        if (type is null)
        {
            throw new KeyNotFoundException("Activity type not found.");
        }

        var activities = await _activityRepository.ListByTypeAsync(activityTypeId, cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine("Title,Start (UTC),End (UTC),Location,Owner,Created,Last Modified,Attachments");

        foreach (var activity in activities)
        {
            var title = Escape(activity.Title);
            var start = activity.ScheduledStartUtc?.ToString("u", CultureInfo.InvariantCulture) ?? string.Empty;
            var end = activity.ScheduledEndUtc?.ToString("u", CultureInfo.InvariantCulture) ?? string.Empty;
            var location = Escape(activity.Location ?? string.Empty);
            var owner = activity.CreatedByUserId;
            var created = activity.CreatedAtUtc.ToString("u", CultureInfo.InvariantCulture);
            var modified = activity.LastModifiedAtUtc?.ToString("u", CultureInfo.InvariantCulture) ?? created;
            var attachmentMetadata = _attachmentManager.CreateMetadata(activity);
            var attachmentCount = attachmentMetadata.Count.ToString(CultureInfo.InvariantCulture);
            var attachmentSummary = attachmentMetadata.Count == 0
                ? string.Empty
                : Escape(string.Join(" | ", attachmentMetadata.Select(a => $"{a.FileName} ({a.DownloadUrl})")));

            builder.Append(title).Append(',')
                   .Append(start).Append(',')
                   .Append(end).Append(',')
                   .Append(location).Append(',')
                   .Append(Escape(owner)).Append(',')
                   .Append(created).Append(',')
                   .Append(modified).Append(',')
                   .Append(attachmentCount).Append(',')
                   .Append(attachmentSummary)
                   .AppendLine();
        }

        var fileName = $"{SanitizeFileName(type.Name)}-activities.csv";
        var content = Encoding.UTF8.GetBytes(builder.ToString());
        return new ActivityExportResult(fileName, "text/csv", content);
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
        {
            var escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        return value;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '-' : c);
        }

        return builder.ToString();
    }
}
