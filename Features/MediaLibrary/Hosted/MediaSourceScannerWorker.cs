using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

public sealed class MediaSourceScannerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<MediaSourceScannerWorker> _logger;

    public MediaSourceScannerWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MediaLibraryOptions> options,
        ILogger<MediaSourceScannerWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        if (_options.AutoMigrate)
        {
            await MigrateAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Media source scanner cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.IdleDelaySeconds), stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        using (var bootstrapScope = _scopeFactory.CreateScope())
        {
            var bootstrapper = bootstrapScope.ServiceProvider.GetRequiredService<IMediaSourceBootstrapper>();
            await bootstrapper.EnsureConfiguredSourcesAsync(cancellationToken);
        }

        MediaLibrarySource prismSource;
        List<MediaLibrarySource> networkSources;
        using (var queryScope = _scopeFactory.CreateScope())
        {
            var mediaDb = queryScope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
            prismSource = await mediaDb.Sources
                .AsNoTracking()
                .SingleAsync(source => source.Key == MediaSourceBootstrapper.PrismSourceKey, cancellationToken);
            networkSources = await mediaDb.Sources
                .AsNoTracking()
                .Where(source => source.IsEnabled && source.SourceType == MediaLibrarySourceType.NetworkShare)
                .OrderBy(source => source.LastSuccessfulScanAtUtc)
                .ToListAsync(cancellationToken);
        }

        if (IsDue(prismSource, _options.ScanIntervalMinutes))
        {
            using var prismScope = _scopeFactory.CreateScope();
            var synchronizer = prismScope.ServiceProvider.GetRequiredService<IPrismMediaCatalogueSynchronizer>();
            await synchronizer.SynchronizeAsync(cancellationToken);
        }

        foreach (var source in networkSources)
        {
            var configured = _options.Sources.FirstOrDefault(item =>
                string.Equals(MediaSourceBootstrapper.NormalizeKey(item.Key), source.Key, StringComparison.OrdinalIgnoreCase));
            var interval = configured?.ScanIntervalMinutes ?? _options.ScanIntervalMinutes;
            if (!IsDue(source, interval))
            {
                continue;
            }

            using var sourceScope = _scopeFactory.CreateScope();
            var scanner = sourceScope.ServiceProvider.GetRequiredService<INetworkMediaSourceScanner>();
            await scanner.ScanAsync(source.Id, cancellationToken);
        }
    }

    private static bool IsDue(MediaLibrarySource source, int intervalMinutes)
    {
        if (source.ScanRequestedAtUtc.HasValue
            && (!source.LastScanStartedAtUtc.HasValue || source.ScanRequestedAtUtc > source.LastScanStartedAtUtc))
        {
            return true;
        }

        return !source.LastSuccessfulScanAtUtc.HasValue
            || source.LastSuccessfulScanAtUtc.Value.AddMinutes(Math.Max(1, intervalMinutes)) <= DateTimeOffset.UtcNow;
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Media library database migrations applied");
    }
}
