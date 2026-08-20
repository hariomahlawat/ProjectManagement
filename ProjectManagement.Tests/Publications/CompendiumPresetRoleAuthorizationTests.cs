using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Publications;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPresetRoleAuthorizationTests
{
    [Fact]
    public async Task Ito_CanCreateSharedCompendiumPreset()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "ito-1", RoleNames.Ito);

        var result = await service.CreateAsync(
            "ito-1",
            "ITO Compendium",
            null,
            new CompendiumPresetConfiguration(
                "SDD Simulators Compendium",
                "Detailed Project Reference",
                "Capability Edition · 2026",
                handlingMarking: null,
                projectIds: new[] { 1 }),
            CancellationToken.None);

        Assert.True(result.Preset.Id > 0);
        Assert.Equal("ITO Compendium", result.Preset.Name);
    }

    [Fact]
    public async Task ProjectOfficer_CannotMaintainSharedCompendiumPreset()
    {
        await using var db = CreateContext();
        await SeedAsync(db);
        var service = CreateService(db, "po-1", RoleNames.ProjectOfficer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(
            "po-1",
            "Not Allowed",
            null,
            new CompendiumPresetConfiguration(
                "SDD Simulators Compendium",
                "Detailed Project Reference",
                "Capability Edition · 2026",
                handlingMarking: null,
                projectIds: new[] { 1 }),
            CancellationToken.None));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"compendium-role-auth-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        db.Users.AddRange(
            User("ito-1", "Information Technology Officer"),
            User("po-1", "Project Officer"));

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Project Alpha",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            CreatedByUserId = "ito-1",
            RowVersion = new byte[] { 1 }
        });

        await db.SaveChangesAsync();
    }

    private static ApplicationUser User(string id, string fullName) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        FullName = fullName,
        Rank = "Test",
        SecurityStamp = Guid.NewGuid().ToString("N")
    };

    private static CompendiumPresetService CreateService(ApplicationDbContext db, string userId, string role)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userId),
                new Claim(ClaimTypes.Role, role)
            },
            "TestAuth");

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return new CompendiumPresetService(
            db,
            accessor,
            new NullAuditService(),
            new FixedClock(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<CompendiumPresetService>.Instance);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class NullAuditService : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            HttpContext? http = null)
            => Task.CompletedTask;
    }
}
