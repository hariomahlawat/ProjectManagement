using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests;

public class ProjectFactsServiceTests
{
    [Fact]
    public async Task UpsertIpaCostAsync_CreatesFactWhenMissing()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedProjectAsync(db);
        var service = new ProjectFactsService(db, clock, new FakeAudit());

        await service.UpsertIpaCostAsync(1, 1250.25m, "user-a");

        var fact = await db.ProjectIpaFacts.SingleAsync();
        Assert.Equal(1250.25m, fact.IpaCost);
        Assert.Equal("user-a", fact.CreatedByUserId);
        Assert.Equal(clock.UtcNow.UtcDateTime, fact.CreatedOnUtc);
    }

    [Fact]
    public async Task UpsertIpaCostAsync_UpdatesExistingFact()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedProjectAsync(db);
        db.ProjectIpaFacts.Add(new ProjectIpaFact
        {
            ProjectId = 1,
            IpaCost = 10m,
            CreatedByUserId = "seed",
            CreatedOnUtc = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
        var service = new ProjectFactsService(db, clock, new FakeAudit());

        await service.UpsertIpaCostAsync(1, 2200m, "user-b");

        var fact = await db.ProjectIpaFacts.SingleAsync();
        Assert.Equal(2200m, fact.IpaCost);
        Assert.Equal("seed", fact.CreatedByUserId);
        Assert.Equal(new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc), fact.CreatedOnUtc);
    }

    [Fact]
    public async Task UpsertSowSponsorsAsync_CreatesAndUpdatesFact()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 7, 15, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedProjectAsync(db);
        var service = new ProjectFactsService(db, clock, new FakeAudit());

        await service.UpsertSowSponsorsAsync(1, "Operations", "Line A", "user-a");

        var created = await db.ProjectSowFacts.SingleAsync();
        Assert.Equal("Operations", created.SponsoringUnit);
        Assert.Equal("Line A", created.SponsoringLineDirectorate);
        Assert.Equal(clock.UtcNow.UtcDateTime, created.CreatedOnUtc);

        await service.UpsertSowSponsorsAsync(1, "Strategy", "Line B", "user-b");

        var updated = await db.ProjectSowFacts.SingleAsync();
        Assert.Equal("Strategy", updated.SponsoringUnit);
        Assert.Equal("Line B", updated.SponsoringLineDirectorate);
        Assert.Equal("user-a", updated.CreatedByUserId);
    }

    [Fact]
    public async Task UpsertSupplyOrderDateAsync_CreatesAndUpdatesFact()
    {
        var clock = new TestClock(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = CreateContext();
        await SeedProjectAsync(db);
        var service = new ProjectFactsService(db, clock, new FakeAudit());

        await service.UpsertSupplyOrderDateAsync(1, new DateOnly(2024, 2, 15), "user-a");

        var created = await db.ProjectSupplyOrderFacts.SingleAsync();
        Assert.Equal(new DateOnly(2024, 2, 15), created.SupplyOrderDate);
        Assert.Equal("user-a", created.CreatedByUserId);
        Assert.Equal(clock.UtcNow.UtcDateTime, created.CreatedOnUtc);

        await service.UpsertSupplyOrderDateAsync(1, new DateOnly(2024, 4, 2), "user-b");

        var updated = await db.ProjectSupplyOrderFacts.SingleAsync();
        Assert.Equal(new DateOnly(2024, 4, 2), updated.SupplyOrderDate);
        Assert.Equal("user-a", updated.CreatedByUserId);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task SeedProjectAsync(ApplicationDbContext db)
    {
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "seed"
        });

        await db.SaveChangesAsync();
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeAudit : IAuditService
    {
        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, Microsoft.AspNetCore.Http.HttpContext? http = null)
            => Task.CompletedTask;
    }
}
