using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Services;

namespace ProjectManagement.Application.Security;

public class FileSecurityValidator(IVirusScanner? virusScanner = null) : IFileSecurityValidator
{
    private readonly IVirusScanner? _virusScanner = virusScanner;

    // Section: Relative path validation
    public void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path is required.", nameof(relativePath));
        }

        var normalizedPath = relativePath.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalizedPath))
        {
            throw new ArgumentException("Relative path must not be rooted.", nameof(relativePath));
        }

        var segments = normalizedPath.Split('/', System.StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException("Relative path must contain at least one segment.", nameof(relativePath));
        }

        foreach (var segment in segments)
        {
            if (segment == "." || segment == "..")
            {
                throw new ArgumentException("Relative path contains invalid traversal segments.", nameof(relativePath));
            }
        }
    }

    // Section: File content validation
    public async Task<bool> IsSafeAsync(string filePath, string contentType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("File to validate was not found.", filePath);
        }

        if (_virusScanner is null)
        {
            return true;
        }

        await using var stream = File.OpenRead(filePath);
        await _virusScanner.ScanAsync(stream, Path.GetFileName(filePath), cancellationToken);
        return true;
    }
}
