using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.ViewModels;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectMediaAggregator
{
    private const string DefaultTotLabel = "Transfer of Technology";

    public ProjectMediaCollectionViewModel Build(ProjectMediaAggregationRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var photos = FilterPhotos(request.Photos, request.SelectedTotId);
        var photoTab = BuildPhotosTab(
            photos,
            request.CoverPhoto,
            request.CoverPhotoVersion,
            request.CoverPhotoUrl,
            request.CanManagePhotos);

        var videos = FilterVideos(request.Videos, request.SelectedTotId);
        var videoTab = videos.Count > 0
            ? new ProjectMediaVideosTabViewModel(videos, Math.Max(0, videos.Count - ProjectMediaSummaryViewModel.DefaultPreviewCount))
            : null;

        var documentsTab = BuildDocumentsTab(request);
        var hasDocuments = documentsTab.Groups.Count > 0 || request.DocumentList.TotalItems > 0;
        var hasPhotos = photoTab.LightboxPhotos.Count > 0 || request.CanManagePhotos;
        var hasVideos = videoTab is not null;

        var activeTab = NormalizeTabKey(request.ActiveTabKey, hasDocuments, hasPhotos, hasVideos);

        var tabs = new List<ProjectMediaTabViewModel>();

        tabs.Add(new ProjectMediaTabViewModel(
            ProjectMediaTabViewModel.DocumentsKey,
            "Documents",
            "bi-file-earmark-text",
            string.Equals(activeTab, ProjectMediaTabViewModel.DocumentsKey, StringComparison.Ordinal),
            request.DocumentPendingRequestCount,
            documentsTab,
            null,
            null,
            documentsTab.Groups.Any(g => g.Items.Any(i => i.ShowTotBadge))));

        tabs.Add(new ProjectMediaTabViewModel(
            ProjectMediaTabViewModel.PhotosKey,
            "Photos",
            "bi-images",
            string.Equals(activeTab, ProjectMediaTabViewModel.PhotosKey, StringComparison.Ordinal),
            0,
            null,
            photoTab,
            null,
            photoTab.LightboxPhotos.Any(p => p.TotId.HasValue)));

        if (videoTab is not null)
        {
            tabs.Add(new ProjectMediaTabViewModel(
                ProjectMediaTabViewModel.VideosKey,
                "Videos",
                "bi-play-circle",
                string.Equals(activeTab, ProjectMediaTabViewModel.VideosKey, StringComparison.Ordinal),
                0,
                null,
                null,
                videoTab,
                false));
        }

        var totFilters = BuildTotFilters(request.AvailableTotIds, request.SelectedTotId, request.TotFilterLabel);

        return new ProjectMediaCollectionViewModel(
            tabs,
            totFilters,
            request.SelectedTotId,
            activeTab);
    }

    private static IReadOnlyList<ProjectMediaFilterOptionViewModel> BuildTotFilters(
        IReadOnlyCollection<int> availableTotIds,
        int? selectedTotId,
        string? totFilterLabel)
    {
        var filters = new List<ProjectMediaFilterOptionViewModel>
        {
            new(null, "All media", !selectedTotId.HasValue)
        };

        if (availableTotIds.Count == 0)
        {
            return filters;
        }

        var distinct = availableTotIds
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        foreach (var totId in distinct)
        {
            filters.Add(new ProjectMediaFilterOptionViewModel(
                totId,
                string.IsNullOrWhiteSpace(totFilterLabel) ? DefaultTotLabel : totFilterLabel!,
                selectedTotId == totId));
        }

        return filters;
    }

    private static string NormalizeTabKey(string? requested, bool hasDocuments, bool hasPhotos, bool hasVideos)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (string.Equals(requested, ProjectMediaTabViewModel.PhotosKey, StringComparison.OrdinalIgnoreCase) && hasPhotos)
            {
                return ProjectMediaTabViewModel.PhotosKey;
            }

            if (string.Equals(requested, ProjectMediaTabViewModel.VideosKey, StringComparison.OrdinalIgnoreCase) && hasVideos)
            {
                return ProjectMediaTabViewModel.VideosKey;
            }
        }

        return hasDocuments ? ProjectMediaTabViewModel.DocumentsKey : (hasPhotos ? ProjectMediaTabViewModel.PhotosKey : ProjectMediaTabViewModel.VideosKey);
    }

    private static ProjectMediaDocumentsTabViewModel BuildDocumentsTab(ProjectMediaAggregationRequest request)
    {
        var groups = request.DocumentList.Groups
            .Select(g => new ProjectMediaDocumentGroupViewModel(
                g.StageCode,
                g.StageDisplayName,
                g.Items.Select(item => new ProjectMediaDocumentRowViewModel(item, item.TotId.HasValue)).ToList()))
            .Where(g => g.Items.Count > 0)
            .ToList();

        var displayedCount = groups.Sum(g => g.Items.Count);

        return new ProjectMediaDocumentsTabViewModel(
            request.DocumentList,
            groups,
            request.DocumentSummary,
            request.DocumentPendingRequests,
            request.IsDocumentApprover,
            request.CanUploadDocuments,
            request.CanViewRecycleBin,
            request.DocumentPendingRequestCount,
            displayedCount,
            request.DocumentList.StatusHeading);
    }

    private static IReadOnlyList<ProjectPhoto> FilterPhotos(IReadOnlyList<ProjectPhoto> photos, int? totId)
    {
        if (!totId.HasValue)
        {
            return photos;
        }

        return photos
            .Where(p => p.TotId.HasValue && p.TotId.Value == totId.Value)
            .ToList();
    }

    private static ProjectMediaPhotosTabViewModel BuildPhotosTab(
        IReadOnlyList<ProjectPhoto> photos,
        ProjectPhoto? coverPhoto,
        int? coverPhotoVersion,
        string? coverPhotoUrl,
        bool canManagePhotos)
    {
        var orderedPhotos = photos
            .OrderBy(p => p.Ordinal)
            .ThenBy(p => p.Id)
            .ToList();

        var previewSource = orderedPhotos
            .Where(p => coverPhoto is null || p.Id != coverPhoto.Id)
            .ToList();

        var previewTiles = previewSource
            .Take(ProjectMediaSummaryViewModel.DefaultPreviewCount)
            .Select(p => new ProjectMediaPhotoTileViewModel(p, p.TotId.HasValue))
            .ToList();

        var remaining = Math.Max(0, previewSource.Count - previewTiles.Count);

        if (previewTiles.Count == 0 && orderedPhotos.Count > 0 && coverPhoto is not null && orderedPhotos.Any(p => p.Id == coverPhoto.Id))
        {
            previewTiles.Add(new ProjectMediaPhotoTileViewModel(coverPhoto, coverPhoto.TotId.HasValue));
        }

        return new ProjectMediaPhotosTabViewModel(
            previewTiles,
            remaining,
            canManagePhotos,
            orderedPhotos,
            coverPhoto,
            coverPhotoVersion,
            coverPhotoUrl);
    }

    private static List<ProjectMediaVideoViewModel> FilterVideos(
        IReadOnlyList<ProjectMediaVideoViewModel> videos,
        int? _)
    {
        return videos.ToList();
    }
}

public sealed record ProjectMediaAggregationRequest(
    ProjectDocumentListViewModel DocumentList,
    ProjectDocumentSummaryViewModel DocumentSummary,
    IReadOnlyList<ProjectDocumentPendingRequestViewModel> DocumentPendingRequests,
    bool IsDocumentApprover,
    bool CanUploadDocuments,
    bool CanViewRecycleBin,
    int DocumentPendingRequestCount,
    IReadOnlyList<ProjectPhoto> Photos,
    ProjectPhoto? CoverPhoto,
    int? CoverPhotoVersion,
    string? CoverPhotoUrl,
    bool CanManagePhotos,
    IReadOnlyList<ProjectMediaVideoViewModel> Videos,
    IReadOnlyCollection<int> AvailableTotIds,
    int? SelectedTotId,
    string? ActiveTabKey,
    string? TotFilterLabel);
