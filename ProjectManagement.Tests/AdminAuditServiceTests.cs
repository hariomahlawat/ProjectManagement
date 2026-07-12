using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminAuditServiceTests
{
    [Fact]
    public async Task RecordAsync_CapturesActorEntityTraceAndBeforeAfterState()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);

        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-admin-1",
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "admin-1"),
                new Claim(ClaimTypes.Name, "administrator"),
                new Claim(ClaimTypes.Role, "Admin")
            }, "Test"))
        };
        context.Request.Path = "/Admin/Users/Edit";

        var accessor = new HttpContextAccessor { HttpContext = context };
        var audit = new AuditService(db, accessor, new FixedClock(new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero)));
        var service = new AdminAuditService(audit, accessor);

        await service.RecordAsync(new AdminAuditEntry(
            "AdminUserUpdated",
            "ApplicationUser",
            "user-7",
            Before: new { Rank = "Maj" },
            After: new { Rank = "Lt Col" },
            Reason: "Promotion"));

        var row = Assert.Single(db.AuditLogs);
        Assert.Equal("admin-1", row.UserId);
        Assert.Equal("administrator", row.UserName);
        Assert.Equal("AdminUserUpdated", row.Action);
        Assert.Contains("trace-admin-1", row.DataJson);
        Assert.Contains("ApplicationUser", row.DataJson);
        Assert.Contains("user-7", row.DataJson);
        Assert.Contains("Promotion", row.DataJson);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
