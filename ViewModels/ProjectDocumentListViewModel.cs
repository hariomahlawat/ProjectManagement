using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;

namespace ProjectManagement.ViewModels;

public sealed record ProjectDocumentListViewModel(
    IReadOnlyList<ProjectDocumentStageGroupViewModel> Groups,
    IReadOnlyList<ProjectDocumentFilterOptionViewModel> StageFilters,
    IReadOnlyList<ProjectDocumentFilterOptionViewModel> StatusFilters,
    string? SelectedStageValue,
    string SelectedStatusValue,
    int Page,
    int PageSize,
    int TotalItems)
{
    public static ProjectDocumentListViewModel Empty { get; } = new(
        Array.Empty<ProjectDocumentStageGroupViewModel>(),
        Array.Empty<ProjectDocumentFilterOptionViewModel>(),
        Array.Empty<ProjectDocumentFilterOptionViewModel>(),
        null,
        PublishedStatusValue,
        1,
        DefaultPageSize,
        0);

    public const string PublishedStatusValue = "published";
    public const string PendingStatusValue = "pending";
    public const string UnassignedStageValue = "__none__";
    public const int DefaultPageSize = 10;

    public int TotalPages => TotalItems == 0
        ? 1
        : (int)Math.Ceiling(TotalItems / (double)Math.Max(PageSize, 1));

    public bool HasItems => Groups.Count > 0;

    public string SelectedStageLabel => StageFilters.FirstOrDefault(f => f.Selected)?.Label ?? "All stages";

    public bool ShowStageFilter => StageFilters.Count > 0;

    public bool ShowStatusFilter => StatusFilters.Count > 1;

    public bool ShowPagination => TotalPages > 1;

    public string StatusHeading => string.Equals(SelectedStatusValue, PendingStatusValue, StringComparison.OrdinalIgnoreCase)
        ? "requests"
        : "documents";
}

public sealed record ProjectDocumentStageGroupViewModel(
    string? StageCode,
    string StageDisplayName,
    IReadOnlyList<ProjectDocumentRowViewModel> Items);

public sealed record ProjectDocumentRowViewModel(
    string? StageCode,
    string StageDisplayName,
    int? DocumentId,
    int? RequestId,
    string Title,
    string? FileName,
    string FileSizeDisplay,
    string MetadataSummary,
    string StatusLabel,
    string StatusVariant,
    bool IsPending,
    bool IsRemoved,
    string? PreviewUrl,
    string? SecondarySummary,
    ProjectDocumentRequestType? PendingRequestType,
    int? TotId,
    bool IsTotLinked,
    ProjectDocumentOcrStatus? OcrStatus,
    string? OcrFailureReason);

public sealed record ProjectDocumentFilterOptionViewModel(string? Value, string Label, bool Selected);

public sealed record ProjectDocumentPendingRequestViewModel(
    int RequestId,
    string Title,
    string StageDisplayName,
    string RequestTypeLabel,
    string RequestedSummary,
    string FileName,
    string FileSizeDisplay,
    string RowVersion,
    string ReviewUrl,
    DateTimeOffset RequestedAtUtc,
    string RequestedBy);
