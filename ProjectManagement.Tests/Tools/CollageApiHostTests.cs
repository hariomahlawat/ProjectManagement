using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Utilities.Reporting;
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
/// The Collage Studio APIs through the real PRISM host: each refuses a request that is not
/// signed in, and library pictures are given the address PRISM Photos itself uses for each kind
/// of photo, so that kind's own page makes its own access check.
/// </summary>
public sealed class CollageApiHostTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string VisitId = "0f8fad5b-d9cb-469f-a165-70867728950e";
    private const string VisitPhotoId = "7c9e6679-7425-40de-944b-e07fc1f90ae7";

    private readonly WebApplicationFactory<Program> _factory;

    public CollageApiHostTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseCollageTestHost("collage-api"));
    }

    [Theory]
    [InlineData("/api/collage/stickers")]
    [InlineData("/api/collage/stickers/1/image?v=1")]
    [InlineData("/api/collage/library-photos?page=1")]
    public async Task An_anonymous_request_is_refused(string path)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("stickers", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Every_collage_route_requires_authorization()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/collage", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(
            new[] { CollageApi.LibraryPhotosPath, CollageApi.StickersPath, CollageApi.StickerPicturePath }.OrderBy(p => p, StringComparer.Ordinal),
            endpoints.Select(endpoint => endpoint.RoutePattern.RawText!).OrderBy(p => p, StringComparer.Ordinal));
        Assert.All(endpoints, endpoint =>
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()));
    }

    [Theory]
    [InlineData(MediaAssetOrigin.ProjectPhoto, "project-photo:41", "12", "project:12", "3",
        "/Projects/12/Photos/41/View/md?v=3", "/Projects/12/Photos/41/View/original?v=3")]
    [InlineData(MediaAssetOrigin.VisitPhoto, "visit-photo:" + VisitPhotoId, VisitId, "visit:" + VisitId, "a1b2",
        "/ProjectOfficeReports/Visits/ViewPhoto/" + VisitId + "/Photos/" + VisitPhotoId + "?size=md&v=a1b2",
        "/ProjectOfficeReports/Visits/ViewPhoto/" + VisitId + "/Photos/" + VisitPhotoId + "?size=original&v=a1b2")]
    [InlineData(MediaAssetOrigin.SocialMediaEventPhoto, "event-photo:" + VisitPhotoId, VisitId, "event:" + VisitId, "c3d4",
        "/ProjectOfficeReports/SocialMedia/ViewPhoto/" + VisitId + "/Photos/" + VisitPhotoId + "?size=feed&v=c3d4",
        "/ProjectOfficeReports/SocialMedia/ViewPhoto/" + VisitId + "/Photos/" + VisitPhotoId + "?size=original&v=c3d4")]
    [InlineData(MediaAssetOrigin.ActivityPhoto, "activity-photo:9", "5", "activity:5", "ABCD",
        "/Photos/Media/7/thumb?v=4", "/Photos/Media/7/original?v=4")]
    [InlineData(MediaAssetOrigin.ExternalFile, "file:x/y.jpg", "share-1", "folder:share-1", null,
        "/Photos/Media/7/thumb?v=4", "/Photos/Media/7/original?v=4")]
    public async Task Each_kind_of_photo_is_listed_at_its_own_page(
        MediaAssetOrigin origin, string sourceEntityId, string parentId, string contextKey, string? versionToken,
        string thumbUrl, string url)
    {
        var item = new MediaLibraryQueryItem(
            7, Guid.NewGuid(), origin, MediaAssetKind.Photo, MediaClassification.Photograph,
            sourceEntityId, parentId, contextKey, contextKey, "Context", string.Empty, "Source",
            "A photo", null, null, Guid.NewGuid(), "a.jpg", 1000, DateTimeOffset.UnixEpoch,
            1600, 1200, null, false, 1, 4, versionToken,
            Array.Empty<MediaLibraryPersonSummary>(), 0, Array.Empty<MediaLibraryAlbumSummary>());

        var photo = Assert.Single((await CallLibraryAsync(item)).GetProperty("photos").EnumerateArray());

        Assert.Equal(thumbUrl, photo.GetProperty("thumbUrl").GetString());
        Assert.Equal(url, photo.GetProperty("url").GetString());
    }

    private async Task<JsonElement> CallLibraryAsync(params MediaLibraryQueryItem[] items)
    {
        var options = Options.Create(new MediaLibraryOptions());
        options.Value.Enabled = true;
        options.Value.Catalogue.Enabled = true;
        var context = new DefaultHttpContext { RequestServices = _factory.Services };
        var links = _factory.Services.GetRequiredService<LinkGenerator>();
        var result = await CollageApi.LibraryPhotosAsync(null, null, null, 1, context, new StubLibrary(items), options, links, CancellationToken.None);
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        return JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement;
    }

    private sealed class StubLibrary(MediaLibraryQueryItem[] items) : IMediaLibraryQueryService
    {
        public Task<MediaLibraryQueryResult> SearchAsync(MediaLibraryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new MediaLibraryQueryResult(
                items,
                Array.Empty<MediaLibraryProjectOption>(),
                Array.Empty<MediaLibraryPersonOption>(),
                Array.Empty<int>(),
                new MediaLibraryStatistics(items.Length, items.Length, 0, 1),
                1, CollageApi.LibraryPageSize, false, true, true, true, true,
                Array.Empty<MediaLibraryQueryWarning>(), null));
    }
}

internal static class CollageTestHost
{
    /// <summary>
    /// The isolated PRISM test host, without the publication font warm-up: these tests make no
    /// reports, and QuestPDF's font registration is process-wide native code that other test hosts
    /// may be running at the same moment.
    /// </summary>
    public static IWebHostBuilder UseCollageTestHost(this IWebHostBuilder builder, string databaseNamePrefix) =>
        builder.UsePrismTestInfrastructure(databaseNamePrefix).ConfigureServices(services =>
        {
            foreach (var descriptor in services
                .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PublicationFontWarmupHostedService))
                .ToList())
            {
                services.Remove(descriptor);
            }
        });
}
