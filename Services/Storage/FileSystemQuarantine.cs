using System.Globalization;

namespace ProjectManagement.Services.Storage;

public sealed record FileQuarantineHandle(
    string OriginalPath,
    string QuarantinePath,
    bool IsDirectory);

public static class FileSystemQuarantine
{
    private const string QuarantineDirectoryName = ".purge-quarantine";

    public static FileQuarantineHandle? StageFile(
        string originalPath,
        string allowedRoot,
        string category,
        string identity)
    {
        var fullOriginalPath = ValidatePath(originalPath, allowedRoot);
        if (!File.Exists(fullOriginalPath))
        {
            return null;
        }

        var quarantinePath = BuildQuarantinePath(
            allowedRoot,
            category,
            identity,
            Path.GetExtension(fullOriginalPath));

        Directory.CreateDirectory(Path.GetDirectoryName(quarantinePath)!);
        File.Move(fullOriginalPath, quarantinePath, overwrite: false);

        return new FileQuarantineHandle(fullOriginalPath, quarantinePath, IsDirectory: false);
    }

    public static FileQuarantineHandle? StageDirectory(
        string originalPath,
        string allowedRoot,
        string category,
        string identity)
    {
        var fullOriginalPath = ValidatePath(originalPath, allowedRoot);
        if (!Directory.Exists(fullOriginalPath))
        {
            return null;
        }

        var quarantinePath = BuildQuarantinePath(allowedRoot, category, identity, extension: null);
        Directory.CreateDirectory(Path.GetDirectoryName(quarantinePath)!);
        Directory.Move(fullOriginalPath, quarantinePath);

        return new FileQuarantineHandle(fullOriginalPath, quarantinePath, IsDirectory: true);
    }

    public static void Restore(FileQuarantineHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (handle.IsDirectory)
        {
            if (!Directory.Exists(handle.QuarantinePath))
            {
                return;
            }

            if (Directory.Exists(handle.OriginalPath) || File.Exists(handle.OriginalPath))
            {
                throw new IOException($"Cannot restore quarantined directory because the original path already exists: {handle.OriginalPath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(handle.OriginalPath)!);
            Directory.Move(handle.QuarantinePath, handle.OriginalPath);
            return;
        }

        if (!File.Exists(handle.QuarantinePath))
        {
            return;
        }

        if (File.Exists(handle.OriginalPath) || Directory.Exists(handle.OriginalPath))
        {
            throw new IOException($"Cannot restore quarantined file because the original path already exists: {handle.OriginalPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(handle.OriginalPath)!);
        File.Move(handle.QuarantinePath, handle.OriginalPath, overwrite: false);
    }

    public static void FinalizeDeletion(FileQuarantineHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (handle.IsDirectory)
        {
            if (Directory.Exists(handle.QuarantinePath))
            {
                Directory.Delete(handle.QuarantinePath, recursive: true);
            }
        }
        else if (File.Exists(handle.QuarantinePath))
        {
            File.Delete(handle.QuarantinePath);
        }

        DeleteEmptyParents(handle.QuarantinePath);
    }

    public static string GetSafeReference(FileQuarantineHandle handle) =>
        Path.GetFileName(handle.QuarantinePath);

    private static string BuildQuarantinePath(
        string allowedRoot,
        string category,
        string identity,
        string? extension)
    {
        var safeCategory = SanitizeSegment(category);
        var safeIdentity = SanitizeSegment(identity);
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var name = string.IsNullOrWhiteSpace(extension)
            ? $"{safeIdentity}-{suffix}"
            : $"{safeIdentity}-{suffix}{extension.ToLowerInvariant()}";

        return Path.Combine(
            Path.GetFullPath(allowedRoot),
            QuarantineDirectoryName,
            safeCategory,
            name);
    }

    private static string ValidatePath(string path, string allowedRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A filesystem path is required.", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(allowedRoot))
        {
            throw new ArgumentException("An allowed root path is required.", nameof(allowedRoot));
        }

        var root = Path.GetFullPath(allowedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        var rootPrefix = root + Path.DirectorySeparatorChar;

        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(rootPrefix, pathComparison))
        {
            throw new InvalidOperationException("The requested filesystem operation is outside the configured upload root.");
        }

        var quarantineRoot = Path.Combine(root, QuarantineDirectoryName) + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(quarantineRoot, pathComparison))
        {
            throw new InvalidOperationException("A quarantine path cannot be staged again.");
        }

        return fullPath;
    }

    private static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var characters = (value ?? string.Empty)
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray();
        var sanitized = new string(characters).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "item" : sanitized;
    }

    private static void DeleteEmptyParents(string path)
    {
        var current = Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(current)
               && Directory.Exists(current)
               && string.Equals(Path.GetFileName(current), QuarantineDirectoryName, StringComparison.OrdinalIgnoreCase) == false)
        {
            if (Directory.EnumerateFileSystemEntries(current).Any())
            {
                break;
            }

            Directory.Delete(current);
            current = Path.GetDirectoryName(current);
        }
    }
}
