using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.Collage;

/// <summary>
/// What Collage Studio reads from PRISM, for signed-in users only and by GET only: the "Crests
/// and logos" stickers and their pictures, and photos from the PRISM Photos library. Nothing a
/// user makes is ever sent here.
/// </summary>
public static class CollageApi
{
    public const string StickersPath = "/api/collage/stickers";
    public const string StickerPicturePath = "/api/collage/stickers/{id:int}/image";
    public const string LibraryPhotosPath = "/api/collage/library-photos";

    /// <summary>Photos per page of the PRISM Photos picker.</summary>
    public const int LibraryPageSize = 48;

    public const string LibraryOffMessage = "PRISM Photos is not switched on. Add photos from your computer.";
    public const string LibraryUnavailableMessage = "PRISM Photos cannot be read right now. Try again in a minute, or add photos from your computer.";

    /// <summary>
    /// One library photo's thumbnail and original, at the same addresses PRISM Photos uses for its
    /// kind: project, visit and social-media photos through their own pages (and their own access
    /// checks), everything else through /Photos/Media.
    /// </summary>
    public static MediaLibraryItemAddresses LibraryPictureAddresses(MediaLibraryQueryItem item, HttpContext httpContext, LinkGenerator links)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(links);
        return MediaLibraryItemAddressResolver.Resolve(
            item,
            (page, values) => links.GetPathByPage(httpContext, page, handler: null, values: values),
            path => path.StartsWith("~/", StringComparison.Ordinal) ? httpContext.Request.PathBase + path[1..] : path);
    }

    /// <summary>The address of one sticker's picture; the version makes a changed picture a new address.</summary>
    public static string PictureUrl(int id, int version) =>
        string.Create(CultureInfo.InvariantCulture, $"/api/collage/stickers/{id}/image?v={version}");

    public static IServiceCollection AddCollage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ICollageStickerService, CollageStickerService>();
        return services;
    }

    public static IEndpointRouteBuilder MapCollageApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet(StickersPath, StickersAsync).RequireAuthorization();
        endpoints.MapGet(StickerPicturePath, PictureAsync).RequireAuthorization();
        endpoints.MapGet(LibraryPhotosPath, LibraryPhotosAsync).RequireAuthorization();
        return endpoints;
    }

    internal static async Task<IResult> StickersAsync(
        HttpContext httpContext,
        ICollageStickerService stickers,
        CancellationToken cancellationToken)
    {
        // The list changes when an administrator changes it, so it is always read afresh.
        httpContext.Response.Headers[HeaderNames.CacheControl] = "no-store";
        var active = await stickers.ListActiveAsync(cancellationToken);
        return Results.Ok(new
        {
            stickers = active.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                words = s.SearchWords ?? string.Empty,
                width = s.Width,
                height = s.Height,
                url = PictureUrl(s.Id, s.Version)
            })
        });
    }

    internal static async Task<IResult> PictureAsync(
        int id,
        HttpContext httpContext,
        ICollageStickerService stickers,
        CancellationToken cancellationToken)
    {
        // A retired sticker is still served, so collages that use it keep their crest.
        var sticker = await stickers.FindAsync(id, cancellationToken);
        var path = sticker is null ? null : stickers.PicturePathOf(sticker);
        if (sticker is null || path is null) return Results.NotFound();

        var etag = new EntityTagHeaderValue(string.Create(CultureInfo.InvariantCulture, $"\"collage-sticker-{sticker.Id}-{sticker.Version}\""));
        var headers = httpContext.Response.Headers;
        headers[HeaderNames.CacheControl] = "private, max-age=604800";
        headers[HeaderNames.ETag] = etag.ToString();
        headers[HeaderNames.XContentTypeOptions] = "nosniff";

        var ifNoneMatch = httpContext.Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch is { Count: > 0 } && ifNoneMatch.Any(tag => tag.Compare(etag, useStrongComparison: false)))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.File(path, "image/png");
    }

    /// <summary>
    /// Photos from the PRISM Photos library for the collage picker: photos only, newest first,
    /// one page at a time, through the library's own search, so its visibility rule decides what
    /// is listed (and each kind's own picture page decides again for each picture, see
    /// <see cref="MediaLibraryItemAddressResolver"/>). The first page also carries
    /// the projects and years to narrow the list by. When the library is off or cannot be read,
    /// the answer says so and lists nothing.
    /// </summary>
    internal static async Task<IResult> LibraryPhotosAsync(
        string? q,
        int? projectId,
        int? year,
        int? page,
        HttpContext httpContext,
        IMediaLibraryQueryService library,
        IOptions<MediaLibraryOptions> options,
        LinkGenerator links,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers[HeaderNames.CacheControl] = "no-store";
        if (!options.Value.IsCatalogueEnabled)
        {
            return Results.Ok(new { available = false, message = LibraryOffMessage, photos = Array.Empty<object>(), page = 1, hasMore = false });
        }

        var pageNumber = Math.Clamp(page ?? 1, 1, 10_000);
        var words = string.IsNullOrWhiteSpace(q) ? null : q.Trim()[..Math.Min(q.Trim().Length, 200)];
        var first = pageNumber == 1;
        var result = await library.SearchAsync(
            new MediaLibraryQuery(
                words,
                Source: "all",
                Kind: "photo",
                Classification: "all",
                ProjectId: projectId is > 0 ? projectId : null,
                PersonId: null,
                Year: year is >= 1900 and <= 9999 ? year : null,
                PageNumber: pageNumber,
                PageSize: LibraryPageSize,
                IncludePeople: false,
                Sort: "newest",
                IncludeFilterFacets: first),
            cancellationToken);

        if (!result.IsAvailable || !result.IsPrimaryQuerySuccessful)
        {
            return Results.Ok(new { available = false, message = LibraryUnavailableMessage, photos = Array.Empty<object>(), page = pageNumber, hasMore = false });
        }

        return Results.Ok(new
        {
            available = true,
            photos = result.Items
                .Where(item => item.Kind == ProjectManagement.Features.MediaLibrary.Domain.MediaAssetKind.Photo)
                .Select(item => (Item: item, Addresses: LibraryPictureAddresses(item, httpContext, links)))
                .Select(row => new
                {
                    id = row.Item.Id,
                    title = string.IsNullOrWhiteSpace(row.Item.Title) ? row.Item.OriginalFileName : row.Item.Title,
                    context = row.Item.ContextTitle,
                    date = row.Item.MediaDateUtc,
                    width = row.Item.Width,
                    height = row.Item.Height,
                    thumbUrl = row.Addresses.Thumbnail,
                    url = row.Addresses.Original
                }),
            page = result.PageNumber,
            hasMore = result.HasNextPage,
            projects = first ? result.Projects.Select(p => new { id = p.Id, name = p.Name }) : null,
            years = first ? result.Years : null
        });
    }
}
