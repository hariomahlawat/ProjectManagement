using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Services;

namespace ProjectManagement.Application.Security;

public class FileSecurityValidator(IVirusScanner? virusScanner = null) : IFileSecurityValidator
{
    private readonly IVirusScanner? _virusScanner = virusScanner;

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
