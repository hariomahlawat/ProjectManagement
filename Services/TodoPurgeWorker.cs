using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services
{
    public class TodoPurgeWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<TodoPurgeWorker> _log;
        private readonly int _retentionDays;
        private bool _loggedMissingDeletedUtc;

        public TodoPurgeWorker(IServiceProvider sp, ILogger<TodoPurgeWorker> log, IOptions<TodoOptions> options)
        {
            _sp = sp;
            _log = log;
            _retentionDays = options.Value.RetentionDays;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

                await PurgeAsync(stoppingToken);

                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await PurgeAsync(stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                // Allow graceful shutdown
            }
            catch (OperationCanceledException)
            {
                // Allow graceful shutdown
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Todo purge worker failed.");
            }
        }

        private async Task PurgeAsync(CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var columnCount = await db.Database
                .SqlQueryRaw<long>("SELECT COUNT(*) FROM information_schema.columns WHERE table_name='TodoItems' AND column_name='DeletedUtc'")
                .SingleAsync(stoppingToken);
            var hasDeletedUtc = columnCount > 0;

            if (!hasDeletedUtc)
            {
                if (!_loggedMissingDeletedUtc)
                {
                    _loggedMissingDeletedUtc = true;
                    _log.LogWarning("TodoItems.DeletedUtc missing; skipping purge.");
                }

                return;
            }

            var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays);
            var deleted = await db.TodoItems
                .Where(t => t.DeletedUtc != null && t.DeletedUtc < cutoff)
                .ExecuteDeleteAsync(stoppingToken);

            if (deleted > 0)
            {
                _log.LogInformation("Purged {Count} todo items", deleted);
            }
        }
    }
}
