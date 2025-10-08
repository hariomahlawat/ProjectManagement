using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectMediaAggregatorTests
{
    [Fact]
    public void Build_PreservesDocumentAndPhotoOrdering()
    {
        // Arrange
        var documentRows = new List<ProjectDocumentRowViewModel>
        {
            CreateDocumentRow(1, "Alpha", totId: null),
            CreateDocumentRow(2, "Bravo", totId: 5)
        };

        var documentList = new ProjectDocumentListViewModel(
            new List<ProjectDocumentStageGroupViewModel>
            {
                new("plan", "Planning", documentRows)
            },
            new List<ProjectDocumentFilterOptionViewModel> { new(null, "All stages", true) },
            new List<ProjectDocumentFilterOptionViewModel>
            {
                new(ProjectDocumentListViewModel.PublishedStatusValue, "Published", true)
            },
            null,
            ProjectDocumentListViewModel.PublishedStatusValue,
            1,
            ProjectDocumentListViewModel.DefaultPageSize,
            documentRows.Count);

        var photos = new List<ProjectPhoto>
        {
            CreatePhoto(3, ordinal: 2, totId: null, caption: "Second"),
            CreatePhoto(4, ordinal: 1, totId: 5, caption: "First"),
            CreatePhoto(5, ordinal: 3, totId: null, caption: "Third")
        };

        var request = CreateRequest(
            documentList,
            documentRows.Count,
            photos,
            availableTotIds: new[] { 5 },
            selectedTotId: null);

        var aggregator = new ProjectMediaAggregator();

        // Act
        var result = aggregator.Build(request);

        // Assert
        var documentTab = result.Tabs.Single(t => t.Key == ProjectMediaTabViewModel.DocumentsKey).Documents!;
        Assert.Equal(new int?[] { 1, 2 }, documentTab.Groups.Single().Items.Select(i => i.Row.DocumentId));

        var photoTab = result.Tabs.Single(t => t.Key == ProjectMediaTabViewModel.PhotosKey).Photos!;
        Assert.Equal(new[] { 4, 3, 5 }, photoTab.PreviewTiles.Select(t => t.Photo.Id));
    }

    [Fact]
    public void Build_FiltersPhotosByTotSelection()
    {
        // Arrange
        var documentRows = new List<ProjectDocumentRowViewModel>
        {
            CreateDocumentRow(10, "General", totId: null),
            CreateDocumentRow(11, "Transfer", totId: 5)
        };

        var documentList = new ProjectDocumentListViewModel(
            new List<ProjectDocumentStageGroupViewModel>
            {
                new("exe", "Execution", documentRows)
            },
            new List<ProjectDocumentFilterOptionViewModel> { new(null, "All stages", true) },
            new List<ProjectDocumentFilterOptionViewModel>
            {
                new(ProjectDocumentListViewModel.PublishedStatusValue, "Published", true)
            },
            null,
            ProjectDocumentListViewModel.PublishedStatusValue,
            1,
            ProjectDocumentListViewModel.DefaultPageSize,
            documentRows.Count);

        var photos = new List<ProjectPhoto>
        {
            CreatePhoto(20, ordinal: 1, totId: null, caption: "General"),
            CreatePhoto(21, ordinal: 2, totId: 5, caption: "ToT")
        };

        var request = CreateRequest(
            documentList,
            documentRows.Count,
            photos,
            availableTotIds: new[] { 5 },
            selectedTotId: 5);

        var aggregator = new ProjectMediaAggregator();

        // Act
        var result = aggregator.Build(request);

        // Assert
        var photoTab = result.Tabs.Single(t => t.Key == ProjectMediaTabViewModel.PhotosKey).Photos!;
        Assert.Single(photoTab.PreviewTiles);
        Assert.Equal(21, photoTab.PreviewTiles[0].Photo.Id);
    }

    [Fact]
    public void Build_SetsTotBadgesWhenApplicable()
    {
        // Arrange
        var documentRows = new List<ProjectDocumentRowViewModel>
        {
            CreateDocumentRow(30, "General", totId: null),
            CreateDocumentRow(31, "Transfer", totId: 5)
        };

        var documentList = new ProjectDocumentListViewModel(
            new List<ProjectDocumentStageGroupViewModel>
            {
                new("exe", "Execution", documentRows)
            },
            new List<ProjectDocumentFilterOptionViewModel> { new(null, "All stages", true) },
            new List<ProjectDocumentFilterOptionViewModel>
            {
                new(ProjectDocumentListViewModel.PublishedStatusValue, "Published", true)
            },
            null,
            ProjectDocumentListViewModel.PublishedStatusValue,
            1,
            ProjectDocumentListViewModel.DefaultPageSize,
            documentRows.Count);

        var photos = new List<ProjectPhoto>
        {
            CreatePhoto(40, ordinal: 1, totId: null, caption: "General"),
            CreatePhoto(41, ordinal: 2, totId: 5, caption: "ToT")
        };

        var request = CreateRequest(
            documentList,
            documentRows.Count,
            photos,
            availableTotIds: new[] { 5 },
            selectedTotId: null);

        var aggregator = new ProjectMediaAggregator();

        // Act
        var result = aggregator.Build(request);

        // Assert
        var documentTab = result.Tabs.Single(t => t.Key == ProjectMediaTabViewModel.DocumentsKey).Documents!;
        Assert.False(documentTab.Groups.Single().Items[0].ShowTotBadge);
        Assert.True(documentTab.Groups.Single().Items[1].ShowTotBadge);
        Assert.True(result.Tabs.Single(t => t.Key == ProjectMediaTabViewModel.DocumentsKey).HasTotItems);

        var photoTab = result.Tabs.Single(t => t.Key == ProjectMediaTabViewModel.PhotosKey).Photos!;
        Assert.False(photoTab.PreviewTiles[0].ShowTotBadge);
        Assert.True(photoTab.PreviewTiles[1].ShowTotBadge);
        Assert.True(result.Tabs.Single(t => t.Key == ProjectMediaTabViewModel.PhotosKey).HasTotItems);
    }

    private static ProjectMediaAggregationRequest CreateRequest(
        ProjectDocumentListViewModel documentList,
        int documentCount,
        IReadOnlyList<ProjectPhoto> photos,
        IReadOnlyCollection<int> availableTotIds,
        int? selectedTotId)
    {
        return new ProjectMediaAggregationRequest(
            documentList,
            new ProjectDocumentSummaryViewModel
            {
                TotalCount = documentCount,
                PublishedCount = documentCount,
                PendingCount = 0
            },
            new List<ProjectDocumentPendingRequestViewModel>(),
            IsDocumentApprover: false,
            CanUploadDocuments: true,
            CanViewRecycleBin: true,
            DocumentPendingRequestCount: 0,
            Photos: photos,
            CoverPhoto: null,
            CoverPhotoVersion: null,
            CoverPhotoUrl: null,
            CanManagePhotos: true,
            Videos: new List<ProjectMediaVideoViewModel>(),
            AvailableTotIds: availableTotIds,
            SelectedTotId: selectedTotId,
            ActiveTabKey: ProjectMediaTabViewModel.DocumentsKey,
            TotFilterLabel: "Transfer of Technology");
    }

    private static ProjectDocumentRowViewModel CreateDocumentRow(int id, string title, int? totId)
    {
        return new ProjectDocumentRowViewModel(
            StageCode: "exe",
            StageDisplayName: "Execution",
            DocumentId: id,
            RequestId: null,
            Title: title,
            FileName: $"{title}.pdf",
            FileSizeDisplay: "10 KB",
            MetadataSummary: "Uploaded",
            StatusLabel: "Published",
            StatusVariant: "success",
            IsPending: false,
            IsRemoved: false,
            PreviewUrl: $"/preview/{id}",
            SecondarySummary: null,
            PendingRequestType: null,
            TotId: totId,
            IsTotLinked: totId.HasValue);
    }

    private static ProjectPhoto CreatePhoto(int id, int ordinal, int? totId, string caption)
    {
        return new ProjectPhoto
        {
            Id = id,
            ProjectId = 1,
            StorageKey = $"photo-{id}",
            OriginalFileName = $"photo-{id}.jpg",
            ContentType = "image/jpeg",
            Ordinal = ordinal,
            Caption = caption,
            TotId = totId,
            Version = 1,
            CreatedUtc = System.DateTime.UtcNow,
            UpdatedUtc = System.DateTime.UtcNow
        };
    }
}
