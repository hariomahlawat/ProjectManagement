using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class MediaDerivativeService : IMediaDerivativeService
{
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> AssetLocks = new();

    private readonly MediaLibraryDbContext _db;
    private readonly IMediaCachePathResolver _cache;
    private readonly INetworkSharePathResolver _pathResolver;
    private readonly MediaLibraryOptions _options;

    public MediaDerivativeService(
        MediaLibraryDbContext db,
        IMediaCachePathResolver cache,
        INetworkSharePathResolver pathResolver,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<string> EnsureAsync(long assetId, string variant, CancellationToken cancellationToken)
    {
        variant = variant.Equals("thumb", StringComparison.OrdinalIgnoreCase) ? "thumb" : "preview";
        var gate = AssetLocks.GetOrAdd(assetId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            var asset = await _db.Assets
                .Include(item => item.Source)
                .SingleOrDefaultAsync(item => item.Id == assetId, cancellationToken)
                ?? throw new FileNotFoundException("The media asset is not indexed.");

            if (!asset.IsAvailable || asset.IsDeleted || asset.Kind != MediaAssetKind.Photo)
            {
                throw new FileNotFoundException("The media asset is unavailable.");
            }

            if (asset.Source.SourceType != MediaLibrarySourceType.NetworkShare
                || string.IsNullOrWhiteSpace(asset.Source.RootPath)
                || string.IsNullOrWhiteSpace(asset.RelativePath))
            {
                throw new InvalidOperationException("Derivatives are generated only for network-share assets.");
            }

            var output = variant == "thumb"
                ? _cache.GetThumbnailPath(asset.Id, asset.CacheVersion)
                : _cache.GetPreviewPath(asset.Id, asset.CacheVersion);

            if (File.Exists(output))
            {
                return output;
            }

            var input = _pathResolver.ResolveAssetPath(asset.Source.RootPath, asset.RelativePath);
            var file = new FileInfo(input);
            if (!file.Exists)
            {
                throw new FileNotFoundException("The original media file is not currently reachable.", input);
            }

            if (file.Length > _options.MaxImageFileSizeBytes)
            {
                throw new InvalidDataException("The image exceeds the configured processing size limit.");
            }

            var maxPixels = variant == "thumb" ? _options.ThumbnailMaxPixels : _options.PreviewMaxPixels;
            var directory = Path.GetDirectoryName(output)!;
            Directory.CreateDirectory(directory);
            var temporary = output + $".{Guid.NewGuid():N}.tmp";

            try
            {
                using var image = await Image.LoadAsync(input, cancellationToken);
                image.Mutate(context => context
                    .AutoOrient()
                    .Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(maxPixels, maxPixels),
                        Sampler = KnownResamplers.Lanczos3
                    }));

                await image.SaveAsWebpAsync(
                    temporary,
                    new WebpEncoder { Quality = _options.WebpQuality },
                    cancellationToken);

                File.Move(temporary, output, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            return output;
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
            {
                AssetLocks.TryRemove(assetId, out _);
            }
        }
    }
}
