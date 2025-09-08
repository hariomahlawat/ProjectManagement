using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;

namespace ProjectManagement.Services
{
    public class TodoPurgeWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<TodoPurgeWorker> _log;
        private const int RetentionDays = 7;

        public TodoPurgeWorker(IServiceProvider sp, ILogger<TodoPurgeWorker> log)
        {
            _sp = sp;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var scope = _sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
                    var deleted = await db.TodoItems
                        .Where(t => t.DeletedUtc != null && t.DeletedUtc < cutoff)
                        .ExecuteDeleteAsync(stoppingToken);
                    if (deleted > 0)
                        _log.LogInformation("Purged {Count} todo items", deleted);
                    await Task.Delay(TimeSpan.FromDays(7), stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                // Ignore cancellation exceptions to allow graceful shutdown
            }
        }
    }
}
