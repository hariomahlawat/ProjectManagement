using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed record MediaContentDescriptor(
    string FileName,
    string ContentType,
    long? Length,
    DateTimeOffset? LastModifiedUtc,
    Func<CancellationToken, Task<Stream>> OpenReadAsync);

public interface IMediaContentProvider
{
    bool CanHandle(MediaAsset asset);
    Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken);
}

public interface IMediaContentProviderResolver
{
    Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken);
}

public sealed class MediaContentProviderResolver : IMediaContentProviderResolver
{
    private readonly IReadOnlyList<IMediaContentProvider> _providers;

    public MediaContentProviderResolver(IEnumerable<IMediaContentProvider> providers)
    {
        _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
    }

    public async Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var matching = _providers.Where(item => item.CanHandle(asset)).ToArray();
        if (matching.Length == 0)
        {
            return null;
        }

        Exception? lastError = null;
        foreach (var provider in matching)
        {
            try
            {
                var resolved = await provider.ResolveAsync(asset, cancellationToken);
                if (resolved is not null)
                {
                    return resolved;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
        {
            throw new MediaContentUnavailableException(
                $"No registered content provider could open media asset {asset.Id} ({asset.Origin}).",
                lastError);
        }

        return null;
    }
}

public sealed class FileSystemMediaContentProvider : IMediaContentProvider
{
    private readonly IFileSystemPathResolver _paths;

    public FileSystemMediaContentProvider(IFileSystemPathResolver paths)
        => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public bool CanHandle(MediaAsset asset) => asset.Origin == MediaAssetOrigin.ExternalFile;

    public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (asset.Source is null
            || string.IsNullOrWhiteSpace(asset.Source.RootPath)
            || string.IsNullOrWhiteSpace(asset.RelativePath))
        {
            return Task.FromResult<MediaContentDescriptor?>(null);
        }

        var path = _paths.ResolveAssetPath(asset.Source.RootPath, asset.RelativePath);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return Task.FromResult<MediaContentDescriptor?>(null);
        }

        return Task.FromResult<MediaContentDescriptor?>(new(
            SafeFileName(asset.OriginalFileName, file.Name),
            asset.ContentType,
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            _ => Task.FromResult<Stream>(new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))));
    }

    private static string SafeFileName(string? preferred, string fallback)
        => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
}

public sealed class ProjectPhotoMediaContentProvider : IMediaContentProvider
{
    private static readonly string[] Variants = { "xl", "md", "sm", "xs" };
    private readonly IProjectPhotoService _photos;

    public ProjectPhotoMediaContentProvider(IProjectPhotoService photos)
        => _photos = photos ?? throw new ArgumentNullException(nameof(photos));

    public bool CanHandle(MediaAsset asset) => asset.Origin == MediaAssetOrigin.ProjectPhoto;

    public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        if (!TryIds(asset, out var projectId, out var photoId))
        {
            return Task.FromResult<MediaContentDescriptor?>(null);
        }

        return Task.FromResult<MediaContentDescriptor?>(new(
            asset.OriginalFileName,
            asset.ContentType,
            asset.FileSizeBytes > 0 ? asset.FileSizeBytes : null,
            asset.FileModifiedAtUtc,
            ct => OpenFirstAsync(projectId, photoId, ct)));
    }

    private async Task<Stream> OpenFirstAsync(int projectId, int photoId, CancellationToken cancellationToken)
    {
        foreach (var variant in Variants)
        {
            var opened = await _photos.OpenDerivativeAsync(
                projectId,
                photoId,
                variant,
                preferWebp: false,
                cancellationToken);
            if (opened is not null)
            {
                return opened.Value.Stream;
            }
        }

        throw new MediaContentUnavailableException(
            $"No project-photo derivative is available for project {projectId}, photo {photoId}.");
    }

    private static bool TryIds(MediaAsset asset, out int parentId, out int entityId)
    {
        parentId = entityId = 0;
        var suffix = asset.SourceEntityId.Split(':').LastOrDefault();
        return int.TryParse(asset.ParentEntityId, out parentId)
               && int.TryParse(suffix, out entityId);
    }
}

public sealed class ProjectVideoMediaContentProvider : IMediaContentProvider
{
    private readonly IProjectVideoService _videos;

