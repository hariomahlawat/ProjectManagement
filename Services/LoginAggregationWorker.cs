using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;

namespace ProjectManagement.Services;

public sealed class LoginAggregationWorker : BackgroundService
{
    private const string WorkerKey = "login-aggregation";

    private readonly IServiceProvider _services;
    private readonly ILogger<LoginAggregationWorker> _logger;
    private readonly IAdminWorkerStatusRegistry? _status;

    public LoginAggregationWorker(
        IServiceProvider services,
        ILogger<LoginAggregationWorker> logger,
        IAdminWorkerStatusRegistry? status = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _status = status;
        _status?.Register(WorkerKey, "Login aggregation");
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
            _logger.LogError(exception, "Login aggregation worker failed.");
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        _status?.MarkStarted(WorkerKey);
        try
        {
            var result = await AggregateAsync(cancellationToken);
            _status?.MarkSucceeded(WorkerKey, result);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _status?.MarkFailed(WorkerKey, exception);
            throw;
        }
    }

    private async Task<string> AggregateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var exists = await db.DailyLoginStats.AnyAsync(row => row.Date == date, cancellationToken);
        if (exists) return $"Daily aggregate for {date:yyyy-MM-dd} already exists.";

        var from = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var count = await db.AuthEvents
            .Where(authEvent =>
                authEvent.Event == AuthenticationEventNames.LoginSucceeded
                && authEvent.WhenUtc >= from
                && authEvent.WhenUtc < to)
            .CountAsync(cancellationToken);

        db.DailyLoginStats.Add(new DailyLoginStat { Date = date, Count = count });
        await db.SaveChangesAsync(cancellationToken);
        return $"Stored {count} successful sign-in(s) for {date:yyyy-MM-dd}.";
    }
}
