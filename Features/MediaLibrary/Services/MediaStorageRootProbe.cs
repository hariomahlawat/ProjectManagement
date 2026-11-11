using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Services.Storage;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Distinguishes "this file is gone" from "the storage holding it is unreachable".
/// A file-system lookup against an unreachable share or upload volume reports the file as
/// absent, which must not be recorded as a permanent source removal.
/// </summary>
public interface IMediaStorageRootProbe
{
    /// <summary>
    /// Returns <c>false</c> only when the storage root for <paramref name="asset"/> is
    /// positively unreachable. Returns <c>true</c> when the root is reachable or cannot be
    /// determined, so existing missing-file handling is preserved in those cases.
    /// </summary>
    bool IsStorageRootReachable(MediaAsset asset);
}

public sealed class MediaStorageRootProbe : IMediaStorageRootProbe
{
    private readonly IFileSystemPathResolver _paths;
    private readonly IUploadRootProvider _uploadRoot;
    private readonly ILogger<MediaStorageRootProbe> _logger;

    public MediaStorageRootProbe(
        IFileSystemPathResolver paths,
        IUploadRootProvider uploadRoot,
        ILogger<MediaStorageRootProbe> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _uploadRoot = uploadRoot ?? throw new ArgumentNullException(nameof(uploadRoot));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsStorageRootReachable(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        string root;
        try
        {
            if (asset.Origin == MediaAssetOrigin.ExternalFile)
            {
                if (asset.Source is null || string.IsNullOrWhiteSpace(asset.Source.RootPath))
                {
                    return true;
                }

                root = _paths.ResolveRoot(asset.Source.RootPath);
            }
            else
            {
                // Project, visit, social-media and activity media all live under the
                // configured PRISM upload root.
                root = _uploadRoot.RootPath;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            return true;
        }

        // Directory.Exists is false for a missing directory and for any I/O or access
        // failure, which is exactly the outage case this probe exists to detect.
        if (Directory.Exists(root))
        {
            return true;
        }

        _logger.LogWarning(
            "Storage root for media asset {AssetId} ({Origin}) is unreachable; treating the failure as temporary.",
            asset.Id,
            asset.Origin);
        return false;
    }
}