    public ProjectVideoMediaContentProvider(IProjectVideoService videos)
        => _videos = videos ?? throw new ArgumentNullException(nameof(videos));

    public bool CanHandle(MediaAsset asset) => asset.Origin == MediaAssetOrigin.ProjectVideo;

    public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        var suffix = asset.SourceEntityId.Split(':').LastOrDefault();
        if (!int.TryParse(asset.ParentEntityId, out var projectId)
            || !int.TryParse(suffix, out var videoId))
        {
            return Task.FromResult<MediaContentDescriptor?>(null);
        }

        return Task.FromResult<MediaContentDescriptor?>(new(
            asset.OriginalFileName,
            asset.ContentType,
            asset.FileSizeBytes > 0 ? asset.FileSizeBytes : null,
            asset.FileModifiedAtUtc,
            async ct =>
            {
                var opened = await _videos.OpenOriginalAsync(projectId, videoId, ct);
                if (opened is null)
                {
                    throw new MediaContentUnavailableException(
                        $"The project video {videoId} in project {projectId} is no longer available.");
                }

                return opened.Value.Stream;
            }));
    }
}

public sealed class VisitPhotoMediaContentProvider : IMediaContentProvider
{
    private static readonly string[] Variants = { "original", "xl", "md", "sm", "xs" };
    private readonly IVisitPhotoService _photos;

    public VisitPhotoMediaContentProvider(IVisitPhotoService photos)
        => _photos = photos ?? throw new ArgumentNullException(nameof(photos));

    public bool CanHandle(MediaAsset asset) => asset.Origin == MediaAssetOrigin.VisitPhoto;

    public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        var suffix = asset.SourceEntityId.Split(':').LastOrDefault();
        if (!Guid.TryParse(asset.ParentEntityId, out var visitId)
            || !Guid.TryParse(suffix, out var photoId))
        {
            return Task.FromResult<MediaContentDescriptor?>(null);
        }

        return Task.FromResult<MediaContentDescriptor?>(new(
            asset.OriginalFileName,
            asset.ContentType,
            asset.FileSizeBytes > 0 ? asset.FileSizeBytes : null,
            asset.FileModifiedAtUtc,
            ct => OpenFirstAsync(visitId, photoId, ct)));
    }

    private async Task<Stream> OpenFirstAsync(Guid visitId, Guid photoId, CancellationToken cancellationToken)
    {
        foreach (var variant in Variants)
        {
            var opened = await _photos.OpenAsync(visitId, photoId, variant, cancellationToken);
            if (opened is not null)
            {
                return opened.Stream;
            }
        }

        throw new MediaContentUnavailableException(
            $"No visit-photo asset is available for visit {visitId}, photo {photoId}.");
    }
}

public sealed class SocialMediaPhotoMediaContentProvider : IMediaContentProvider
{
    private static readonly string[] Variants = { "original", "feed", "story", "thumb" };
    private readonly ISocialMediaEventPhotoService _photos;

    public SocialMediaPhotoMediaContentProvider(ISocialMediaEventPhotoService photos)
        => _photos = photos ?? throw new ArgumentNullException(nameof(photos));

    public bool CanHandle(MediaAsset asset) => asset.Origin == MediaAssetOrigin.SocialMediaEventPhoto;

    public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        var suffix = asset.SourceEntityId.Split(':').LastOrDefault();
        if (!Guid.TryParse(asset.ParentEntityId, out var eventId)
            || !Guid.TryParse(suffix, out var photoId))
        {
            return Task.FromResult<MediaContentDescriptor?>(null);
        }

        return Task.FromResult<MediaContentDescriptor?>(new(
            asset.OriginalFileName,
            asset.ContentType,
            asset.FileSizeBytes > 0 ? asset.FileSizeBytes : null,
            asset.FileModifiedAtUtc,
            ct => OpenFirstAsync(eventId, photoId, ct)));
    }

    private async Task<Stream> OpenFirstAsync(Guid eventId, Guid photoId, CancellationToken cancellationToken)
    {
        foreach (var variant in Variants)
        {
            var opened = await _photos.OpenAsync(eventId, photoId, variant, cancellationToken);
            if (opened is not null)
            {
                return opened.Stream;
            }
        }

        throw new MediaContentUnavailableException(
            $"No social-media photo asset is available for event {eventId}, photo {photoId}.");
    }
}
