using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Infrastructure;

// Server-wide allowances shared by every client: one for sign-in attempts, one for document
// uploads. They bound the total even when clients forge X-Forwarded-For to get a fresh
// per-client partition, and they are only consulted after the per-client or per-user
// allowance passes, so one throttled client cannot use up everyone else's share.
public sealed class SharedRateLimiters : IDisposable
{
    public SharedRateLimiters(IOptions<LoginRateLimitOptions> login, IOptions<DocumentUploadRateLimitOptions> upload)
    {
        Login = Create(login.Value);
        DocumentUpload = Create(upload.Value);
    }

    public RateLimiter Login { get; }

    public RateLimiter DocumentUpload { get; }

    private static RateLimiter Create(RateLimitWindowOptions options) => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
    {
        PermitLimit = options.EffectiveServerPermitLimit,
        Window = options.EffectiveWindow,
        QueueLimit = 0,
        AutoReplenishment = true
    });

    public void Dispose()
    {
        Login.Dispose();
        DocumentUpload.Dispose();
    }
}

// Takes a permit from a partition's own limiter first and only then from the shared limiter.
// The shared limiter belongs to SharedRateLimiters and is never disposed here.
public sealed class GatedRateLimiter : RateLimiter
{
    private readonly RateLimiter _own;
    private readonly RateLimiter _shared;

    public GatedRateLimiter(RateLimiter own, RateLimiter shared)
    {
        _own = own;
        _shared = shared;
    }

    public override TimeSpan? IdleDuration => _own.IdleDuration;

    public override RateLimiterStatistics? GetStatistics() => _own.GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var own = _own.AttemptAcquire(permitCount);
        if (!own.IsAcquired)
        {
            return own;
        }

        var shared = _shared.AttemptAcquire(permitCount);
        if (!shared.IsAcquired)
        {
            own.Dispose();
            return shared;
        }

        shared.Dispose();
        return own;
    }

    // Nothing queues (QueueLimit is 0). The rate-limiting middleware calls this after a failed
    // AttemptAcquire; answering without taking permits keeps a rejection from counting twice.
    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        => ValueTask.FromResult<RateLimitLease>(RejectedLease.Instance);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _own.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override ValueTask DisposeAsyncCore() => _own.DisposeAsync();

    private sealed class RejectedLease : RateLimitLease
    {
        public static readonly RejectedLease Instance = new();

        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => Array.Empty<string>();

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
