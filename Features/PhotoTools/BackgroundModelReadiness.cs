using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Features.PhotoTools;

public enum BackgroundModelState
{
    Ready,
    Disabled,
    NotConfigured,
    FileMissing,
    ChecksumNotConfigured,
    ChecksumMismatch
}

public sealed record BackgroundModelReadiness(BackgroundModelState State, string? ModelPath)
{
    public bool IsReady => State == BackgroundModelState.Ready;
}

public interface IBackgroundModelReadinessService
{
    BackgroundModelReadiness Check();
}

/// <summary>
/// Says whether the approved model can be used: the tool is on, the file is where the settings
/// say, and its SHA-256 matches. The checksum of a large file is worked out once and kept until
/// the file changes. Nothing here stops PRISM from starting; a missing model only makes the tool
/// say it is not set up.
/// </summary>
public sealed class BackgroundModelReadinessService : IBackgroundModelReadinessService
{
    private readonly IOptionsMonitor<BackgroundRemovalOptions> _options;
    private readonly IWebHostEnvironment _environment;
    private readonly object _gate = new();
    private (string Path, long Length, DateTime Written, string Hash)? _checked;

    public BackgroundModelReadinessService(
        IOptionsMonitor<BackgroundRemovalOptions> options,
        IWebHostEnvironment environment)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public BackgroundModelReadiness Check()
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return new(BackgroundModelState.Disabled, null);
        }

        if (string.IsNullOrWhiteSpace(options.ModelRoot) || string.IsNullOrWhiteSpace(options.Model.FileName))
        {
            return new(BackgroundModelState.NotConfigured, null);
        }

        var root = Path.IsPathRooted(options.ModelRoot)
            ? options.ModelRoot
            : Path.Combine(_environment.ContentRootPath, options.ModelRoot);
        var path = Path.GetFullPath(Path.Combine(root, Path.GetFileName(options.Model.FileName)));
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return new(BackgroundModelState.FileMissing, path);
        }

        var expected = options.Model.Sha256?.Trim().ToLowerInvariant() ?? string.Empty;
        if (expected.Length != 64)
        {
            return new(BackgroundModelState.ChecksumNotConfigured, path);
        }

        var actual = HashOf(file);
        return actual == expected
            ? new(BackgroundModelState.Ready, path)
            : new(BackgroundModelState.ChecksumMismatch, path);
    }

    private string HashOf(FileInfo file)
    {
        lock (_gate)
        {
            if (_checked is { } known
                && known.Path == file.FullName
                && known.Length == file.Length
                && known.Written == file.LastWriteTimeUtc)
            {
                return known.Hash;
            }

            using var stream = file.OpenRead();
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            _checked = (file.FullName, file.Length, file.LastWriteTimeUtc, hash);
            return hash;
        }
    }
}
