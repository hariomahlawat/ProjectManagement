using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class ActivityTypeServiceTests
{
    [Fact]
    public async Task CreateAsync_WhenUserUnauthorized_ReturnsUnauthorized()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 1, 8, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new StubUserContext(CreatePrincipal("user-1"), "user-1");
        var service = new ActivityTypeService(context, clock, audit, userContext);

        var result = await service.CreateAsync("Operations", null, 1, CancellationToken.None);

        Assert.Equal(ActivityTypeMutationOutcome.Unauthorized, result.Outcome);
        Assert.Empty(await context.ActivityTypes.ToListAsync());
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task CreateAsync_WithValidInput_PersistsEntityAndAudit()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 2, 9, 30, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new StubUserContext(CreatePrincipal("manager-1", "ProjectOffice"), "manager-1");
        var service = new ActivityTypeService(context, clock, audit, userContext);

        var result = await service.CreateAsync("Operations", "General project office work", 2, CancellationToken.None);

        Assert.Equal(ActivityTypeMutationOutcome.Success, result.Outcome);
        var entity = await context.ActivityTypes.SingleAsync();
        Assert.Equal("Operations", entity.Name);
        Assert.Equal("General project office work", entity.Description);
        Assert.Equal(2, entity.Ordinal);
        Assert.True(entity.IsActive);
        Assert.Equal(clock.UtcNow, entity.CreatedAtUtc);
        Assert.Equal("manager-1", entity.CreatedByUserId);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("ProjectOfficeReports.ActivityTypeAdded", entry.Action);
        Assert.Equal("manager-1", entry.UserId);
        Assert.Equal(entity.Id.ToString(), entry.Data["ActivityTypeId"]);
        Assert.Equal("Operations", entry.Data["Name"]);
    }

    [Fact]
    public async Task UpdateAsync_WithDuplicateName_ReturnsDuplicate()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 3, 10, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new StubUserContext(CreatePrincipal("manager-2", "Admin"), "manager-2");
        var existing = new ActivityType
        {
            Id = Guid.NewGuid(),
            Name = "Operations",
            Description = "Ops",
            IsActive = true,
            Ordinal = 1,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = "seed"
        };
        context.ActivityTypes.Add(existing);
        context.ActivityTypes.Add(new ActivityType
        {
            Id = Guid.NewGuid(),
            Name = "Logistics",
            IsActive = true,
            Ordinal = 2,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = "seed"
        });
        await context.SaveChangesAsync();

        var service = new ActivityTypeService(context, clock, audit, userContext);
        var duplicateNameResult = await service.UpdateAsync(existing.Id, "Logistics", null, true, 1, existing.RowVersion, CancellationToken.None);

        Assert.Equal(ActivityTypeMutationOutcome.DuplicateName, duplicateNameResult.Outcome);
    }

    [Fact]
    public async Task UpdateAsync_WithStaleRowVersion_ReturnsConcurrency()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 4, 11, 15, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new StubUserContext(CreatePrincipal("manager-3", "HoD"), "manager-3");
        var existing = new ActivityType
        {
            Id = Guid.NewGuid(),
            Name = "Operations",
            Description = "Ops",
            IsActive = true,
            Ordinal = 1,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = "seed"
        };
        context.ActivityTypes.Add(existing);
        await context.SaveChangesAsync();

        var staleRowVersion = new byte[] { 9, 9, 9, 9 };
        var service = new ActivityTypeService(context, clock, audit, userContext);

        var result = await service.UpdateAsync(existing.Id, "Operations", null, true, 1, staleRowVersion, CancellationToken.None);

        Assert.Equal(ActivityTypeMutationOutcome.ConcurrencyConflict, result.Outcome);
    }

    [Fact]
    public async Task DeleteAsync_WhenInUse_ReturnsInUse()
    {
        await using var context = CreateContext();
        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 5, 5, 9, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var userContext = new StubUserContext(CreatePrincipal("manager-4", "ProjectOffice"), "manager-4");
        var typeId = Guid.NewGuid();
        context.ActivityTypes.Add(new ActivityType
        {
            Id = typeId,
            Name = "Operations",
            IsActive = true,
            Ordinal = 1,
            CreatedAtUtc = clock.UtcNow,
            CreatedByUserId = "seed"
        });
        context.MiscActivities.Add(new MiscActivity
        {
            Id = Guid.NewGuid(),
            ActivityTypeId = typeId,
            Nomenclature = "Compiled report",
            OccurrenceDate = new DateOnly(2024, 4, 1),
            CapturedAtUtc = clock.UtcNow,
            CapturedByUserId = "seed"
        });
        await context.SaveChangesAsync();

        var service = new ActivityTypeService(context, clock, audit, userContext);
        var rowVersion = (await context.ActivityTypes.AsNoTracking().Where(x => x.Id == typeId).Select(x => x.RowVersion).SingleAsync())!;

        var result = await service.DeleteAsync(typeId, rowVersion, CancellationToken.None);

        Assert.Equal(ActivityTypeDeletionOutcome.InUse, result.Outcome);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, params string[] roles)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(identity);
    }

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(ClaimsPrincipal user, string? userId)
        {
            User = user;
            UserId = userId;
        }

        public ClaimsPrincipal User { get; }

        public string? UserId { get; }
    }
}
