using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

// A value that records use is deactivated, never deleted (docs/ux/admin-review-2-fixes.md, PR 10).
public sealed class SocialMediaPlatformDeleteTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PlatformWithEvents_IsNotDeleted_AndTheListSaysHowManyUseIt()
    {
        await using var db = NewDb();
        var type = SocialMediaTestData.CreateEventType();
        var used = SocialMediaTestData.CreatePlatform(name: "Instagram");
        var unused = SocialMediaTestData.CreatePlatform(name: "Threads");
        db.SocialMediaEventTypes.Add(type);
        db.SocialMediaPlatforms.AddRange(used, unused);
        db.SocialMediaEvents.Add(SocialMediaTestData.CreateEvent(type.Id, used.Id));
        await db.SaveChangesAsync();

        var service = new SocialMediaPlatformService(db, new FixedClock(Now), new RecordingAudit());

        var summaries = await service.GetSummariesAsync(default);
        Assert.Equal(1, summaries.Single(item => item.Id == used.Id).EventCount);
        Assert.Equal(0, summaries.Single(item => item.Id == unused.Id).EventCount);

        var refused = await service.DeleteAsync(used.Id, used.RowVersion, "admin", default);
        Assert.Equal(SocialMediaPlatformDeletionOutcome.InUse, refused.Outcome);
        Assert.True(await db.SocialMediaPlatforms.AnyAsync(item => item.Id == used.Id));

        var deleted = await service.DeleteAsync(unused.Id, unused.RowVersion, "admin", default);
        Assert.Equal(SocialMediaPlatformDeletionOutcome.Success, deleted.Outcome);
        Assert.False(await db.SocialMediaPlatforms.AnyAsync(item => item.Id == unused.Id));
    }

    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
