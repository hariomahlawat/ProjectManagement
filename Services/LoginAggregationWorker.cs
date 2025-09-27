using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services
{
    public class LoginAggregationWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<LoginAggregationWorker> _log;

        public LoginAggregationWorker(IServiceProvider sp, ILogger<LoginAggregationWorker> log)
        {
            _sp = sp;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

                await AggregateAsync(stoppingToken);

                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await AggregateAsync(stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                // Ignore cancellation exceptions to allow graceful shutdown
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation exceptions to allow graceful shutdown
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Login aggregation worker failed.");
            }
        }

        private async Task AggregateAsync(CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var now = DateTime.UtcNow;
            var date = DateOnly.FromDateTime(now.AddDays(-1));
            var exists = await db.DailyLoginStats.AnyAsync(x => x.Date == date, stoppingToken);
            if (exists)
            {
                return;
            }

            var from = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
            var to = from.AddDays(1);

            var count = await db.AuthEvents
                .Where(e => e.Event == "LoginSucceeded" && e.WhenUtc >= from && e.WhenUtc < to)
                .CountAsync(stoppingToken);

            db.DailyLoginStats.Add(new DailyLoginStat { Date = date, Count = count });
            await db.SaveChangesAsync(stoppingToken);
        }
    }
}
