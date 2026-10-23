using System;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using ProjectManagement.Models;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Projects;

public sealed class ProjectMediaViewerItemsTests
{
    private static SimpleUrlHelper Url()
        => new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()));

    private static ProjectPhoto Photo(int id, int ordinal, string? caption = null) => new()
    {
        Id = id,
        ProjectId = 9,
        Ordinal = ordinal,
        Caption = caption,
        Version = 3,
        CreatedUtc = new DateTime(2026, 3, 1, 20, 0, 0, DateTimeKind.Utc), // 2 Mar in IST
    };

    [Fact]
    public void Photos_AreInGalleryOrder_CoverFirst()
    {
        var photos = new[] { Photo(1, 2), Photo(2, 1), Photo(3, 3) };

        var ordered = ProjectMediaViewerItems.InGalleryOrder(photos, coverPhotoId: 3);

        Assert.Equal(new[] { 3, 2, 1 }, ordered.Select(photo => photo.Id));
        Assert.Equal(new[] { 2, 1, 3 }, ProjectMediaViewerItems.InGalleryOrder(photos, null).Select(photo => photo.Id));
    }

    [Fact]
    public void Photo_ShowsTheLargeSize_DownloadsAJpeg_AndSaysWhenItWasAdded()
    {
        var item = ProjectMediaViewerItems.ForPhoto(Url(), 9, "Driving Simulator", Photo(5, 1, "  Field trials  "));

        Assert.Equal("photo-5", item.Key);
        Assert.Equal("photo", item.Kind);
        Assert.Contains("size=xl", item.DisplayUrl, StringComparison.Ordinal);
        Assert.Contains("v=3", item.DisplayUrl, StringComparison.Ordinal);
        Assert.Contains("/Projects/Photos/Download", item.DownloadUrl, StringComparison.Ordinal);
        Assert.Contains("format=jpg", item.DownloadUrl, StringComparison.Ordinal);
        Assert.Equal("Field trials", item.Title);
        Assert.Equal("Driving Simulator", item.Context);
        Assert.Equal("Added 2 Mar 2026", item.Date);

        var untitled = ProjectMediaViewerItems.ForPhoto(Url(), 9, "Driving Simulator", Photo(6, 2));
        Assert.Equal("Photo", untitled.Title);
        Assert.Equal("Driving Simulator photo", untitled.Alt);
    }

    [Fact]
    public void Video_StreamsWithItsPoster_AndFallsBackToTheFileName()
    {
        var video = new ProjectVideo { Id = 7, ProjectId = 9, OriginalFileName = "demo-run.mp4", Version = 2, PosterStorageKey = "posters/7.jpg" };

        var item = ProjectMediaViewerItems.ForVideo(Url(), 9, "Driving Simulator", video);

        Assert.Equal("video-7", item.Key);
        Assert.Equal("video", item.Kind);
        Assert.Contains("/Projects/Videos/Stream", item.DisplayUrl, StringComparison.Ordinal);
        Assert.Contains("/Projects/Videos/Poster", item.Poster, StringComparison.Ordinal);
        Assert.Equal("demo-run", item.Title);
        Assert.Null(item.DownloadUrl);

        video.PosterStorageKey = null;
        Assert.Null(ProjectMediaViewerItems.ForVideo(Url(), 9, "Driving Simulator", video).Poster);
    }

    [Fact]
    public void ItemsJson_IsCamelCase_AndACaptionCannotCloseTheScriptElement()
    {
        var item = ProjectMediaViewerItems.ForPhoto(Url(), 9, "Driving Simulator", Photo(5, 1, "</script><script>alert(1)</script>"));

        var json = new MediaViewerItemsGroup("project-photos", new[] { item }).ToJson();

        Assert.DoesNotContain("</script>", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", json, StringComparison.Ordinal);
        using var parsed = JsonDocument.Parse(json);
        var first = parsed.RootElement[0];
        Assert.Equal("photo-5", first.GetProperty("key").GetString());
        Assert.Equal("</script><script>alert(1)</script>", first.GetProperty("title").GetString());
        Assert.True(first.TryGetProperty("displayUrl", out _));
    }
}
