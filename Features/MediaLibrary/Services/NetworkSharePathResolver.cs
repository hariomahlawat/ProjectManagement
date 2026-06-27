namespace ProjectManagement.Features.MediaLibrary.Services;

public sealed class NetworkSharePathResolver : INetworkSharePathResolver
{
    public string ResolveRoot(string configuredRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            throw new InvalidOperationException("The media source root path is not configured.");
        }

        if (!Path.IsPathFullyQualified(configuredRoot))
        {
            throw new InvalidOperationException("The media source root path must be fully qualified.");
        }

        var fullPath = Path.GetFullPath(configuredRoot);
        var pathRoot = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrWhiteSpace(pathRoot)
            && string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase))
        {
            return pathRoot;
        }

        return TrimTrailingSeparators(fullPath);
    }

    public string ResolveAssetPath(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("The media asset relative path is empty.");
        }

        var root = ResolveRoot(rootPath);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureWithinRoot(root, candidate);
        return candidate;
    }

    public string ToRelativePath(string rootPath, string fullPath)
    {
        var root = ResolveRoot(rootPath);
        var candidate = Path.GetFullPath(fullPath);
        EnsureWithinRoot(root, candidate);

        var relative = Path.GetRelativePath(root, candidate)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        if (relative is "." or ".." || relative.StartsWith("../", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The resolved media path is outside the configured source root.");
        }

        return relative;
    }

    private static void EnsureWithinRoot(string root, string candidate)
    {
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            || root.EndsWith(Path.AltDirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
        if (!candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The resolved media path is outside the configured source root.");
        }
    }

    private static string TrimTrailingSeparators(string value)
        => value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
