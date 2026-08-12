using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace ProjectManagement.Services.Publications;

public interface IBrochurePhotoService
{
    Task<IReadOnlyDictionary<int, BrochurePhotoProbe>> ProbeAsync(
        IReadOnlyCollection<BrochurePhotoReference> references,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, BrochurePublicationImage>> RenderAsync(
        IReadOnlyCollection<BrochurePhotoRenderRequest> requests,
        CancellationToken cancellationToken = default);

    Task<BrochurePublicationImage?> RenderAsync(
        BrochurePhotoRenderRequest request,
        CancellationToken cancellationToken = default);

    Task<BrochurePhotoPreview?> GetPreviewAsync(
        int projectId,
        int photoId,
        BrochurePhotoPreviewKind kind,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Publication-specific image pipeline. The master image is preferred, with the largest
/// available project derivative used only as a fallback. Browser previews, preflight and
/// final PDF rendering all resolve through this same source-selection path so focal points
/// are interpreted against the same image geometry the publication renderer uses.
/// </summary>
public sealed class BrochurePhotoService : IBrochurePhotoService
{
    private const int PrintReadyWidth = 1600;
    private const int PrintReadyHeight = 900;
    private const int ExcellentWidth = 2400;
    private const int ExcellentHeight = 1350;
    private const int AcceptableWidth = 1100;
    private const int AcceptableHeight = 619;
    private const int ThumbnailWidth = 360;
    private const int ThumbnailHeight = 216;
    private const int SourcePreviewMaxWidth = 1500;
    private const int SourcePreviewMaxHeight = 1100;

    private static readonly string[] MasterExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] PreferredDerivativeKeys = ["xl", "lg", "md", "sm", "xs"];
    private static readonly MemoryCacheEntryOptions ProbeCacheOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(15),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
    };

    private readonly ApplicationDbContext _db;
    private readonly IUploadRootProvider _uploadRoots;
    private readonly ProjectPhotoOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BrochurePhotoService> _logger;

    public BrochurePhotoService(
        ApplicationDbContext db,
        IUploadRootProvider uploadRoots,
        IOptions<ProjectPhotoOptions> options,
        IMemoryCache cache,
        ILogger<BrochurePhotoService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _uploadRoots = uploadRoots ?? throw new ArgumentNullException(nameof(uploadRoots));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyDictionary<int, BrochurePhotoProbe>> ProbeAsync(
        IReadOnlyCollection<BrochurePhotoReference> references,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeReferences(references);
        if (normalized.Length == 0)
        {
            return new Dictionary<int, BrochurePhotoProbe>();
        }

        var photoIds = normalized.Select(reference => reference.PhotoId).ToArray();
        var photos = await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => photoIds.Contains(photo.Id))
            .ToDictionaryAsync(photo => photo.Id, cancellationToken);

        var result = new Dictionary<int, BrochurePhotoProbe>(normalized.Length);
        foreach (var reference in normalized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!photos.TryGetValue(reference.PhotoId, out var photo)
                || photo.ProjectId != reference.ProjectId)
            {
                result[reference.PhotoId] = new BrochurePhotoProbe(
                    reference.ProjectId,
                    reference.PhotoId,
                    false,
                    false,
                    0,
                    0,
                    null,
                    "The selected photograph does not belong to this project or no longer exists.");
                continue;
            }

            result[reference.PhotoId] = ProbePhoto(photo, cancellationToken);
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<int, BrochurePublicationImage>> RenderAsync(
        IReadOnlyCollection<BrochurePhotoRenderRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var normalized = requests?
            .Where(request => request.ProjectId > 0 && request.PhotoId > 0)
            .GroupBy(request => request.PhotoId)
            .Select(group => group.First())
            .ToArray() ?? Array.Empty<BrochurePhotoRenderRequest>();
        if (normalized.Length == 0)
        {
            return new Dictionary<int, BrochurePublicationImage>();
        }

        var photoIds = normalized.Select(request => request.PhotoId).ToArray();
        var photos = await _db.ProjectPhotos
            .AsNoTracking()
            .Where(photo => photoIds.Contains(photo.Id))
            .ToDictionaryAsync(photo => photo.Id, cancellationToken);

        var result = new Dictionary<int, BrochurePublicationImage>(normalized.Length);
        foreach (var request in normalized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!photos.TryGetValue(request.PhotoId, out var photo)
                || photo.ProjectId != request.ProjectId)
            {
                continue;
            }

            var rendered = await RenderPhotoAsync(photo, request, cancellationToken);
            if (rendered is not null)
            {
                result[request.PhotoId] = rendered;
            }
        }

        return result;
    }

    public async Task<BrochurePublicationImage?> RenderAsync(
        BrochurePhotoRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProjectId <= 0 || request.PhotoId <= 0)
        {
            return null;
        }

        var photo = await _db.ProjectPhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.Id == request.PhotoId && row.ProjectId == request.ProjectId,
                cancellationToken);
        if (photo is null)
        {
            return null;
        }

