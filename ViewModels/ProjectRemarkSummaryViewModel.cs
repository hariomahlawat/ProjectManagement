using System;
using ProjectManagement.Models.Remarks;

namespace ProjectManagement.ViewModels;

public sealed class ProjectRemarkSummaryViewModel
{
    public static readonly ProjectRemarkSummaryViewModel Empty = new();

    public int InternalCount { get; init; }
    public int ExternalCount { get; init; }
    public int TotalCount => InternalCount + ExternalCount;
    public bool HasRemarks => TotalCount > 0;
    public DateTime? LastActivityUtc { get; init; }
    public int? LastRemarkId { get; init; }
    public RemarkType? LastRemarkType { get; init; }
    public RemarkActorRole? LastRemarkActorRole { get; init; }
    public string? LastRemarkPreview { get; init; }
}
