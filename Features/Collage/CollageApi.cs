using System.Globalization;
using Microsoft.Net.Http.Headers;

namespace ProjectManagement.Features.Collage;

/// <summary>
/// What Collage Studio reads from PRISM, for signed-in users only and by GET only: the "Crests
/// and logos" stickers and their pictures. Nothing a user makes is ever sent here.
/// </summary>
public static class CollageApi
{
    public const string StickersPath = "/api/collage/stickers";
    public const string StickerPicturePath = "/api/collage/stickers/{id:int}/image";

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
}
