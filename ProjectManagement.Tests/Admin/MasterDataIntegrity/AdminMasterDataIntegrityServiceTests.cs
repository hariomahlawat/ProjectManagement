using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.Integrity;

namespace ProjectManagement.Tests.Admin.MasterDataIntegrity;

public sealed class AdminMasterDataIntegrityServiceTests
{
    [Fact]
    public async Task InspectAsync_DetectsOrderingCalendarAndAnnualDateFindings()
    {
        await using var db = CreateContext();
        db.ProjectTypes.AddRange(
            new ProjectType { Id = 1, Name = "A", SortOrder = 1 },
            new ProjectType { Id = 2, Name = "B", SortOrder = 1 },
            new ProjectType { Id = 3, Name = "C", SortOrder = 5 });
        db.Holidays.AddRange(
            new Holiday { Id = 1, Date = new DateOnly(2026, 1, 26), Name = "Holiday A" },
            new Holiday { Id = 2, Date = new DateOnly(2026, 1, 26), Name = "Holiday B" });
        db.Celebrations.Add(new Celebration
        {
            Id = Guid.NewGuid(),
            Name = "Invalid annual date",
            EventType = CelebrationType.Birthday,
            Day = 31,
            Month = 2,
            CreatedById = "admin"
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var snapshot = await service.InspectAsync();

        Assert.Equal(MasterDataIntegrityStatus.Warning,
            snapshot.Checks.Single(check => check.Key == "project-type-order").Status);
        Assert.Equal(MasterDataIntegrityStatus.Critical,
            snapshot.Checks.Single(check => check.Key == "holiday-dates").Status);
        Assert.Equal(MasterDataIntegrityStatus.Critical,
            snapshot.Checks.Single(check => check.Key == "celebration-dates").Status);
        Assert.True(snapshot.Findings >= 3);
    }

    [Fact]
    public async Task NormaliseOrderAsync_ProducesContiguousDeterministicPositionsAndAudit()
    {
        await using var db = CreateContext();
        db.ProjectTypes.AddRange(
            new ProjectType { Id = 1, Name = "Zulu", SortOrder = 4 },
            new ProjectType { Id = 2, Name = "Alpha", SortOrder = 1 },
            new ProjectType { Id = 3, Name = "Bravo", SortOrder = 1 });
        await db.SaveChangesAsync();
        var audit = new RecordingAdminAudit();
        var service = CreateService(db, audit);

        var result = await service.NormaliseOrderAsync("project-type-order");
        var rows = await db.ProjectTypes.AsNoTracking()
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .ToListAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { 0, 1, 2 }, rows.Select(item => item.SortOrder));
        Assert.Single(audit.Entries);
        Assert.Equal("MasterData.IntegrityOrderNormalised", audit.Entries[0].Action);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AdminMasterDataIntegrityService CreateService(
        ApplicationDbContext db,
        RecordingAdminAudit? audit = null) =>
        new(
            db,
            audit ?? new RecordingAdminAudit(),
            new FixedAdminTime(new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero)));

    private sealed class RecordingAdminAudit : IAdminAuditService
    {
        public List<AdminAuditEntry> Entries { get; } = new();

        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedAdminTime : IAdminTimeService
    {
        public FixedAdminTime(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
        public DateOnly TodayIst => DateOnly.FromDateTime(UtcNow.DateTime);
        public DateTimeOffset StartOfIstDayUtc(DateOnly date) => throw new NotSupportedException();
        public DateTimeOffset EndExclusiveOfIstDayUtc(DateOnly date) => throw new NotSupportedException();
        public DateTime ToIst(DateTime utc) => utc;
        public DateTimeOffset ToIst(DateTimeOffset utc) => utc;
        public string FormatIst(DateTime? utc, string fallback = "—") => utc?.ToString("O") ?? fallback;
        public string FormatIst(DateTimeOffset? utc, string fallback = "—") => utc?.ToString("O") ?? fallback;
    }
}
