using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Usage;

public sealed class UserActivityRetentionWorker : BackgroundService
{
    private const string WorkerKey = "erp-usage-retention";
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);
    private const int BatchSize = 5000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ErpUsageOptions> _options;
    private readonly ILogger<UserActivityRetentionWorker> _logger;
    private readonly IAdminWorkerStatusRegistry? _status;
    private readonly IClock _clock;

    public UserActivityRetentionWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ErpUsageOptions> options,
        ILogger<UserActivityRetentionWorker> logger,
        IAdminWorkerStatusRegistry? status = null,
        IClock? clock = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _status = status;
        _clock = clock ?? new SystemClock();
        _status?.Register(WorkerKey, "ERP usage detail retention", SweepInterval);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _status?.MarkStarted(WorkerKey);
                var removed = await RunOnceAsync(stoppingToken);
                _status?.MarkSucceeded(WorkerKey, $"Removed {removed} expired detailed activity bucket(s); permanent daily summaries were retained.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _status?.MarkFailed(WorkerKey, exception);
                _logger.LogError(exception, "ERP usage detail retention failed; permanent daily summaries and application requests are unaffected.");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var retentionDays = Math.Clamp(_options.CurrentValue.RetentionDays, 30, 1095);
        var cutoff = _clock.UtcNow.UtcDateTime.AddDays(-retentionDays);
        var removedTotal = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ids = await db.UserActivityBuckets
                .AsNoTracking()
                .Where(bucket => bucket.BucketStartUtc < cutoff)
                .OrderBy(bucket => bucket.BucketStartUtc)
                .ThenBy(bucket => bucket.Id)
                .Select(bucket => bucket.Id)
                .Take(BatchSize)
                .ToArrayAsync(cancellationToken);

            if (ids.Length == 0)
            {
                break;
            }

            int removed;
            if (db.Database.IsRelational())
            {
                removed = await db.UserActivityBuckets
                    .Where(bucket => ids.Contains(bucket.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                var entities = await db.UserActivityBuckets
                    .Where(bucket => ids.Contains(bucket.Id))
                    .ToListAsync(cancellationToken);
                db.UserActivityBuckets.RemoveRange(entities);
                removed = await db.SaveChangesAsync(cancellationToken);
            }

            removedTotal += removed;
            if (ids.Length < BatchSize)
            {
                break;
            }
        }

        return removedTotal;
    }
}
