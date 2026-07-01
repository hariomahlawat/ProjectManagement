namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Short-lived process cache for the comparatively expensive physical media-schema probe.
/// Startup migration remains authoritative; this cache only prevents every worker cycle from
/// repeating the same information-schema and representative-query checks.
/// </summary>
public sealed class MediaLibrarySchemaStatusCache
{
    private static readonly TimeSpan SuccessLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FailureLifetime = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private MediaLibrarySchemaStatus? _status;
    private DateTimeOffset _expiresAtUtc;

    public bool TryGet(out MediaLibrarySchemaStatus status)
    {
        lock (_gate)
        {
            if (_status is not null && DateTimeOffset.UtcNow < _expiresAtUtc)
            {
                status = _status;
                return true;
            }

            status = null!;
            return false;
        }
    }

    public void Store(MediaLibrarySchemaStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        lock (_gate)
        {
            _status = status;
            _expiresAtUtc = DateTimeOffset.UtcNow.Add(
                status.IsCurrent ? SuccessLifetime : FailureLifetime);
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _status = null;
            _expiresAtUtc = DateTimeOffset.MinValue;
        }
    }
}
