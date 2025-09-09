using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Helpers;

namespace ProjectManagement.Services
{
    public class AuditService : IAuditService
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpContextAccessor _http;

        public AuditService(ApplicationDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        private static readonly string[] SensitiveKeys = new[]
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

        public async Task LogAsync(string action, string? message = null, string level = "Info",
                                   string? userId = null, string? userName = null,
                                   IDictionary<string, string?>? data = null, HttpContext? http = null)
        {
            if (action.StartsWith("Todo.", StringComparison.OrdinalIgnoreCase))
            {
                return; // Skip noisy Todo logs
            }

            http ??= _http.HttpContext;
            var ip = ClientIp.Get(http);
            var ua = http?.Request?.Headers["User-Agent"].ToString();

            var clean = data is null ? null : Scrub(data);

            var log = new AuditLog
            {
                TimeUtc = DateTime.UtcNow,
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
        }
    }
}
