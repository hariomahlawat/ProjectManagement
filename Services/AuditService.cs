using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Usage;
using ProjectManagement.Services.Usage;

namespace ProjectManagement.Services
{
    public class AuditService : IAuditService
    {
        private const int MaximumDailyActionCount = 1000000;

        private readonly ApplicationDbContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly IClock _clock;
        private readonly ILogger<AuditService> _logger;

        public AuditService(
            ApplicationDbContext db,
            IHttpContextAccessor http,
            IClock? clock = null,
            ILogger<AuditService>? logger = null)
        {
            _db = db;
            _http = http;
            _clock = clock ?? new SystemClock();
            _logger = logger ?? NullLogger<AuditService>.Instance;
        }

        private static readonly string[] SensitiveKeys =
        {
            "password", "pwd", "pass", "token", "authorization", "cookie", "secret",
            "apikey", "api_key", "client_secret", "otp", "code", "privatekey", "private_key"
        };

        private static IDictionary<string, string?> Scrub(IDictionary<string, string?> data)
        {
            return data.ToDictionary(
                kvp => kvp.Key,
                kvp => SensitiveKeys.Any(sk => kvp.Key.Contains(sk, StringComparison.OrdinalIgnoreCase))
                    ? "***redacted***"
                    : kvp.Value
            );
        }

        public async Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
        {
            if (action.StartsWith("Todo.", StringComparison.OrdinalIgnoreCase))
            {
                return; // Skip noisy Todo logs
            }

            http ??= _http.HttpContext;
            var ip = ClientIp.Get(http);
            var ua = http?.Request?.Headers["User-Agent"].ToString();
            var clean = data is null ? null : Scrub(data);
            var occurredUtc = _clock.UtcNow.ToUniversalTime();

            var log = new AuditLog
            {
                TimeUtc = occurredUtc.UtcDateTime,
                Level = level,
                Action = action,
                UserId = userId,
                UserName = userName,
                Ip = string.IsNullOrWhiteSpace(ip) ? null : ip,
                UserAgent = ua,
                Message = message,
                DataJson = clean is null ? null : JsonSerializer.Serialize(clean)
            };

            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync();

            var actionKind = ErpUsageActionClassifier.Classify(action);
            if (string.IsNullOrWhiteSpace(userId) || actionKind == ErpUsageActionKind.Ignored)
            {
                return;
            }

            try
            {
                await PersistDailyActionSummaryAsync(userId, occurredUtc, actionKind);
            }
            catch (Exception exception)
            {
                // The formal audit entry has already been written. Failure to update the
                // derived adoption summary must not turn a successful business action into
                // an application error; the migration/backfill can recover it from AuditLogs.
                _logger.LogWarning(
                    exception,
                    "Could not update the permanent ERP activity summary for user {UserId} and action {Action}.",
                    userId,
                    action);
            }
        }

        private async Task PersistDailyActionSummaryAsync(
            string userId,
            DateTimeOffset occurredUtc,
            ErpUsageActionKind actionKind)
        {
            var seenUtc = occurredUtc.UtcDateTime;
            var activityDateIst = DateOnly.FromDateTime(IstClock.ToIst(occurredUtc).DateTime);
            var isOperational = actionKind == ErpUsageActionKind.Operational;
            var isAdministrative = actionKind == ErpUsageActionKind.Administrative;
            var operationalIncrement = isOperational ? 1 : 0;
            var administrativeIncrement = isAdministrative ? 1 : 0;

            if (_db.Database.IsNpgsql())
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "UserActivityDailySummaries"
                        ("UserId", "ActivityDateIst", "HadNavigation", "HadInteractiveHeartbeat",
                         "HadAdministrativeAction", "HadOperationalAction",
                         "FirstSeenUtc", "LastSeenUtc", "NavigationCount", "HeartbeatCount",
                         "AdministrativeActionCount", "OperationalActionCount")
                    SELECT
                        {userId}, {activityDateIst}, FALSE, FALSE,
                        {isAdministrative}, {isOperational},
                        {seenUtc}, {seenUtc}, 0, 0,
                        {administrativeIncrement}, {operationalIncrement}
                    WHERE EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "Id" = {userId})
                    ON CONFLICT ("UserId", "ActivityDateIst")
                    DO UPDATE SET
                        "HadAdministrativeAction" = "UserActivityDailySummaries"."HadAdministrativeAction" OR EXCLUDED."HadAdministrativeAction",
                        "HadOperationalAction" = "UserActivityDailySummaries"."HadOperationalAction" OR EXCLUDED."HadOperationalAction",
                        "FirstSeenUtc" = LEAST("UserActivityDailySummaries"."FirstSeenUtc", EXCLUDED."FirstSeenUtc"),
                        "LastSeenUtc" = GREATEST("UserActivityDailySummaries"."LastSeenUtc", EXCLUDED."LastSeenUtc"),
                        "AdministrativeActionCount" = LEAST({MaximumDailyActionCount}, "UserActivityDailySummaries"."AdministrativeActionCount"::bigint + EXCLUDED."AdministrativeActionCount")::integer,
                        "OperationalActionCount" = LEAST({MaximumDailyActionCount}, "UserActivityDailySummaries"."OperationalActionCount"::bigint + EXCLUDED."OperationalActionCount")::integer;
                    """);
                return;
            }

            var userExists = await _db.Users.AnyAsync(user => user.Id == userId);
            if (!userExists)
            {
                return;
            }

            var summary = await _db.UserActivityDailySummaries.SingleOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.ActivityDateIst == activityDateIst);

            if (summary is null)
            {
                _db.UserActivityDailySummaries.Add(new UserActivityDailySummary
                {
                    UserId = userId,
                    ActivityDateIst = activityDateIst,
                    HadAdministrativeAction = isAdministrative,
                    HadOperationalAction = isOperational,
                    FirstSeenUtc = seenUtc,
                    LastSeenUtc = seenUtc,
                    AdministrativeActionCount = administrativeIncrement,
                    OperationalActionCount = operationalIncrement
                });
            }
            else
            {
                summary.HadAdministrativeAction |= isAdministrative;
                summary.HadOperationalAction |= isOperational;
                summary.FirstSeenUtc = summary.FirstSeenUtc <= seenUtc ? summary.FirstSeenUtc : seenUtc;
                summary.LastSeenUtc = summary.LastSeenUtc >= seenUtc ? summary.LastSeenUtc : seenUtc;
                if (isAdministrative)
                {
                    summary.AdministrativeActionCount = Math.Min(
                        MaximumDailyActionCount,
                        summary.AdministrativeActionCount + 1);
                }
                if (isOperational)
                {
                    summary.OperationalActionCount = Math.Min(
                        MaximumDailyActionCount,
                        summary.OperationalActionCount + 1);
                }
            }

            await _db.SaveChangesAsync();
        }
    }
}
