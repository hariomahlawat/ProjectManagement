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
    private const int MaximumBucketCounterValue = 10000;
    private const int MaximumDailyCounterValue = 1000000;

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
        var navigationIncrement = hadNavigation ? 1 : 0;
        var heartbeatIncrement = hadHeartbeat ? 1 : 0;
        var normalizedModule = moduleKey.Trim().ToLowerInvariant();

        if (_db.Database.IsNpgsql())
        {
            // The detailed bucket supports module/time analytics for the bounded retention
            // period. The daily summary is retained permanently for personal year views.
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "UserActivityBuckets"
                    ("UserId", "BucketStartUtc", "ActivityDateIst", "ModuleKey",
                     "HadNavigation", "HadInteractiveHeartbeat", "FirstSeenUtc", "LastSeenUtc",
                     "NavigationCount", "HeartbeatCount")
                VALUES
                    ({userId}, {bucketStartUtc}, {activityDateIst}, {normalizedModule},
                     {hadNavigation}, {hadHeartbeat}, {seenUtc}, {seenUtc},
                     {navigationIncrement}, {heartbeatIncrement})
                ON CONFLICT ("UserId", "BucketStartUtc", "ModuleKey")
                DO UPDATE SET
                    "HadNavigation" = "UserActivityBuckets"."HadNavigation" OR EXCLUDED."HadNavigation",
                    "HadInteractiveHeartbeat" = "UserActivityBuckets"."HadInteractiveHeartbeat" OR EXCLUDED."HadInteractiveHeartbeat",
                    "FirstSeenUtc" = LEAST("UserActivityBuckets"."FirstSeenUtc", EXCLUDED."FirstSeenUtc"),
                    "LastSeenUtc" = GREATEST("UserActivityBuckets"."LastSeenUtc", EXCLUDED."LastSeenUtc"),
                    "NavigationCount" = LEAST({MaximumBucketCounterValue}, "UserActivityBuckets"."NavigationCount"::bigint + EXCLUDED."NavigationCount")::integer,
                    "HeartbeatCount" = LEAST({MaximumBucketCounterValue}, "UserActivityBuckets"."HeartbeatCount"::bigint + EXCLUDED."HeartbeatCount")::integer;

                INSERT INTO "UserActivityDailySummaries"
                    ("UserId", "ActivityDateIst", "HadNavigation", "HadInteractiveHeartbeat",
                     "HadAdministrativeAction", "HadOperationalAction",
                     "FirstSeenUtc", "LastSeenUtc", "NavigationCount", "HeartbeatCount",
                     "AdministrativeActionCount", "OperationalActionCount")
                VALUES
                    ({userId}, {activityDateIst}, {hadNavigation}, {hadHeartbeat},
                     FALSE, FALSE, {seenUtc}, {seenUtc}, {navigationIncrement}, {heartbeatIncrement},
                     0, 0)
                ON CONFLICT ("UserId", "ActivityDateIst")
                DO UPDATE SET
                    "HadNavigation" = "UserActivityDailySummaries"."HadNavigation" OR EXCLUDED."HadNavigation",
                    "HadInteractiveHeartbeat" = "UserActivityDailySummaries"."HadInteractiveHeartbeat" OR EXCLUDED."HadInteractiveHeartbeat",
                    "FirstSeenUtc" = LEAST("UserActivityDailySummaries"."FirstSeenUtc", EXCLUDED."FirstSeenUtc"),
                    "LastSeenUtc" = GREATEST("UserActivityDailySummaries"."LastSeenUtc", EXCLUDED."LastSeenUtc"),
                    "NavigationCount" = LEAST({MaximumDailyCounterValue}, "UserActivityDailySummaries"."NavigationCount"::bigint + EXCLUDED."NavigationCount")::integer,
                    "HeartbeatCount" = LEAST({MaximumDailyCounterValue}, "UserActivityDailySummaries"."HeartbeatCount"::bigint + EXCLUDED."HeartbeatCount")::integer;
                """, cancellationToken);
            return;
        }

        var existingBucket = await _db.UserActivityBuckets.SingleOrDefaultAsync(
            bucket =>
                bucket.UserId == userId
                && bucket.BucketStartUtc == bucketStartUtc
                && bucket.ModuleKey == normalizedModule,
            cancellationToken);

        if (existingBucket is null)
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
                NavigationCount = navigationIncrement,
                HeartbeatCount = heartbeatIncrement
            });
        }
        else
        {
            MergeBucket(existingBucket, hadNavigation, hadHeartbeat, seenUtc);
        }

        var dailySummary = await _db.UserActivityDailySummaries.SingleOrDefaultAsync(
            summary => summary.UserId == userId && summary.ActivityDateIst == activityDateIst,
            cancellationToken);

        if (dailySummary is null)
        {
            _db.UserActivityDailySummaries.Add(new UserActivityDailySummary
            {
                UserId = userId,
                ActivityDateIst = activityDateIst,
                HadNavigation = hadNavigation,
                HadInteractiveHeartbeat = hadHeartbeat,
                FirstSeenUtc = seenUtc,
                LastSeenUtc = seenUtc,
                NavigationCount = navigationIncrement,
                HeartbeatCount = heartbeatIncrement
            });
        }
        else
        {
            dailySummary.HadNavigation |= hadNavigation;
            dailySummary.HadInteractiveHeartbeat |= hadHeartbeat;
            dailySummary.FirstSeenUtc = dailySummary.FirstSeenUtc <= seenUtc ? dailySummary.FirstSeenUtc : seenUtc;
            dailySummary.LastSeenUtc = dailySummary.LastSeenUtc >= seenUtc ? dailySummary.LastSeenUtc : seenUtc;
            if (hadNavigation)
            {
                dailySummary.NavigationCount = Math.Min(MaximumDailyCounterValue, dailySummary.NavigationCount + 1);
            }
            if (hadHeartbeat)
            {
                dailySummary.HeartbeatCount = Math.Min(MaximumDailyCounterValue, dailySummary.HeartbeatCount + 1);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void MergeBucket(
        UserActivityBucket bucket,
        bool hadNavigation,
        bool hadHeartbeat,
        DateTime seenUtc)
    {
        bucket.HadNavigation |= hadNavigation;
        bucket.HadInteractiveHeartbeat |= hadHeartbeat;
        bucket.FirstSeenUtc = bucket.FirstSeenUtc <= seenUtc ? bucket.FirstSeenUtc : seenUtc;
        bucket.LastSeenUtc = bucket.LastSeenUtc >= seenUtc ? bucket.LastSeenUtc : seenUtc;
        if (hadNavigation)
        {
            bucket.NavigationCount = Math.Min(MaximumBucketCounterValue, bucket.NavigationCount + 1);
        }
        if (hadHeartbeat)
        {
            bucket.HeartbeatCount = Math.Min(MaximumBucketCounterValue, bucket.HeartbeatCount + 1);
        }
    }
}
