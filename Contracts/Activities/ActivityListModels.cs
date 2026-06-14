using System;
using System.Collections.Generic;

namespace ProjectManagement.Contracts.Activities;

public enum ActivityListSort
{
    ScheduledStart,
    CreatedAt,
    Title,
    ActivityType
}

public enum ActivityAttachmentTypeFilter
{
    Any,
    Pdf,
    Photo,
    Video
}

public enum ActivityMediaFilter
{
    Any,
    WithMedia,
    WithoutMedia,
    Photos,
    Videos,
    Documents
}

public sealed record ActivityListRequest(
    int Page = 1,
    int PageSize = 25,
    ActivityListSort Sort = ActivityListSort.ScheduledStart,
    bool SortDescending = true,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int? ActivityTypeId = null,
    string? CreatedByUserId = null,
    ActivityAttachmentTypeFilter AttachmentType = ActivityAttachmentTypeFilter.Any,
    string? Search = null,
    ActivityMediaFilter MediaFilter = ActivityMediaFilter.Any);

public sealed record ActivityListItem(
    int Id,
    string Title,
    string ActivityTypeName,
    int ActivityTypeId,
    string? Location,
    string? RemarksPreview,
    DateTimeOffset? ScheduledStartUtc,
    DateTimeOffset? ScheduledEndUtc,
    DateTimeOffset CreatedAtUtc,
    string CreatedByUserId,
    string? CreatedByDisplayName,
    string? CreatedByEmail,
    int AttachmentCount,
    int PdfAttachmentCount,
    int PhotoAttachmentCount,
    int VideoAttachmentCount,
    IReadOnlyList<ActivityMediaPreview> MediaPreviews,
    bool HasPendingDelete);

public sealed record ActivityMediaPreview(
    int AttachmentId,
    string FileName,
    string ContentType,
    string MediaKind,
    string StorageKey,
    long SizeBytes);

public sealed record ActivityListResult(
    IReadOnlyList<ActivityListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    ActivityListSort Sort,
    bool SortDescending);

/// <summary>
/// Full-result review summary for the activity list. Counts intentionally ignore
/// <see cref="ActivityListRequest.MediaFilter" /> so media chips can act as
/// stable navigation buckets while other base filters remain applied.
/// </summary>
public sealed record ActivityReviewSummaryResult(
    int All,
    int WithMedia,
    int Photos,
    int Documents,
    int Videos);
