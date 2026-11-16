using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.Collage;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Tests.Infrastructure;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// Collage Studio's "From PRISM Photos" list: photos only, through the library's own search (so
/// its visibility rule decides), paged, and nothing at all when the library is off or unreadable.
/// </summary>
public sealed class CollageLibraryPhotosTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CollageLibraryPhotosTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseCollageTestHost("collage-library"));
    }

    [Fact]
    public async Task It_asks_the_library_for_photos_only_and_lists_their_pictures()
    {
        var library = new StubLibrary(Result(Item(7, MediaAssetKind.Photo, "Farewell 2026"), Item(8, MediaAssetKind.Video, "Clip")));
        var json = await CallAsync(library, q: "  farewell ", projectId: 12, year: 2026, page: 1);

        var asked = Assert.Single(library.Queries);
        Assert.Equal(("farewell", "photo", "all", 12, 2026, 1, CollageApi.LibraryPageSize, "newest"),
            (asked.Query, asked.Kind, asked.Source, asked.ProjectId, asked.Year, asked.PageNumber, asked.PageSize, asked.Sort));
        Assert.True(asked.IncludeFilterFacets);
        // Its Projects filter offers only projects that have photos.
        Assert.True(asked.ProjectsOfKindOnly);

        Assert.True(json.GetProperty("available").GetBoolean());
        var photo = Assert.Single(json.GetProperty("photos").EnumerateArray());
        Assert.Equal(7, photo.GetProperty("id").GetInt64());
        Assert.Equal("Farewell 2026", photo.GetProperty("title").GetString());
        // A project photo is read through the project's own photo page and its access check.
        Assert.Equal("/Projects/12/Photos/7/View/md?v=v3", photo.GetProperty("thumbUrl").GetString());
        // Its largest size: the project photo page serves xs to xl and no "original".
        Assert.Equal("/Projects/12/Photos/7/View/xl?v=v3", photo.GetProperty("url").GetString());
        Assert.Equal("Project A", json.GetProperty("projects")[0].GetProperty("name").GetString());
        Assert.True(json.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Later_pages_skip_the_lists_and_odd_values_are_ignored()
    {
        var library = new StubLibrary(Result());
        var json = await CallAsync(library, q: "   ", projectId: -4, year: 12, page: 3);

        var asked = Assert.Single(library.Queries);
        Assert.Null(asked.Query);
        Assert.Null(asked.ProjectId);
        Assert.Null(asked.Year);
        Assert.Equal(3, asked.PageNumber);
        Assert.False(asked.IncludeFilterFacets);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("projects").ValueKind);
    }

    [Fact]
    public async Task When_the_library_is_off_or_unreadable_nothing_is_listed_and_it_says_so()
    {
        var off = await CallAsync(new StubLibrary(Result()), enabled: false);
        Assert.False(off.GetProperty("available").GetBoolean());
        Assert.Equal(CollageApi.LibraryOffMessage, off.GetProperty("message").GetString());

        var unreadable = await CallAsync(new StubLibrary(MediaLibraryQueryResult.Unavailable(1, 48, "down")));
        Assert.False(unreadable.GetProperty("available").GetBoolean());
        Assert.Equal(CollageApi.LibraryUnavailableMessage, unreadable.GetProperty("message").GetString());
        Assert.Empty(unreadable.GetProperty("photos").EnumerateArray());
    }

    private async Task<JsonElement> CallAsync(StubLibrary library, string? q = null, int? projectId = null, int? year = null, int? page = null, bool enabled = true)
    {
        var options = Options.Create(new MediaLibraryOptions());
        options.Value.Enabled = enabled;
        options.Value.Catalogue.Enabled = enabled;
        var context = new DefaultHttpContext { RequestServices = _factory.Services };
        var links = _factory.Services.GetRequiredService<LinkGenerator>();
        var result = await CollageApi.LibraryPhotosAsync(q, projectId, year, page, context, library, options, links, CancellationToken.None);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        return JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement;
    }

    private static MediaLibraryQueryResult Result(params MediaLibraryQueryItem[] items) =>
        new(items,
            new[] { new MediaLibraryProjectOption(12, "Project A") },
            Array.Empty<MediaLibraryPersonOption>(),
            new[] { 2026 },
            new MediaLibraryStatistics(items.Length, items.Length, 0, 1),
            1, CollageApi.LibraryPageSize, false, true, true, true, true,
            Array.Empty<MediaLibraryQueryWarning>(), null);

    private static MediaLibraryQueryItem Item(long id, MediaAssetKind kind, string title) =>
        new(id, Guid.NewGuid(), MediaAssetOrigin.ProjectPhoto, kind, MediaClassification.Photograph,
            $"project-photo:{id}", "12", "project:12", "project:12", "Project A", string.Empty, "Projects",
            title, null, null, Guid.NewGuid(), $"{title}.jpg", 1000, DateTimeOffset.UnixEpoch,
            1600, 1200, null, false, id, 3, "v3",
            Array.Empty<MediaLibraryPersonSummary>(), 0, Array.Empty<MediaLibraryAlbumSummary>());

    private sealed class StubLibrary(MediaLibraryQueryResult result) : IMediaLibraryQueryService
    {
        public List<MediaLibraryQuery> Queries { get; } = new();

        public Task<MediaLibraryQueryResult> SearchAsync(MediaLibraryQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(result);
        }
    }
}
