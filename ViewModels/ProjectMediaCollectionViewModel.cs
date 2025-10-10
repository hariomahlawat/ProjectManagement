using System;
using System.Collections.Generic;
using ProjectManagement.Models;

namespace ProjectManagement.ViewModels;

public sealed record ProjectMediaCollectionViewModel(
    IReadOnlyList<ProjectMediaTabViewModel> Tabs,
    IReadOnlyList<ProjectMediaFilterOptionViewModel> TotFilters,
    int? SelectedTotId,
    string ActiveTabKey)
{
    public static ProjectMediaCollectionViewModel Empty { get; } = new(
        Array.Empty<ProjectMediaTabViewModel>(),
        Array.Empty<ProjectMediaFilterOptionViewModel>(),
        null,
        ProjectMediaTabViewModel.DocumentsKey);
}

public sealed record ProjectMediaFilterOptionViewModel(int? Value, string Label, bool Selected);

public sealed record ProjectMediaTabViewModel(
    string Key,
    string Title,
    string Icon,
    bool IsActive,
    int BadgeCount,
    ProjectMediaDocumentsTabViewModel? Documents,
    ProjectMediaPhotosTabViewModel? Photos,
    ProjectMediaVideosTabViewModel? Videos,
    bool HasTotItems)
{
    public const string DocumentsKey = "documents";
    public const string PhotosKey = "photos";
    public const string VideosKey = "videos";
}

public sealed record ProjectMediaDocumentsTabViewModel(
    ProjectDocumentListViewModel List,
    IReadOnlyList<ProjectMediaDocumentGroupViewModel> Groups,
    ProjectDocumentSummaryViewModel Summary,
    IReadOnlyList<ProjectDocumentPendingRequestViewModel> PendingRequests,
    bool IsApprover,
    bool CanUpload,
    bool CanViewRecycleBin,
    int PendingCount,
    int DisplayedCount,
    string StatusHeading);

public sealed record ProjectMediaDocumentGroupViewModel(
    string? StageCode,
    string StageDisplayName,
    IReadOnlyList<ProjectMediaDocumentRowViewModel> Items);

public sealed record ProjectMediaDocumentRowViewModel(ProjectDocumentRowViewModel Row, bool ShowTotBadge);

public sealed record ProjectMediaPhotosTabViewModel(
    IReadOnlyList<ProjectMediaPhotoTileViewModel> PreviewTiles,
    int RemainingCount,
    bool CanManagePhotos,
    IReadOnlyList<ProjectPhoto> LightboxPhotos,
    ProjectPhoto? CoverPhoto,
    int? CoverPhotoVersion,
    string? CoverPhotoUrl);

public sealed record ProjectMediaPhotoTileViewModel(ProjectPhoto Photo, bool ShowTotBadge);

public sealed record ProjectMediaVideosTabViewModel(
    IReadOnlyList<ProjectMediaVideoViewModel> Items,
    int RemainingCount);

public sealed record ProjectMediaVideoViewModel(
    int Id,
    string Title,
    string PlaybackUrl,
    string ThumbnailUrl,
    TimeSpan? Duration);
