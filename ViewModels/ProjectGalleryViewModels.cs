using ProjectManagement.Models;

namespace ProjectManagement.ViewModels;

public sealed record ProjectPhotoGalleryViewModel(
    int ProjectId,
    string ProjectName,
    ProjectMediaPhotosTabViewModel Photos,
    int? SelectedTotId,
    string? SelectedTotLabel,
    bool CanManagePhotos);

public sealed record ProjectVideoGalleryViewModel(
    int ProjectId,
    string ProjectName,
    ProjectMediaVideosTabViewModel Videos,
    bool CanManageVideos);

public sealed record PdfPreviewViewModel(
    string Title,
    string SourceUrl,
    string FileName,
    string UploadedSummary,
    int ProjectId);
