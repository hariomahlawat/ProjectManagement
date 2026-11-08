using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Admin;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Failed sign-ins carry only the name as typed. They used to be matched to accounts
/// case-sensitively, so "ADMIN" and "admin" were two affected accounts, and a person with a
/// success (keyed by user id) and a failure (keyed by name) counted twice as an active user.
/// </summary>
public sealed class AdminLoginMonitoringCaseTests
{
    [Fact]
    public async Task TypedNamesInAnyCase_CountAsTheAccountTheyBelongTo()
    {
        var now = new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.Users.Add(new ApplicationUser { Id = "u-admin", UserName = "admin", NormalizedUserName = "ADMIN", FullName = "Admin" });
        db.AuthEvents.Add(new AuthEvent { Id = Guid.NewGuid(), UserId = "u-admin", Event = AuthenticationEventNames.LoginSucceeded, WhenUtc = now.AddHours(-1) });
        db.AuditLogs.AddRange(
            new AuditLog { TimeUtc = now.AddHours(-2).UtcDateTime, Level = "Warning", Action = AuthenticationEventNames.AuditLoginFailed, UserName = "ADMIN" },
            new AuditLog { TimeUtc = now.AddHours(-3).UtcDateTime, Level = "Warning", Action = AuthenticationEventNames.AuditLoginFailed, UserName = "Admin" },
            new AuditLog { TimeUtc = now.AddHours(-4).UtcDateTime, Level = "Warning", Action = AuthenticationEventNames.AuditLoginFailed, UserName = "nobody" });
        await db.SaveChangesAsync();

        var service = new AdminLoginMonitoringService(
            db,
            new AdminTimeService(FakeClock.AtUtc(now)),
            Options.Create(new AdminLoginMonitoringOptions()));

        var all = await service.GetAsync(new AdminLoginMonitoringRequest(Days: 7));
        var forAdmin = await service.GetAsync(new AdminLoginMonitoringRequest(Days: 7, UserId: "u-admin"));

        Assert.Equal(3, all.Failed);
        Assert.Equal(2, all.AffectedAccounts); // admin + the unknown "nobody"
        Assert.Equal(2, all.UniqueUsers);      // admin once, plus "nobody"
        // Only admin signed in successfully: the caption under Successful sign-ins counts that.
        Assert.Equal(1, all.SuccessfulPeople);
        Assert.Equal(2, forAdmin.Failed);
    }
}
