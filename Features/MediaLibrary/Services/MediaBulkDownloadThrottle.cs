using System.Collections.Concurrent;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Bounds concurrent Photos ZIP builds. Each build reads up to MaxSourceBytes and writes an
/// archive of similar size to the media cache volume, which derivatives and face crops also
/// use. Without a bound, a few concurrent requests could fill that volume. One build per user
/// at a time, and a small number in total, per application instance.
/// </summary>
public sealed class MediaBulkDownloadThrottle
{
    private readonly SemaphoreSlim _global;
    private readonly ConcurrentDictionary<string, byte> _activeUsers = new(StringComparer.Ordinal);

    public MediaBulkDownloadThrottle(int maximumConcurrentArchives)
        => _global = new SemaphoreSlim(Math.Clamp(maximumConcurrentArchives, 1, 16));

    /// <summary>Process-wide default used when no instance is supplied.</summary>
    public static MediaBulkDownloadThrottle Shared { get; } = new(2);

    public bool TryEnter(string userKey, out IDisposable? lease)
    {
        lease = null;
        if (!_activeUsers.TryAdd(userKey, 0))
        {
            return false;
        }

        if (!_global.Wait(0))
        {
            _activeUsers.TryRemove(userKey, out _);
            return false;
        }

        lease = new Lease(this, userKey);
        return true;
    }

    private void Release(string userKey)
    {
        _activeUsers.TryRemove(userKey, out _);
        _global.Release();
    }

    private sealed class Lease(MediaBulkDownloadThrottle owner, string userKey) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(userKey);
            }
        }
    }
}
