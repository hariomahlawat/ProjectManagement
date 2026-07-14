using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Calendar;
using ProjectManagement.Tests.Fakes;

namespace ProjectManagement.Tests.Scheduling;

public sealed class HolidayAdminServiceTests
{
    [Fact]
    public async Task RestrictedHoliday_CanBeDeclaredAndWithdrawn_WithoutChangingClassification()
    {
        await using var db = CreateContext();
        var holiday = new Holiday
        {
            Date = new DateOnly(2026, 8, 26),
            Name = "Onam",
            Type = HolidayType.Restricted,
            IsObservedAsOfficeHoliday = false
        };
        db.Holidays.Add(holiday);
        await db.SaveChangesAsync();

        var audit = new RecordingAdminAudit();
        var service = CreateService(db, audit);
        var firstVersion = Convert.ToBase64String(holiday.RowVersion);

        var declared = await service.DeclareOfficeObservanceAsync(
            holiday.Id,
            "Office Order 12/2026",
            "Observed by the office.",
            firstVersion);

        Assert.True(declared.Succeeded);
        var observed = await db.Holidays.AsNoTracking().SingleAsync();
        Assert.Equal(HolidayType.Restricted, observed.Type);
        Assert.True(observed.IsObservedAsOfficeHoliday);
        Assert.Equal("Office Order 12/2026", observed.AuthorityReference);
        Assert.Contains(audit.Entries, entry => entry.Action == "RestrictedHolidayDeclaredOfficeHoliday");

        var withdrawn = await service.WithdrawOfficeObservanceAsync(
            holiday.Id,
            "Office observance withdrawn.",
            Convert.ToBase64String(observed.RowVersion));

        Assert.True(withdrawn.Succeeded);
        var informational = await db.Holidays.AsNoTracking().SingleAsync();
        Assert.Equal(HolidayType.Restricted, informational.Type);
        Assert.False(informational.IsObservedAsOfficeHoliday);
        Assert.Contains(audit.Entries, entry => entry.Action == "RestrictedHolidayOfficeObservanceWithdrawn");
    }

    private static HolidayAdminService CreateService(
        ApplicationDbContext db,
        RecordingAdminAudit audit)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "admin-1"),
                new Claim(ClaimTypes.Name, "administrator")
            },
            "Test");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return new HolidayAdminService(
            db,
            audit,
            accessor,
            FakeClock.ForIstDate(2026, 7, 14, 10),
            NullLogger<HolidayAdminService>.Instance);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class RecordingAdminAudit : IAdminAuditService
    {
        public List<AdminAuditEntry> Entries { get; } = new();

        public Task RecordAsync(
            AdminAuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