        return await RenderPhotoAsync(photo, request, cancellationToken);
    }

    public async Task<BrochurePhotoPreview?> GetPreviewAsync(
        int projectId,
        int photoId,
        BrochurePhotoPreviewKind kind,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0 || photoId <= 0 || !Enum.IsDefined(kind))
        {
            return null;
        }

        var photo = await _db.ProjectPhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => row.Id == photoId && row.ProjectId == projectId,
                cancellationToken);
        if (photo is null)
        {
            return null;
        }

        var source = ResolveUsableSource(photo, cancellationToken);
        if (source is null)
        {
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(source.Path, cancellationToken);
            using var image = Image.Load(bytes);
            image.Mutate(context => context.AutoOrient());

            var sourceWidth = image.Width;
            var sourceHeight = image.Height;
            if (kind == BrochurePhotoPreviewKind.Thumbnail)
            {
                var crop = CalculateCropRectangle(
                    sourceWidth,
                    sourceHeight,
                    ThumbnailWidth,
                    ThumbnailHeight,
                    .5d,
                    .5d);
                image.Mutate(context => context
                    .Crop(crop)
                    .Resize(ThumbnailWidth, ThumbnailHeight));
            }
            else
            {
                ResizeWithinBounds(image, SourcePreviewMaxWidth, SourcePreviewMaxHeight);
            }

            using var output = new MemoryStream();
            image.Save(output, new JpegEncoder { Quality = kind == BrochurePhotoPreviewKind.Thumbnail ? 82 : 88 });

            return new BrochurePhotoPreview(
                output.ToArray(),
                "image/jpeg",
                sourceWidth,
                sourceHeight,
                source.Variant,
                DetermineQuality(sourceWidth, sourceHeight));
        }
        catch (Exception exception) when (IsRecoverableImageException(exception))
        {
            _logger.LogWarning(
                exception,
                "Publication photo preview could not be generated. ProjectId={ProjectId}, PhotoId={PhotoId}, Variant={Variant}",
                projectId,
                photoId,
                source.Variant);
            return null;
        }
    }

    private BrochurePhotoProbe ProbePhoto(ProjectPhoto photo, CancellationToken cancellationToken)
    {
        var source = ResolveUsableSource(photo, cancellationToken);
        if (source is null)
        {
            return new BrochurePhotoProbe(
                photo.ProjectId,
                photo.Id,
                false,
                false,
                0,
                0,
                null,
                "No usable master image or photograph derivative was found.");
        }

        return new BrochurePhotoProbe(
            photo.ProjectId,
            photo.Id,
            true,
            source.Quality >= BrochurePhotoQuality.PrintReady,
            source.Width,
            source.Height,
            source.Variant,
            null,
            source.Quality);
    }

    private async Task<BrochurePublicationImage?> RenderPhotoAsync(
        ProjectPhoto photo,
        BrochurePhotoRenderRequest request,
        CancellationToken cancellationToken)
    {
        var source = ResolveUsableSource(photo, cancellationToken);
        if (source is null)
        {
            _logger.LogWarning(
                "No brochure-ready photograph was found. ProjectId={ProjectId}, PhotoId={PhotoId}",
                photo.ProjectId,
                photo.Id);
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(source.Path, cancellationToken);
            using var image = Image.Load(bytes);
            image.Mutate(context => context.AutoOrient());

            var sourceWidth = image.Width;
            var sourceHeight = image.Height;
            var crop = CalculateCropRectangle(
                sourceWidth,
                sourceHeight,
                request.TargetWidth,
                request.TargetHeight,
                ClampFocal(request.FocalX),
                ClampFocal(request.FocalY));

            image.Mutate(context => context
                .Crop(crop)
                .Resize(request.TargetWidth, request.TargetHeight)
                .BackgroundColor(Color.White));

            using var output = new MemoryStream();
            image.Save(output, new JpegEncoder { Quality = 91 });

            var quality = DetermineQuality(sourceWidth, sourceHeight);
            return new BrochurePublicationImage(
                photo.Id,
                output.ToArray(),
                sourceWidth,
                sourceHeight,
                quality >= BrochurePhotoQuality.PrintReady,
                source.Variant,
                quality);
        }
        catch (Exception exception) when (IsRecoverableImageException(exception))
        {
            _logger.LogWarning(
                exception,
                "Brochure photograph could not be rendered. ProjectId={ProjectId}, PhotoId={PhotoId}, Variant={Variant}",
                photo.ProjectId,
                photo.Id,
                source.Variant);
            return null;
        }
    }

    private ResolvedPhotoSource? ResolveUsableSource(ProjectPhoto photo, CancellationToken cancellationToken)
    {
        var foundFile = false;
        foreach (var candidate in EnumerateCandidates(photo))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(candidate.Path))
            {
                continue;
            }

            foundFile = true;
            var file = new FileInfo(candidate.Path);
            var cacheKey = $"pub-photo-probe:{photo.Id}:{photo.Version}:{candidate.Path}:{file.Length}:{file.LastWriteTimeUtc.Ticks}";
            if (_cache.TryGetValue<CandidateProbe>(cacheKey, out var cached) && cached is not null)
            {
                if (cached.IsReady)
                {
                    return new ResolvedPhotoSource(
                        candidate.Path,
                        candidate.Variant,
                        cached.Width,
                        cached.Height,
                        cached.Quality);
                }

                continue;
            }

            CandidateProbe probe;
            try
            {
                using var stream = new FileStream(
                    candidate.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var image = Image.Load(stream);
                image.Mutate(context => context.AutoOrient());
                var quality = DetermineQuality(image.Width, image.Height);
                probe = new CandidateProbe(true, image.Width, image.Height, quality);
            }
            catch (Exception exception) when (IsRecoverableImageException(exception))
            {
                _logger.LogDebug(
                    exception,
                    "Brochure photo source candidate rejected. ProjectId={ProjectId}, PhotoId={PhotoId}, Variant={Variant}",
                    photo.ProjectId,
                    photo.Id,
                    candidate.Variant);
                probe = new CandidateProbe(false, 0, 0, BrochurePhotoQuality.Low);
            }

            _cache.Set(cacheKey, probe, ProbeCacheOptions);
            if (probe.IsReady)
            {
                return new ResolvedPhotoSource(
                    candidate.Path,
                    candidate.Variant,
                    probe.Width,
                    probe.Height,
                    probe.Quality);
            }
        }

        if (foundFile)
        {
            _logger.LogDebug(
                "Brochure photograph files were present but none could be decoded. ProjectId={ProjectId}, PhotoId={PhotoId}",
                photo.ProjectId,
                photo.Id);
        }

        return null;
    }

    internal static Rectangle CalculateCropRectangle(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        double focalX,
        double focalY)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Source and target dimensions must be positive.");
        }

        focalX = ClampFocal(focalX);
        focalY = ClampFocal(focalY);

        var sourceAspect = sourceWidth / (double)sourceHeight;
        var targetAspect = targetWidth / (double)targetHeight;

        int cropWidth;
        int cropHeight;
        if (sourceAspect > targetAspect)
        {
            cropHeight = sourceHeight;
            cropWidth = Math.Max(1, (int)Math.Round(cropHeight * targetAspect));
        }
        else
        {
            cropWidth = sourceWidth;
            cropHeight = Math.Max(1, (int)Math.Round(cropWidth / targetAspect));
        }

        cropWidth = Math.Min(cropWidth, sourceWidth);
        cropHeight = Math.Min(cropHeight, sourceHeight);

        var focalPixelX = focalX * sourceWidth;
        var focalPixelY = focalY * sourceHeight;
        var x = (int)Math.Round(focalPixelX - (cropWidth / 2d));
        var y = (int)Math.Round(focalPixelY - (cropHeight / 2d));
        x = Math.Clamp(x, 0, sourceWidth - cropWidth);
        y = Math.Clamp(y, 0, sourceHeight - cropHeight);

        return new Rectangle(x, y, cropWidth, cropHeight);
    }

    internal static BrochurePhotoQuality DetermineQuality(int width, int height)
    {
        var (effectiveWidth, effectiveHeight) = EffectiveWideCropDimensions(width, height);
        if (effectiveWidth >= ExcellentWidth && effectiveHeight >= ExcellentHeight)
        {
            return BrochurePhotoQuality.Excellent;
        }
        if (effectiveWidth >= PrintReadyWidth && effectiveHeight >= PrintReadyHeight)
        {
            return BrochurePhotoQuality.PrintReady;
        }
        if (effectiveWidth >= AcceptableWidth && effectiveHeight >= AcceptableHeight)
        {
            return BrochurePhotoQuality.Acceptable;
        }
        return BrochurePhotoQuality.Low;
    }

    internal static (double Width, double Height) EffectiveWideCropDimensions(int width, int height)
        => EffectiveCropDimensions(width, height, 16d / 9d);

    internal static (double Width, double Height) EffectiveCropDimensions(
        int width,
        int height,
        double targetAspect)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(targetAspect) || targetAspect <= 0d)
        {
            return (0d, 0d);
        }

        var sourceAspect = width / (double)height;
        return sourceAspect > targetAspect
            ? (height * targetAspect, height)
            : (width, width / targetAspect);
    }

    private IEnumerable<PhotoCandidate> EnumerateCandidates(ProjectPhoto photo)
    {
        var directory = _uploadRoots.GetProjectPhotosRoot(photo.ProjectId);
        foreach (var extension in PreferredExtensions(photo.ContentType))
        {
            yield return new PhotoCandidate(
                Path.Combine(directory, $"{photo.StorageKey}-master{extension}"),
                $"master/{extension.TrimStart('.')}");
        }

        var sizeKeys = PreferredDerivativeKeys
            .Where(key => _options.Derivatives.ContainsKey(key))
            .Concat(_options.Derivatives
                .Where(pair => !PreferredDerivativeKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(pair => pair.Value.Width * pair.Value.Height)
                .Select(pair => pair.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var sizeKey in sizeKeys)
        {
            foreach (var extension in PreferredExtensions(photo.ContentType))
            {
                yield return new PhotoCandidate(
                    Path.Combine(directory, $"{photo.StorageKey}-{sizeKey}{extension}"),
                    $"{sizeKey}/{extension.TrimStart('.')}");
            }
        }
    }

    private static IReadOnlyList<string> PreferredExtensions(string? contentType)
    {
        var preferred = contentType?.Trim().ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };

        return new[] { preferred }
            .Concat(MasterExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static BrochurePhotoReference[] NormalizeReferences(
        IReadOnlyCollection<BrochurePhotoReference>? references)
        => references?
            .Where(reference => reference.ProjectId > 0 && reference.PhotoId > 0)
            .GroupBy(reference => reference.PhotoId)
            .Select(group => group.First())
            .ToArray() ?? Array.Empty<BrochurePhotoReference>();

    private static void ResizeWithinBounds(Image image, int maxWidth, int maxHeight)
    {
        if (image.Width <= maxWidth && image.Height <= maxHeight)
        {
            return;
        }

        var ratio = Math.Min(maxWidth / (double)image.Width, maxHeight / (double)image.Height);
        var width = Math.Max(1, (int)Math.Round(image.Width * ratio));
        var height = Math.Max(1, (int)Math.Round(image.Height * ratio));
        image.Mutate(context => context.Resize(width, height));
    }

    private static bool IsRecoverableImageException(Exception exception)
    {
        if (exception is IOException
            or UnauthorizedAccessException
            or UnknownImageFormatException
            or InvalidImageContentException
            or ArgumentException
            or NotSupportedException)
        {
            return true;
        }

        // ImageSharp can surface format/processing-specific exception types that vary between
        // library versions. At this single-photo boundary those failures mean "this source is not
        // renderable", not "crash the entire publication request". Limit the fallback strictly to
        // ImageSharp exception namespaces so process-level/runtime failures still propagate.
        var exceptionNamespace = exception.GetType().Namespace;
        return string.Equals(exceptionNamespace, "SixLabors.ImageSharp", StringComparison.Ordinal)
            || (exceptionNamespace?.StartsWith("SixLabors.ImageSharp.", StringComparison.Ordinal) ?? false);
    }

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;

    private sealed record PhotoCandidate(string Path, string Variant);
    private sealed record CandidateProbe(bool IsReady, int Width, int Height, BrochurePhotoQuality Quality);
    private sealed record ResolvedPhotoSource(string Path, string Variant, int Width, int Height, BrochurePhotoQuality Quality);
}
