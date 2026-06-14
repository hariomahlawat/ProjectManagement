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
    int? ActivityTypeId,
    string ViewMode,
    string? Search,
    ActivityMediaFilter MediaFilter,
    ActivityReviewSummaryViewModel Summary,
    IReadOnlyList<ActivityMonthGroupViewModel> Groups);

public sealed record ActivityListRowViewModel(
    int Id,
    string Title,
    string ActivityType,
    string? Location,
    string? RemarksPreview,
    DateTimeOffset? ScheduledStartUtc,
    DateTimeOffset? ScheduledEndUtc,
    DateTimeOffset CreatedAtUtc,
    string CreatedByDisplayName,
    string? CreatedByEmail,
    int AttachmentCount,
    int PdfAttachmentCount,
    int PhotoAttachmentCount,
    int VideoAttachmentCount,
    IReadOnlyList<ActivityMediaPreviewViewModel> MediaPreviews,
    bool HasPendingDelete,
    bool CanEdit,
    bool CanDelete)
{
    public bool HasPdf => PdfAttachmentCount > 0;
    public bool HasPhoto => PhotoAttachmentCount > 0;
    public bool HasVideo => VideoAttachmentCount > 0;
    public bool HasMedia => AttachmentCount > 0;
}

public sealed record ActivityMediaPreviewViewModel(
    int AttachmentId,
    string FileName,
    string ContentType,
    string MediaKind,
    string? ThumbnailUrl,
    string DownloadUrl,
    long SizeBytes);

public sealed record ActivityMonthGroupViewModel(
    string Label,
    IReadOnlyList<ActivityListRowViewModel> Items);

public sealed record ActivityReviewSummaryViewModel(
    int All,
    int WithMedia,
    int Photos,
    int Documents,
    int Videos);
