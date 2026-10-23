using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.Chat;
using ProjectManagement.Hubs;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Chat;

namespace ProjectManagement.Hosted;

/// <summary>
/// Chat attachments clean-up (docs/chat-attachments.md): once a day, retention removes sent
/// files older than the retention age; every sweep removes uploads never sent and deletes the
/// files of removed attachments once they can no longer be restored. Each sweep only acts on what is still to do, so a sweep that runs twice,
/// or overlaps another during a restart, does no harm.
/// </summary>
public sealed class ChatAttachmentCleanupWorker : BackgroundService
{
    private const string WorkerKey = "chat-attachment-cleanup";
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ChatOptions> _options;
    private readonly ILogger<ChatAttachmentCleanupWorker> _logger;
    private readonly IAdminWorkerStatusRegistry? _status;
    private DateTimeOffset _lastRetention = DateTimeOffset.MinValue;

    public ChatAttachmentCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ChatOptions> options,
        ILogger<ChatAttachmentCleanupWorker> logger,
        IAdminWorkerStatusRegistry? status = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _status = status;
        _status?.Register(WorkerKey, "Chat attachment clean-up", Interval);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let start-up (migrations, seeding) finish first.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.CurrentValue.Attachments.Enabled)
                {
                    _status?.MarkStarted(WorkerKey);
                    using var scope = _scopeFactory.CreateScope();
                    // Retention once a day (and once after each start): files older than the
                    // retention age are removed as one batch, undoable for a day, owners told.
                    if (DateTimeOffset.UtcNow - _lastRetention >= RetentionInterval)
                    {
                        var retention = await scope.ServiceProvider.GetRequiredService<ChatStorageService>().ApplyRetentionAsync(stoppingToken);
                        await ChatApi.PushFileChangesAsync(scope.ServiceProvider.GetRequiredService<IHubContext<ChatHub, IChatClient>>(), retention.Updates);
                        _lastRetention = DateTimeOffset.UtcNow;
                    }

                    var deleted = await scope.ServiceProvider.GetRequiredService<ChatAttachmentService>().CleanUpAsync(stoppingToken);
                    if (deleted > 0)
                    {
                        _logger.LogInformation("Deleted {Count} removed chat attachment file(s).", deleted);
                    }

                    _status?.MarkSucceeded(WorkerKey, $"Deleted {deleted} chat attachment file(s).");
                }

                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _status?.MarkFailed(WorkerKey, ex);
                _logger.LogError(ex, "Chat attachment clean-up failed; it will run again.");
                await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }
}
