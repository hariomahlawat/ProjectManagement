using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectRetentionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<ProjectRetentionOptions> _options;
    private readonly ILogger<ProjectRetentionWorker> _logger;
    private readonly IClock _clock;

    public ProjectRetentionWorker(
        IServiceProvider serviceProvider,
        IOptions<ProjectRetentionOptions> options,
        ILogger<ProjectRetentionWorker> logger,
        IClock clock)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromHours(24);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Swallow cancellation during shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute project retention job");
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

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var retentionDays = Math.Max(0, _options.Value.TrashRetentionDays);
        var removeAssets = _options.Value.RemoveAssetsOnPurge;
        var cutoff = _clock.UtcNow.AddDays(-retentionDays);

        using var scope = _serviceProvider.CreateScope();
        var moderation = scope.ServiceProvider.GetRequiredService<ProjectModerationService>();
        var purged = await moderation.PurgeExpiredAsync(cutoff, removeAssets, cancellationToken);
        if (purged > 0)
        {
            _logger.LogInformation("Purged {Count} projects from Trash", purged);
        }
    }
}
