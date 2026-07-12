using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class MediaCachePathResolver : IMediaCachePathResolver
{
    public MediaCachePathResolver(IWebHostEnvironment environment, IOptions<MediaLibraryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        var configured = options.Value.CacheRoot;
        CacheRoot = Path.IsPathFullyQualified(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));

    }

    public string CacheRoot { get; }

    public string GetThumbnailPath(long assetId, int cacheVersion)
        => BuildPath(assetId, cacheVersion, "thumb");

    public string GetPreviewPath(long assetId, int cacheVersion)
        => BuildPath(assetId, cacheVersion, "preview");

    private string BuildPath(long assetId, int cacheVersion, string variant)
    {
        var shard = (assetId % 1000).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
        var directory = Path.Combine(CacheRoot, shard, assetId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Path.Combine(directory, $"v{cacheVersion}-{variant}.webp");
    }
}
