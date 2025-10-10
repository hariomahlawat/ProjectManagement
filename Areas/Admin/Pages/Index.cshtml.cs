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
using ProjectManagement.Models;

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
            public int ArchivedProjects { get; set; }
            public int TrashedProjects { get; set; }
            public int DeletedDocuments { get; set; }
            public int DeletedEvents { get; set; }

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

            var since7d = nowUtc.AddDays(-7);
            var since24h = nowUtc.AddDays(-1);
            var usersQuery = _db.Users.AsNoTracking();
            var loginQuery = _db.AuditLogs.AsNoTracking()
                .Where(a => a.TimeUtc >= since7d);
            var recentEventsQuery = _db.AuditLogs.AsNoTracking()
                .Where(a => a.TimeUtc >= since24h);
            var projectsQuery = _db.Projects.AsNoTracking();
            var documentsQuery = _db.ProjectDocuments.AsNoTracking()
                .Where(d => d.Status == ProjectDocumentStatus.SoftDeleted);
            var deletedEventsQuery = _db.Events.AsNoTracking()
                .Where(e => e.IsDeleted);

            Metrics.TotalUsers = await usersQuery.CountAsync();
            Metrics.DisabledUsers = await usersQuery.Where(u => u.IsDisabled).CountAsync();
            Metrics.MustChangePwd = await usersQuery.Where(u => u.MustChangePassword).CountAsync();

            Metrics.LoginsLast7d = await loginQuery.Where(a => a.Action == "LoginSuccess").CountAsync();
            Metrics.UniqueLoginsLast7d = await loginQuery.Where(a => a.Action == "LoginSuccess" && a.UserId != null)
                .Select(a => a.UserId!)
                .Distinct()
                .CountAsync();
            Metrics.FailedLoginsLast7d = await loginQuery.Where(a => a.Action == "LoginFailed").CountAsync();

            Metrics.AuditEvents24h = await recentEventsQuery.CountAsync();
            Metrics.WarningEvents24h = await recentEventsQuery.Where(a => a.Level == "Warning").CountAsync();
            Metrics.ErrorEvents24h = await recentEventsQuery.Where(a => a.Level == "Error").CountAsync();

            Metrics.ArchivedProjects = await projectsQuery.Where(p => !p.IsDeleted && p.IsArchived).CountAsync();
            Metrics.TrashedProjects = await projectsQuery.Where(p => p.IsDeleted).CountAsync();
            Metrics.DeletedDocuments = await documentsQuery.CountAsync();
            Metrics.DeletedEvents = await deletedEventsQuery.CountAsync();

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
            if (Metrics.TrashedProjects > 0)
                Attention.Items.Add(new AttentionItem
                {
                    Text = $"{Metrics.TrashedProjects} project(s) in trash",
                    LinkText = "Review",
                    Href = Url.Page("/Admin/Projects/Trash")
                });
            if (Metrics.DeletedDocuments > 0)
                Attention.Items.Add(new AttentionItem
                {
                    Text = $"{Metrics.DeletedDocuments} document(s) in recycle bin",
                    LinkText = "Restore",
                    Href = Url.Page("/Admin/Documents/Recycle")
                });
            if (Metrics.DeletedEvents > 0)
                Attention.Items.Add(new AttentionItem
                {
                    Text = $"{Metrics.DeletedEvents} calendar event(s) deleted",
                    LinkText = "Recover",
                    Href = Url.Page("/Admin/Calendar/Deleted")
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

