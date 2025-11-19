using System;
using System.IO;

namespace ProjectManagement.Services.Storage;

public interface IUploadPathResolver
{
    string ToAbsolute(string storageKey);

    string ToRelative(string absolutePath);
}

public sealed class UploadPathResolver : IUploadPathResolver
{
    private readonly IUploadRootProvider _uploadRootProvider;

    public UploadPathResolver(IUploadRootProvider uploadRootProvider)
    {
        _uploadRootProvider = uploadRootProvider ?? throw new ArgumentNullException(nameof(uploadRootProvider));
    }

    public string ToAbsolute(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key is required.", nameof(storageKey));
        }

        if (Path.IsPathRooted(storageKey))
        {
            return Path.GetFullPath(storageKey);
        }

        var normalized = storageKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var combined = Path.Combine(_uploadRootProvider.RootPath, normalized);
        var fullPath = Path.GetFullPath(combined);

        if (!fullPath.StartsWith(_uploadRootProvider.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage key resolved outside of the configured upload root.");
        }

        return fullPath;
    }

    public string ToRelative(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            throw new ArgumentException("Absolute path is required.", nameof(absolutePath));
        }

        var fullPath = Path.GetFullPath(absolutePath);

        if (!fullPath.StartsWith(_uploadRootProvider.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        var relative = Path.GetRelativePath(_uploadRootProvider.RootPath, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
