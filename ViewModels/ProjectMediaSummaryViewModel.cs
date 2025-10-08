using System;
using System.Collections.Generic;
using ProjectManagement.Models;

namespace ProjectManagement.ViewModels;

public sealed class ProjectMediaSummaryViewModel
{
    public static readonly ProjectMediaSummaryViewModel Empty = new();
    public const int DefaultPreviewCount = 4;

    public int PhotoCount { get; init; }
    public int AdditionalPhotoCount { get; init; }
    public int RemainingPhotoCount { get; init; }
    public ProjectPhoto? CoverPhoto { get; init; }
    public int? CoverPhotoVersion { get; init; }
    public string? CoverPhotoUrl { get; init; }
    public IReadOnlyList<ProjectPhoto> PreviewPhotos { get; init; } = Array.Empty<ProjectPhoto>();
    public int DocumentCount { get; init; }
    public int PendingDocumentCount { get; init; }

    public bool HasPhotos => PhotoCount > 0;
    public bool HasAdditionalPhotos => AdditionalPhotoCount > 0;
    public bool HasDocuments => DocumentCount > 0;
    public bool HasPendingDocuments => PendingDocumentCount > 0;
}
