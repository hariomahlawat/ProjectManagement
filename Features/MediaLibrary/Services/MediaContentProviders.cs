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
        var provider = _providers.FirstOrDefault(item => item.CanHandle(asset));
        return provider is null ? null : await provider.ResolveAsync(asset, cancellationToken);
    }
}

public sealed class FileSystemMediaContentProvider : IMediaContentProvider
{
    private readonly IFileSystemPathResolver _paths;

    public FileSystemMediaContentProvider(IFileSystemPathResolver paths) => _paths = paths;

    public bool CanHandle(MediaAsset asset) => asset.Origin == MediaAssetOrigin.ExternalFile;

    public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (asset.Source is null || string.IsNullOrWhiteSpace(asset.Source.RootPath) || string.IsNullOrWhiteSpace(asset.RelativePath))
            return Task.FromResult<MediaContentDescriptor?>(null);

        var path = _paths.ResolveAssetPath(asset.Source.RootPath, asset.RelativePath);
        var file = new FileInfo(path);
        if (!file.Exists) return Task.FromResult<MediaContentDescriptor?>(null);

        return Task.FromResult<MediaContentDescriptor?>(new(
            asset.OriginalFileName,
            asset.ContentType,
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            _ => Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))));
    }
}

public sealed class ProjectPhotoMediaContentProvider : IMediaContentProvider
{
    private readonly IProjectPhotoService _photos;
    public ProjectPhotoMediaContentProvider(IProjectPhotoService photos) => _photos = photos;
    public bool CanHandle(MediaAsset asset) => asset.Origin == MediaAssetOrigin.ProjectPhoto;

    public async Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        if (!TryIds(asset, out var projectId, out var photoId)) return null;
        return new MediaContentDescriptor(asset.OriginalFileName, asset.ContentType, asset.FileSizeBytes > 0 ? asset.FileSizeBytes : null,
            asset.FileModifiedAtUtc, async ct =>
            {
                var opened = await _photos.OpenDerivativeAsync(projectId, photoId, "xl", preferWebp: false, ct)
                    ?? throw new FileNotFoundException("The project photo is no longer available.");
                return opened.Stream;
            });
    }

    private static bool TryIds(MediaAsset asset, out int parentId, out int entityId)
    {
        parentId = entityId = 0;
        var suffix = asset.SourceEntityId.Split(':').LastOrDefault();
        return int.TryParse(asset.ParentEntityId, out parentId) && int.TryParse(suffix, out entityId);
    }
}

public sealed class ProjectVideoMediaContentProvider : IMediaContentProvider
{
    private readonly IProjectVideoService _videos;
    public ProjectVideoMediaContentProvider(IProjectVideoService videos) => _videos = videos;
    public bool CanHandle(MediaAsset asset) => asset.Origin == MediaAssetOrigin.ProjectVideo;

    public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        var suffix = asset.SourceEntityId.Split(':').LastOrDefault();
        if (!int.TryParse(asset.ParentEntityId, out var projectId) || !int.TryParse(suffix, out var videoId))
            return Task.FromResult<MediaContentDescriptor?>(null);

        return Task.FromResult<MediaContentDescriptor?>(new MediaContentDescriptor(asset.OriginalFileName, asset.ContentType,
            asset.FileSizeBytes > 0 ? asset.FileSizeBytes : null, asset.FileModifiedAtUtc, async ct =>
            {
                var opened = await _videos.OpenOriginalAsync(projectId, videoId, ct)
                    ?? throw new FileNotFoundException("The project video is no longer available.");
                return opened.Stream;
            }));
    }
}

public sealed class VisitPhotoMediaContentProvider : IMediaContentProvider
{
    private readonly IVisitPhotoService _photos;
    public VisitPhotoMediaContentProvider(IVisitPhotoService photos) => _photos = photos;
    public bool CanHandle(MediaAsset asset) => asset.Origin == MediaAssetOrigin.VisitPhoto;

    public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        var suffix = asset.SourceEntityId.Split(':').LastOrDefault();
        if (!Guid.TryParse(asset.ParentEntityId, out var visitId) || !Guid.TryParse(suffix, out var photoId))
            return Task.FromResult<MediaContentDescriptor?>(null);

        return Task.FromResult<MediaContentDescriptor?>(new MediaContentDescriptor(asset.OriginalFileName, asset.ContentType,
            asset.FileSizeBytes > 0 ? asset.FileSizeBytes : null, asset.FileModifiedAtUtc, async ct =>
            {
                var opened = await _photos.OpenAsync(visitId, photoId, "original", ct)
                    ?? throw new FileNotFoundException("The visit photo is no longer available.");
                return opened.Stream;
            }));
    }
}

public sealed class SocialMediaPhotoMediaContentProvider : IMediaContentProvider
{
    private readonly ISocialMediaEventPhotoService _photos;
    public SocialMediaPhotoMediaContentProvider(ISocialMediaEventPhotoService photos) => _photos = photos;
    public bool CanHandle(MediaAsset asset) => asset.Origin == MediaAssetOrigin.SocialMediaEventPhoto;

    public Task<MediaContentDescriptor?> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        var suffix = asset.SourceEntityId.Split(':').LastOrDefault();
        if (!Guid.TryParse(asset.ParentEntityId, out var eventId) || !Guid.TryParse(suffix, out var photoId))
            return Task.FromResult<MediaContentDescriptor?>(null);

        return Task.FromResult<MediaContentDescriptor?>(new MediaContentDescriptor(asset.OriginalFileName, asset.ContentType,
            asset.FileSizeBytes > 0 ? asset.FileSizeBytes : null, asset.FileModifiedAtUtc, async ct =>
            {
                var opened = await _photos.OpenAsync(eventId, photoId, "original", ct)
                    ?? throw new FileNotFoundException("The event photo is no longer available.");
                return opened.Stream;
            }));
    }
}
