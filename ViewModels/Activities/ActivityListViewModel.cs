using System;
using System.Collections.Generic;
using ProjectManagement.Contracts.Activities;

namespace ProjectManagement.ViewModels.Activities;

public sealed record ActivityListViewModel(
    IReadOnlyList<ActivityListRowViewModel> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    ActivityListSort Sort,
    bool SortDescending,
    ActivityAttachmentTypeFilter AttachmentFilter,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int? ActivityTypeId);

public sealed record ActivityListRowViewModel(
    int Id,
    string Title,
    string ActivityType,
    string? Location,
    DateTimeOffset? ScheduledStartUtc,
    DateTimeOffset? ScheduledEndUtc,
    DateTimeOffset CreatedAtUtc,
    string CreatedByDisplayName,
    string? CreatedByEmail,
    int AttachmentCount,
    int PdfAttachmentCount,
    int PhotoAttachmentCount,
    int VideoAttachmentCount,
    bool CanEdit,
    bool CanDelete)
{
    public bool HasPdf => PdfAttachmentCount > 0;
    public bool HasPhoto => PhotoAttachmentCount > 0;
    public bool HasVideo => VideoAttachmentCount > 0;
}
