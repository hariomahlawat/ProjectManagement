using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Areas.Admin.Pages
{
    [Authorize(Roles = "Admin")]
    public class AdminIndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public AdminIndexModel(ApplicationDbContext db) => _db = db;

        public MetricsVM Metrics { get; set; } = new();
        public List<ActionVM> RecentAdminActions { get; set; } = new();
        public AttentionVM Attention { get; set; } = new();

        public class MetricsVM
        {
            public int TotalUsers { get; set; }
            public int DisabledUsers { get; set; }
            public int MustChangePwd { get; set; }
            public int LoginsLast7d { get; set; }
            public int UniqueLoginsLast7d { get; set; }
            public int FailedLoginsLast7d { get; set; }
            public int AuditEvents24h { get; set; }
            public int WarningEvents24h { get; set; }
            public int ErrorEvents24h { get; set; }

            public int DisabledPct => TotalUsers == 0 ? 0 : (int)Math.Round(100.0 * DisabledUsers / TotalUsers);
            public int MustChangePwdPct => TotalUsers == 0 ? 0 : (int)Math.Round(100.0 * MustChangePwd / TotalUsers);
            public int UniqueLoginPct => LoginsLast7d == 0 ? 0 : (int)Math.Round(100.0 * UniqueLoginsLast7d / LoginsLast7d);
            public int FailedLoginPct => LoginsLast7d == 0 ? 0 : (int)Math.Round(100.0 * FailedLoginsLast7d / LoginsLast7d);
            public int WarningPct => AuditEvents24h == 0 ? 0 : (int)Math.Round(100.0 * WarningEvents24h / AuditEvents24h);
            public int ErrorPct => AuditEvents24h == 0 ? 0 : (int)Math.Round(100.0 * ErrorEvents24h / AuditEvents24h);
        }

        public class ActionVM
        {
            public string Level { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string WhenLocal { get; set; } = string.Empty;
        }

        public class AttentionVM
        {
            public List<AttentionItem> Items { get; set; } = new();
        }

        public class AttentionItem
        {
            public string Text { get; set; } = string.Empty;
            public string? LinkText { get; set; }
            public string? Href { get; set; }
        }

        public async Task OnGet()
        {
            var nowUtc = DateTime.UtcNow;

            var users = await _db.Users.AsNoTracking().ToListAsync();
            Metrics.TotalUsers = users.Count;
            Metrics.DisabledUsers = users.Count(u => u.IsDisabled);
            Metrics.MustChangePwd = users.Count(u => u.MustChangePassword);

            var since7d = nowUtc.AddDays(-7);
            var loginLogs = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.TimeUtc >= since7d)
                .ToListAsync();
            Metrics.LoginsLast7d = loginLogs.Count(a => a.Action == "LoginSuccess");
            Metrics.UniqueLoginsLast7d = loginLogs.Where(a => a.Action == "LoginSuccess")
                .Select(a => a.UserId).Where(id => id != null).Distinct().Count();
            Metrics.FailedLoginsLast7d = loginLogs.Count(a => a.Action == "LoginFailed");

            var since24h = nowUtc.AddDays(-1);
            var recentEvents = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.TimeUtc >= since24h)
                .ToListAsync();
            Metrics.AuditEvents24h = recentEvents.Count;
            Metrics.WarningEvents24h = recentEvents.Count(a => a.Level == "Warning");
            Metrics.ErrorEvents24h = recentEvents.Count(a => a.Level == "Error");

            if (Metrics.MustChangePwd > 0)
                Attention.Items.Add(new AttentionItem
                {
                    Text = $"{Metrics.MustChangePwd} users must change password",
                    LinkText = "View",
                    Href = Url.Page("/Admin/Users/Index", new { status = "" })
                });
            if (Metrics.DisabledUsers > 0)
                Attention.Items.Add(new AttentionItem
                {
                    Text = $"{Metrics.DisabledUsers} users are disabled",
                    LinkText = "Manage",
                    Href = Url.Page("/Admin/Users/Index", new { status = "disabled" })
                });
            if (Metrics.ErrorEvents24h > 0)
                Attention.Items.Add(new AttentionItem
                {
                    Text = $"{Metrics.ErrorEvents24h} error logs in last 24 hours",
                    LinkText = "Investigate",
                    Href = Url.Page("/Admin/Logs/Index")
                });

            RecentAdminActions = await _db.AuditLogs.AsNoTracking()
                .OrderByDescending(a => a.TimeUtc)
                .Take(10)
                .Select(a => new ActionVM
                {
                    Level = a.Level,
                    Message = a.Message ?? a.Action,
                    WhenLocal = TimeFmt.ToIst(a.TimeUtc)
                })
                .ToListAsync();
        }
    }
}

