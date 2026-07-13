using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectRetentionWorker : BackgroundService
{
    private const string WorkerKey = "project-retention";

    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<ProjectRetentionOptions> _options;
    private readonly ILogger<ProjectRetentionWorker> _logger;
    private readonly IClock _clock;
    private readonly IAdminWorkerStatusRegistry? _status;

    public ProjectRetentionWorker(
        IServiceProvider serviceProvider,
        IOptions<ProjectRetentionOptions> options,
        ILogger<ProjectRetentionWorker> logger,
        IClock clock,
        IAdminWorkerStatusRegistry? status = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _status = status;
        _status?.Register(WorkerKey, "Project trash retention");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromHours(24);
        while (!stoppingToken.IsCancellationRequested)
        {
            _status?.MarkStarted(WorkerKey);
            try
            {
                var purged = await RunOnceAsync(stoppingToken);
                _status?.MarkSucceeded(WorkerKey, $"Purged {purged} project record(s).");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _status?.MarkFailed(WorkerKey, exception);
                _logger.LogError(exception, "Failed to execute project retention job");
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

    private async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var retentionDays = Math.Max(0, _options.Value.TrashRetentionDays);
        var cutoff = _clock.UtcNow.AddDays(-retentionDays);

        using var scope = _serviceProvider.CreateScope();
        var moderation = scope.ServiceProvider.GetRequiredService<ProjectModerationService>();
        var purged = await moderation.PurgeExpiredAsync(
            cutoff,
            _options.Value.RemoveAssetsOnPurge,
            cancellationToken);
        if (purged > 0) _logger.LogInformation("Purged {Count} projects from Trash", purged);
        return purged;
    }
}
