using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Services;

public sealed class TodoPurgeWorker : BackgroundService
{
    private const string WorkerKey = "todo-retention";

    private readonly IServiceProvider _services;
    private readonly ILogger<TodoPurgeWorker> _logger;
    private readonly int _retentionDays;
    private readonly IAdminWorkerStatusRegistry? _status;
    private bool _loggedMissingDeletedUtc;

    public TodoPurgeWorker(
        IServiceProvider services,
        ILogger<TodoPurgeWorker> logger,
        IOptions<TodoOptions> options,
        IAdminWorkerStatusRegistry? status = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _retentionDays = options?.Value.RetentionDays ?? throw new ArgumentNullException(nameof(options));
        _status = status;
        _status?.Register(WorkerKey, "Task retention");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
            await RunCycleAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunCycleAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        catch (Exception exception)
        {
            _status?.MarkFailed(WorkerKey, exception);
            _logger.LogError(exception, "Todo purge worker failed.");
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        _status?.MarkStarted(WorkerKey);
        try
        {
            var result = await PurgeAsync(cancellationToken);
            _status?.MarkSucceeded(WorkerKey, result);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _status?.MarkFailed(WorkerKey, exception);
            throw;
        }
    }

    private async Task<string> PurgeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var columnCount = await db.Database
            .SqlQueryRaw<long>("SELECT COUNT(*) AS \"Value\" FROM information_schema.columns WHERE table_name='TodoItems' AND column_name='DeletedUtc'")
            .SingleAsync(cancellationToken);

        if (columnCount == 0)
        {
            if (!_loggedMissingDeletedUtc)
            {
                _loggedMissingDeletedUtc = true;
                _logger.LogWarning("TodoItems.DeletedUtc missing; skipping purge.");
            }
            return "Skipped because TodoItems.DeletedUtc is unavailable.";
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays);
        var deleted = await db.TodoItems
            .Where(todo => todo.DeletedUtc != null && todo.DeletedUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0) _logger.LogInformation("Purged {Count} todo items", deleted);
        return $"Purged {deleted} task record(s).";
    }
}
