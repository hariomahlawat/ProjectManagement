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
    ChecksumMismatch,
    /// <summary>The model's checksum is being worked out (about a minute for a 1 GB file).</summary>
    Checking,
    /// <summary>The file is there but cannot be read, for example while it is being copied.</summary>
    FileUnreadable,
    /// <summary>The file passed its checksum but could not be loaded as the settings describe.</summary>
    ModelInvalid,
    /// <summary>ONNX Runtime's native library cannot be loaded on this server.</summary>
    RuntimeMissing
}

/// <summary>
/// Whether the model can be used and, when it can, which file was approved: its path, and the size
/// and time of the file whose SHA-256 was checked, so the model loader can refuse a file swapped in
/// after the check.
/// </summary>
public sealed record BackgroundModelReadiness(
    BackgroundModelState State,
    string? ModelPath,
    long Length = 0,
    DateTime LastWriteUtc = default)
{
    public bool IsReady => State == BackgroundModelState.Ready;
}

public interface IBackgroundModelReadinessService
{
    BackgroundModelReadiness Check();
}

/// <summary>
/// Says whether the approved model can be used: the tool is on, the file is where the settings
/// say, and its SHA-256 matches. The checksum is worked out once and kept until the file changes.
/// A large file (the 1 GB model) is hashed in the background: meanwhile the state is
/// <see cref="BackgroundModelState.Checking"/>, so no request waits a minute on a lock. A file that
/// cannot be read (still being copied) is <see cref="BackgroundModelState.FileUnreadable"/> and is
/// tried again on a later check. Nothing here stops PRISM from starting; a missing model only makes
/// the tool say it is not set up.
/// </summary>
public sealed class BackgroundModelReadinessService : IBackgroundModelReadinessService
{
    private static readonly TimeSpan UnreadableRetry = TimeSpan.FromSeconds(30);

    private readonly IOptionsMonitor<BackgroundRemovalOptions> _options;
    private readonly IWebHostEnvironment _environment;
    private readonly object _gate = new();
    private (string Path, long Length, DateTime Written, string Hash)? _checked;
    private (string Path, long Length, DateTime Written)? _hashing;
    private (string Path, long Length, DateTime Written, DateTime FailedUtc)? _unreadable;

    /// <summary>Files up to this size are hashed at once, larger ones in the background.</summary>
    internal long SynchronousHashLimitBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>The background hash, for tests to wait on.</summary>
    internal Task? HashingTask { get; private set; }

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

        var key = (file.FullName, file.Length, file.LastWriteTimeUtc);
        string? actual;
        lock (_gate)
        {
            if (_checked is { } known && (known.Path, known.Length, known.Written) == key)
            {
                actual = known.Hash;
            }
            else if (_hashing == key)
            {
                return new(BackgroundModelState.Checking, path);
            }
            else if (_unreadable is { } failed
                && (failed.Path, failed.Length, failed.Written) == key
                && DateTime.UtcNow - failed.FailedUtc < UnreadableRetry)
            {
                return new(BackgroundModelState.FileUnreadable, path);
            }
            else if (file.Length > SynchronousHashLimitBytes)
            {
                _hashing = key;
                HashingTask = Task.Run(() => HashInBackground(file.FullName, key));
                return new(BackgroundModelState.Checking, path);
            }
            else
            {
                actual = TryHash(file.FullName, key);
                if (actual is null)
                {
                    return new(BackgroundModelState.FileUnreadable, path);
                }
            }
        }

        return actual == expected
            ? new(BackgroundModelState.Ready, path, key.Length, key.LastWriteTimeUtc)
            : new(BackgroundModelState.ChecksumMismatch, path);
    }

    private void HashInBackground(string path, (string, long, DateTime) key)
    {
        var hash = TryHash(path, key);
        lock (_gate)
        {
            if (_hashing == key)
            {
                _hashing = null;
            }

            if (hash is not null)
            {
                _checked = (key.Item1, key.Item2, key.Item3, hash);
            }
        }
    }

    /// <summary>The file's SHA-256, or null (remembered for a short while) when it cannot be read.</summary>
    private string? TryHash(string path, (string Path, long Length, DateTime Written) key)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            lock (_gate)
            {
                _checked = (key.Path, key.Length, key.Written, hash);
                _unreadable = null;
            }

            return hash;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            lock (_gate)
            {
                _unreadable = (key.Path, key.Length, key.Written, DateTime.UtcNow);
            }

            return null;
        }
    }
}
