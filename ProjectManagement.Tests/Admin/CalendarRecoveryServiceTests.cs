using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Calendar;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Event has a global !IsDeleted query filter. The admin recovery queries must opt out of it,
/// or no deleted event can ever be listed, counted or restored.
/// </summary>
public sealed class CalendarRecoveryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Query_ListsDeletedEventsOnly()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"calendar-recovery-{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApplicationDbContext(options);
        db.Events.AddRange(
            NewEvent("Deleted review", isDeleted: true),
            NewEvent("Live review", isDeleted: false));
        await db.SaveChangesAsync();

        var page = await CreateService(db, new RecordingAdminAudit()).QueryAsync(new DeletedCalendarEventQuery());

        var item = Assert.Single(page.Items);
        Assert.Equal("Deleted review", item.Title);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Restore_BringsADeletedEventBack_AndAuditsIt()
    {
        // SQLite: the restore uses ExecuteUpdate and a transaction, which the in-memory provider lacks.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var deleted = NewEvent("Deleted review", isDeleted: true);
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Events.Add(deleted);
            await seed.SaveChangesAsync();
        }

        var audit = new RecordingAdminAudit();
        await using (var db = new ApplicationDbContext(options))
        {
            var result = await CreateService(db, audit).RestoreAsync(deleted.Id);
            Assert.True(result.Succeeded, result.UserMessage);
        }

        await using var verify = new ApplicationDbContext(options);
        var restored = await verify.Events.SingleAsync(item => item.Id == deleted.Id);
        Assert.False(restored.IsDeleted);
        Assert.Equal("CalendarEventRestored", Assert.Single(audit.Entries).Action);
    }

    private static Event NewEvent(string title, bool isDeleted) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Category = EventCategory.Other,
        StartUtc = Now.AddDays(1),
        EndUtc = Now.AddDays(1).AddHours(1),
        CreatedAt = Now.AddDays(-2),
        UpdatedAt = Now.AddDays(-1),
        IsDeleted = isDeleted
    };

    private static CalendarRecoveryService CreateService(ApplicationDbContext db, IAdminAuditService audit) =>
        new(
            db,
            CreateUserManager(db),
            audit,
            new AdminTimeService(FakeClock.AtUtc(Now)),
            new HttpContextAccessor(),
            NullLogger<CalendarRecoveryService>.Instance);

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db) =>
        new(
            new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db),
            new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

    private sealed class RecordingAdminAudit : IAdminAuditService
    {
        public List<AdminAuditEntry> Entries { get; } = new();

        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
