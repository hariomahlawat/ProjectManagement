using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Data;
using ProjectManagement.Models;

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

        public async Task LogAsync(string action, string? message = null, string level = "Info",
                                   string? userId = null, string? userName = null,
                                   object? data = null, HttpContext? http = null)
        {
            http ??= _http.HttpContext;
            var ip = http?.Connection?.RemoteIpAddress?.ToString();
            var ua = http?.Request?.Headers["User-Agent"].ToString();

            var log = new AuditLog
            {
                TimeUtc = DateTime.UtcNow,
                Level = level,
                Action = action,
                UserId = userId,
                UserName = userName,
                Ip = ip,
                UserAgent = ua,
                Message = message,
                DataJson = data == null ? null : JsonSerializer.Serialize(data)
            };

            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync();
        }
    }
}
