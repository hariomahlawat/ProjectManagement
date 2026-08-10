using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Storage;
using SixLabors.ImageSharp;
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
}

/// <summary>
/// Publication-specific image pipeline. Unlike the PowerPoint photo loader, this service
/// starts from the preserved master (or the largest usable derivative), honours a user
/// selected focal point and creates a deterministic 16:9 publication crop.
/// </summary>
public sealed class BrochurePhotoService : IBrochurePhotoService
{
    private const int PrintReadyWidth = 1600;
    private const int PrintReadyHeight = 900;

    private static readonly string[] MasterExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] PreferredDerivativeKeys = ["xl", "lg", "md", "sm", "xs"];

    private readonly ApplicationDbContext _db;
    private readonly IUploadRootProvider _uploadRoots;
    private readonly ProjectPhotoOptions _options;
    private readonly ILogger<BrochurePhotoService> _logger;

    public BrochurePhotoService(
        ApplicationDbContext db,
        IUploadRootProvider uploadRoots,
        IOptions<ProjectPhotoOptions> options,
        ILogger<BrochurePhotoService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _uploadRoots = uploadRoots ?? throw new ArgumentNullException(nameof(uploadRoots));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
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

            result[reference.PhotoId] = ProbePhoto(photo);
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

    private BrochurePhotoProbe ProbePhoto(ProjectPhoto photo)
    {
        var foundFile = false;
        foreach (var candidate in EnumerateCandidates(photo))
        {
            if (!File.Exists(candidate.Path))
            {
                continue;
            }

            foundFile = true;
            try
            {
                using var stream = new FileStream(
                    candidate.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var info = Image.Identify(stream);
                if (info is null)
                {
                    continue;
                }

                var printReady = !photo.IsLowResolution && IsPrintReadyForWideCrop(info.Width, info.Height);
                return new BrochurePhotoProbe(
                    photo.ProjectId,
                    photo.Id,
                    true,
                    printReady,
                    info.Width,
                    info.Height,
                    candidate.Variant);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or UnknownImageFormatException
                                               or InvalidImageContentException)
            {
                _logger.LogDebug(
                    exception,
                    "Brochure photo probe rejected candidate. ProjectId={ProjectId}, PhotoId={PhotoId}, Variant={Variant}",
                    photo.ProjectId,
                    photo.Id,
                    candidate.Variant);
            }
        }

        return new BrochurePhotoProbe(
            photo.ProjectId,
            photo.Id,
            false,
            false,
            photo.Width,
            photo.Height,
            null,
            foundFile
                ? "The selected photograph exists but cannot be decoded for publication."
                : "No usable master image or photograph derivative was found.");
    }

    private async Task<BrochurePublicationImage?> RenderPhotoAsync(
        ProjectPhoto photo,
        BrochurePhotoRenderRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in EnumerateCandidates(photo))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(candidate.Path))
            {
                continue;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(candidate.Path, cancellationToken);
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

                return new BrochurePublicationImage(
                    photo.Id,
                    output.ToArray(),
                    sourceWidth,
                    sourceHeight,
                    !photo.IsLowResolution && IsPrintReadyForWideCrop(sourceWidth, sourceHeight),
                    candidate.Variant);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or UnknownImageFormatException
                                               or InvalidImageContentException)
            {
                _logger.LogWarning(
                    exception,
                    "Brochure photo candidate could not be rendered. ProjectId={ProjectId}, PhotoId={PhotoId}, Variant={Variant}",
                    photo.ProjectId,
                    photo.Id,
                    candidate.Variant);
            }
        }

        _logger.LogWarning(
            "No brochure-ready photograph was found. ProjectId={ProjectId}, PhotoId={PhotoId}",
            photo.ProjectId,
            photo.Id);
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

    private static bool IsPrintReadyForWideCrop(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        const double targetAspect = 16d / 9d;
        var sourceAspect = width / (double)height;
        var effectiveWidth = sourceAspect > targetAspect
            ? height * targetAspect
            : width;
        var effectiveHeight = sourceAspect > targetAspect
            ? height
            : width / targetAspect;

        return effectiveWidth >= PrintReadyWidth && effectiveHeight >= PrintReadyHeight;
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

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;

    private sealed record PhotoCandidate(string Path, string Variant);
}
