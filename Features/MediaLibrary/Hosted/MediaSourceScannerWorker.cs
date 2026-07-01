using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;

namespace ProjectManagement.Features.MediaLibrary.Hosted;

/// <summary>
/// Coordinates PRISM catalogue synchronisation and optional external-folder scans.
/// Failures are isolated from the web host and from other sources.
/// </summary>
public sealed class MediaSourceScannerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MediaLibraryOptions _options;
    private readonly ILogger<MediaSourceScannerWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private bool _bootstrapCompleted;

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
            catch (Exception ex) when (IsCatalogueInfrastructureFailure(ex))
            {
                _logger.LogWarning(ex,
                    "Media catalogue is unavailable. PRISM Photos remains operational without catalogue-backed items.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Media source scanner cycle failed");
            }

            var delay = GetCycleDelay();
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        using (var readinessScope = _scopeFactory.CreateScope())
        {
            var schema = readinessScope.ServiceProvider.GetRequiredService<IMediaLibrarySchemaService>();
            var status = await schema.GetStatusAsync(cancellationToken);
            if (!status.IsAvailable || !status.IsOperational)
            {
                _logger.LogInformation(
                    "Media source scanner is waiting for an operational catalogue schema. Reference={Reference}",
                    status.DiagnosticReference);
                return;
            }
        }

        if (!_bootstrapCompleted)
        {
            using var bootstrapScope = _scopeFactory.CreateScope();
            var bootstrapper = bootstrapScope.ServiceProvider.GetRequiredService<IMediaSourceBootstrapper>();
            await bootstrapper.EnsureConfiguredSourcesAsync(cancellationToken);
            _bootstrapCompleted = true;
        }

        MediaLibrarySource? prismSource = null;
        List<MediaLibrarySource> externalSources = new();
        using (var queryScope = _scopeFactory.CreateScope())
        {
            var mediaDb = queryScope.ServiceProvider.GetRequiredService<MediaLibraryDbContext>();
            if (_options.Catalogue.SynchronizePrismMedia)
            {
                prismSource = await mediaDb.Sources
                    .AsNoTracking()
                    .SingleOrDefaultAsync(source => source.Key == MediaSourceBootstrapper.PrismSourceKey,
                        cancellationToken);
            }

            if (_options.IsScannerWorkerEnabled)
            {
                externalSources = await mediaDb.Sources
                    .AsNoTracking()
                    .Where(source => source.IsEnabled
                                     && !source.IsDeleted
                                     && source.SourceType == MediaLibrarySourceType.FileSystem)
                    .OrderBy(source => source.LastSuccessfulScanAtUtc)
                    .ToListAsync(cancellationToken);
            }
        }

        if (prismSource is not null
            && IsDue(prismSource, _options.Catalogue.GetSynchronizeInterval()))
        {
            using var prismScope = _scopeFactory.CreateScope();
            var synchronizer = prismScope.ServiceProvider.GetRequiredService<IPrismMediaCatalogueSynchronizer>();
            try
            {
                await synchronizer.SynchronizeAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PRISM media catalogue synchronisation failed");
            }
        }

        foreach (var source in externalSources)
        {
            if (!IsDue(source, TimeSpan.FromMinutes(Math.Max(1, source.ScanIntervalMinutes))))
            {
                continue;
            }

            using var sourceScope = _scopeFactory.CreateScope();
            var scanner = sourceScope.ServiceProvider.GetRequiredService<IExternalMediaSourceScanner>();
            await scanner.ScanAsync(source.Id, _workerId, cancellationToken);
        }
    }

    private TimeSpan GetCycleDelay()
    {
        var candidates = new List<TimeSpan>();

        if (_options.Catalogue.SynchronizePrismMedia)
        {
            candidates.Add(_options.Catalogue.GetSynchronizeInterval());
        }

        if (_options.IsExternalSourceFeatureEnabled)
        {
            candidates.Add(TimeSpan.FromSeconds(Math.Max(1, _options.ExternalSources.IdleDelaySeconds)));
        }

        return candidates.Count == 0
            ? TimeSpan.FromSeconds(60)
            : candidates.Min();
    }

    private static bool IsDue(MediaLibrarySource source, TimeSpan interval)
    {
        if (source.ScanRequestedAtUtc.HasValue
            && (!source.LastScanStartedAtUtc.HasValue || source.ScanRequestedAtUtc > source.LastScanStartedAtUtc))
        {
            return true;
        }

        var lastAttempt = source.LastSuccessfulScanAtUtc
                          ?? source.LastScanCompletedAtUtc
                          ?? source.LastScanStartedAtUtc;
        return !lastAttempt.HasValue
               || lastAttempt.Value.Add(interval) <= DateTimeOffset.UtcNow;
    }

    private static bool IsCatalogueInfrastructureFailure(Exception exception)
        => exception is NpgsqlException
            or DbUpdateException
            or InvalidOperationException
            or TimeoutException;
}
