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

public sealed record ActivityListRequest(
    int Page = 1,
    int PageSize = 25,
    ActivityListSort Sort = ActivityListSort.ScheduledStart,
    bool SortDescending = true,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int? ActivityTypeId = null,
    string? CreatedByUserId = null,
    string? CreatedBySearch = null,
    ActivityAttachmentTypeFilter AttachmentType = ActivityAttachmentTypeFilter.Any);

public sealed record ActivityListItem(
    int Id,
    string Title,
    string ActivityTypeName,
    int ActivityTypeId,
    string? Location,
    DateTimeOffset? ScheduledStartUtc,
    DateTimeOffset? ScheduledEndUtc,
    DateTimeOffset CreatedAtUtc,
    string CreatedByUserId,
    string? CreatedByDisplayName,
    string? CreatedByEmail,
    int AttachmentCount,
    int PdfAttachmentCount,
    int PhotoAttachmentCount,
    int VideoAttachmentCount);

public sealed record ActivityListResult(
    IReadOnlyList<ActivityListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    ActivityListSort Sort,
    bool SortDescending);
