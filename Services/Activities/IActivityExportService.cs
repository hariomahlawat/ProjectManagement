using System;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Contracts.Activities;

namespace ProjectManagement.Services.Activities;

public interface IActivityExportService
{
    Task<ActivityExportResult?> ExportAsync(ActivityExportRequest request, CancellationToken cancellationToken = default);
}

public sealed record ActivityExportResult(string FileName, string ContentType, byte[] Content);

public sealed record ActivityExportRequest(
    ActivityListSort Sort = ActivityListSort.ScheduledStart,
    bool SortDescending = true,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int? ActivityTypeId = null,
    string? CreatedByUserId = null,
    string? CreatedBySearch = null,
    ActivityAttachmentTypeFilter AttachmentType = ActivityAttachmentTypeFilter.Any);
