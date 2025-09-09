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
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    using var scope = _sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var hasDeletedUtc = await db.Database.ExecuteSqlRawAsync(
                        "SELECT 1 FROM information_schema.columns WHERE table_name='TodoItems' AND column_name='DeletedUtc'") > 0;
                    if (!hasDeletedUtc)
                    {
                        _log.LogWarning("TodoItems.DeletedUtc missing; skipping purge.");
                        continue;
                    }

                    var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays);
                    var deleted = await db.TodoItems
                        .Where(t => t.DeletedUtc != null && t.DeletedUtc < cutoff)
                        .ExecuteDeleteAsync(stoppingToken);
                    if (deleted > 0)
                        _log.LogInformation("Purged {Count} todo items", deleted);
                }
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.LogError(ex, "Todo purge worker failed.");
            }
        }
    }
}
