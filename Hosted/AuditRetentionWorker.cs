using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Hosted;

/// <summary>
/// Applies an explicitly enabled audit-retention policy in bounded batches. The worker is
/// intentionally independent of startup migration and does nothing unless an administrator
/// enables Audit:Retention:Enabled in configuration.
/// </summary>
public sealed class AuditRetentionWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AuditRetentionOptions> _options;
    private readonly IClock _clock;
    private readonly ILogger<AuditRetentionWorker> _logger;

    public AuditRetentionWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AuditRetentionOptions> options,
        IClock clock,
        ILogger<AuditRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            var options = _options.CurrentValue;
            var delay = options.GetSafeSweepInterval();

            try
            {
                if (options.Enabled)
                {
                    var removed = await RunOnceAsync(options, stoppingToken);
                    if (removed > 0)
                    {
                        _logger.LogWarning(
                            "Audit retention removed {Count} record(s) older than {RetentionDays} days under the configured governance policy.",
                            removed,
                            options.RetentionDays);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Audit retention enforcement failed; no startup operation is affected.");
                delay = TimeSpan.FromMinutes(30);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<int> RunOnceAsync(
        AuditRetentionOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return 0;
        }

        if (options.RetentionDays < 30)
        {
            throw new InvalidOperationException(
                "Audit retention below 30 days is blocked. Increase Audit:Retention:RetentionDays or disable the policy.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cutoff = _clock.UtcNow.UtcDateTime.AddDays(-options.RetentionDays);
        var batchSize = options.GetSafeBatchSize();

        var ids = await db.AuditLogs
            .AsNoTracking()
            .Where(audit => audit.TimeUtc < cutoff)
            .OrderBy(audit => audit.TimeUtc)
            .ThenBy(audit => audit.Id)
            .Select(audit => audit.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        if (ids.Length == 0)
        {
            return 0;
        }

        return await db.AuditLogs
            .Where(audit => ids.Contains(audit.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
