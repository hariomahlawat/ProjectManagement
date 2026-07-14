using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models.Usage;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Usage;

public enum UserActivitySignal
{
    Navigation = 1,
    InteractiveHeartbeat = 2
}

public interface IUserActivityRecorder
{
    Task RecordAsync(
        string userId,
        string moduleKey,
        UserActivitySignal signal,
        DateTimeOffset? occurredUtc = null,
        CancellationToken cancellationToken = default);
}

public sealed class UserActivityRecorder : IUserActivityRecorder
{
    private const int MaximumCounterValue = 10000;

    private readonly ApplicationDbContext _db;
    private readonly IErpUsageModuleCatalog _modules;
    private readonly IOptions<ErpUsageOptions> _options;
    private readonly IClock _clock;

    public UserActivityRecorder(
        ApplicationDbContext db,
        IErpUsageModuleCatalog modules,
        IOptions<ErpUsageOptions> options,
        IClock? clock = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _modules = modules ?? throw new ArgumentNullException(nameof(modules));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? new SystemClock();
    }

    public async Task RecordAsync(
        string userId,
        string moduleKey,
        UserActivitySignal signal,
        DateTimeOffset? occurredUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || _modules.Find(moduleKey) is null)
        {
            return;
        }

        var now = (occurredUtc ?? _clock.UtcNow).ToUniversalTime();
        var bucketMinutes = _options.Value.BucketMinutes;
        var bucketTicks = TimeSpan.FromMinutes(bucketMinutes).Ticks;
        var utcTicks = now.UtcDateTime.Ticks - (now.UtcDateTime.Ticks % bucketTicks);
        var bucketStartUtc = new DateTime(utcTicks, DateTimeKind.Utc);
        var seenUtc = now.UtcDateTime;
        var activityDateIst = DateOnly.FromDateTime(IstClock.ToIst(now).DateTime);
        var hadNavigation = signal == UserActivitySignal.Navigation;
        var hadHeartbeat = signal == UserActivitySignal.InteractiveHeartbeat;
        var normalizedModule = moduleKey.Trim().ToLowerInvariant();

        if (_db.Database.IsNpgsql())
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "UserActivityBuckets"
                    ("UserId", "BucketStartUtc", "ActivityDateIst", "ModuleKey",
                     "HadNavigation", "HadInteractiveHeartbeat", "FirstSeenUtc", "LastSeenUtc",
                     "NavigationCount", "HeartbeatCount")
                VALUES
                    ({userId}, {bucketStartUtc}, {activityDateIst}, {normalizedModule},
                     {hadNavigation}, {hadHeartbeat}, {seenUtc}, {seenUtc},
                     {(hadNavigation ? 1 : 0)}, {(hadHeartbeat ? 1 : 0)})
                ON CONFLICT ("UserId", "BucketStartUtc", "ModuleKey")
                DO UPDATE SET
                    "HadNavigation" = "UserActivityBuckets"."HadNavigation" OR EXCLUDED."HadNavigation",
                    "HadInteractiveHeartbeat" = "UserActivityBuckets"."HadInteractiveHeartbeat" OR EXCLUDED."HadInteractiveHeartbeat",
                    "FirstSeenUtc" = LEAST("UserActivityBuckets"."FirstSeenUtc", EXCLUDED."FirstSeenUtc"),
                    "LastSeenUtc" = GREATEST("UserActivityBuckets"."LastSeenUtc", EXCLUDED."LastSeenUtc"),
                    "NavigationCount" = LEAST({MaximumCounterValue}, "UserActivityBuckets"."NavigationCount" + EXCLUDED."NavigationCount"),
                    "HeartbeatCount" = LEAST({MaximumCounterValue}, "UserActivityBuckets"."HeartbeatCount" + EXCLUDED."HeartbeatCount");
                """, cancellationToken);
            return;
        }

        var existing = await _db.UserActivityBuckets.SingleOrDefaultAsync(
            bucket =>
                bucket.UserId == userId
                && bucket.BucketStartUtc == bucketStartUtc
                && bucket.ModuleKey == normalizedModule,
            cancellationToken);

        if (existing is null)
        {
            _db.UserActivityBuckets.Add(new UserActivityBucket
            {
                UserId = userId,
                BucketStartUtc = bucketStartUtc,
                ActivityDateIst = activityDateIst,
                ModuleKey = normalizedModule,
                HadNavigation = hadNavigation,
                HadInteractiveHeartbeat = hadHeartbeat,
                FirstSeenUtc = seenUtc,
                LastSeenUtc = seenUtc,
                NavigationCount = hadNavigation ? 1 : 0,
                HeartbeatCount = hadHeartbeat ? 1 : 0
            });
        }
        else
        {
            existing.HadNavigation |= hadNavigation;
            existing.HadInteractiveHeartbeat |= hadHeartbeat;
            existing.FirstSeenUtc = existing.FirstSeenUtc <= seenUtc ? existing.FirstSeenUtc : seenUtc;
            existing.LastSeenUtc = existing.LastSeenUtc >= seenUtc ? existing.LastSeenUtc : seenUtc;
            if (hadNavigation) existing.NavigationCount = Math.Min(MaximumCounterValue, existing.NavigationCount + 1);
            if (hadHeartbeat) existing.HeartbeatCount = Math.Min(MaximumCounterValue, existing.HeartbeatCount + 1);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
