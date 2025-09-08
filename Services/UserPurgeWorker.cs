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

namespace ProjectManagement.Services
{
    public class UserPurgeWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<UserPurgeWorker> _log;

        public UserPurgeWorker(IServiceProvider sp, ILogger<UserPurgeWorker> log)
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
                    var opts = scope.ServiceProvider.GetRequiredService<IOptions<UserLifecycleOptions>>().Value;
                    var due = await db.Users
                        .Where(u => u.PendingDeletion && u.DeletionRequestedUtc != null &&
                                    DateTime.UtcNow >= u.DeletionRequestedUtc.Value.AddMinutes(opts.UndoWindowMinutes))
                        .Select(u => u.Id)
                        .ToListAsync(stoppingToken);

                    var svc = scope.ServiceProvider.GetRequiredService<IUserLifecycleService>();
                    foreach (var id in due)
                    {
                        if (await svc.PurgeIfDueAsync(id))
                            _log.LogInformation("Purged user {UserId}", id);
                    }
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                // Ignore cancellation exceptions to allow graceful shutdown
            }
        }
    }
}
