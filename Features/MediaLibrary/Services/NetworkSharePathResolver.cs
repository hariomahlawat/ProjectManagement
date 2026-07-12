namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Resolves both local absolute paths and UNC paths while preventing traversal outside
/// the configured root. The historic filename is retained so upgrades can replace it
/// without requiring a manual delete.
/// </summary>
public sealed class FileSystemPathResolver : IFileSystemPathResolver
{
    public string ResolveRoot(string configuredRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            throw new InvalidOperationException("The media source root path is not configured.");
        }

        if (!Path.IsPathFullyQualified(configuredRoot))
        {
            throw new InvalidOperationException("The media source root path must be a fully-qualified local or UNC path.");
        }

        var fullPath = Path.GetFullPath(configuredRoot.Trim());
        var pathRoot = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrWhiteSpace(pathRoot)
            && string.Equals(fullPath, pathRoot, PathComparison))
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

        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException("A media asset path must be relative to its configured source root.");
        }

        var root = ResolveRoot(rootPath);
        var normalizedRelative = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(root, normalizedRelative));
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

    public string DescribePathKind(string configuredRoot)
    {
        var root = ResolveRoot(configuredRoot);
        return root.StartsWith(@"\\", StringComparison.Ordinal) ? "UNC share" : "Local folder";
    }

    private static void EnsureWithinRoot(string root, string candidate)
    {
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            || root.EndsWith(Path.AltDirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

        if (!candidate.Equals(root, PathComparison)
            && !candidate.StartsWith(rootPrefix, PathComparison))
        {
            throw new InvalidOperationException("The resolved media path is outside the configured source root.");
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string TrimTrailingSeparators(string value)
        => value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
