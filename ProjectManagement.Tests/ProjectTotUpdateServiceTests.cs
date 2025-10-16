using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectTotUpdateServiceTests
{
    [Fact]
    public async Task SubmitAsync_ProjectOfficerCreatesPendingUpdate()
    {
        await using var context = CreateContext();
        using var userManager = CreateUserManager(context);

        context.Roles.Add(new IdentityRole
        {
            Id = "role-po",
            Name = "Project Officer",
            NormalizedName = "PROJECT OFFICER"
        });

        context.Projects.Add(new Project
        {
            Id = 1,
            Name = "Orion",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator",
            LeadPoUserId = "po-1",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        });

        var projectOfficer = new ApplicationUser
        {
            Id = "po-1",
            UserName = "po1",
            Email = "po1@example.com"
        };

        await context.SaveChangesAsync();
        await userManager.CreateAsync(projectOfficer);
        await userManager.AddToRoleAsync(projectOfficer, "Project Officer");

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotUpdateService(context, userManager, clock);
        var principal = CreatePrincipal(projectOfficer.Id);

        var result = await service.SubmitAsync(1, "Visited partner lab", new DateOnly(2024, 9, 15), principal);

        Assert.True(result.IsSuccess);

        var update = await context.ProjectTotProgressUpdates.SingleAsync();
        Assert.Equal(ProjectTotProgressUpdateState.Pending, update.State);
        Assert.Equal(ProjectTotUpdateActorRole.ProjectOfficer, update.SubmittedByRole);
        Assert.Equal("Visited partner lab", update.Body);
        Assert.Equal(new DateOnly(2024, 9, 15), update.EventDate);
        Assert.Null(update.PublishedOnUtc);
    }

    [Fact]
    public async Task SubmitAsync_HoDAutoApprovesUpdate()
    {
        await using var context = CreateContext();
        using var userManager = CreateUserManager(context);

        context.Roles.AddRange(
            new IdentityRole { Id = "role-hod", Name = "HoD", NormalizedName = "HOD" },
            new IdentityRole { Id = "role-po", Name = "Project Officer", NormalizedName = "PROJECT OFFICER" });

        context.Projects.Add(new Project
        {
            Id = 2,
            Name = "Lyra",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator",
            LeadPoUserId = "po-2",
            HodUserId = "hod-1",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        });

        var hod = new ApplicationUser { Id = "hod-1", UserName = "hod", Email = "hod@example.com" };
        await context.SaveChangesAsync();
        await userManager.CreateAsync(hod);
        await userManager.AddToRoleAsync(hod, "HoD");

        var clock = new FixedClock(new DateTimeOffset(2024, 11, 2, 7, 30, 0, TimeSpan.Zero));
        var service = new ProjectTotUpdateService(context, userManager, clock);
        var principal = CreatePrincipal(hod.Id);

        var result = await service.SubmitAsync(2, "Technology shared with unit", new DateOnly(2024, 10, 25), principal);

        Assert.True(result.IsSuccess);

        var update = await context.ProjectTotProgressUpdates.SingleAsync();
        Assert.Equal(ProjectTotProgressUpdateState.Approved, update.State);
        Assert.Equal(ProjectTotUpdateActorRole.HeadOfDepartment, update.SubmittedByRole);
        Assert.Equal(ProjectTotUpdateActorRole.HeadOfDepartment, update.DecidedByRole);
        Assert.NotNull(update.PublishedOnUtc);
        Assert.Equal(clock.UtcNow.UtcDateTime, update.PublishedOnUtc);
    }

    [Fact]
    public async Task DecideAsync_AdminApprovesPendingUpdate()
    {
        await using var context = CreateContext();
        using var userManager = CreateUserManager(context);

        context.Roles.AddRange(
            new IdentityRole { Id = "role-admin", Name = "Admin", NormalizedName = "ADMIN" },
            new IdentityRole { Id = "role-po", Name = "Project Officer", NormalizedName = "PROJECT OFFICER" });

        context.Projects.Add(new Project
        {
            Id = 3,
            Name = "Vega",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator",
            LeadPoUserId = "po-3",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        });

        var projectOfficer = new ApplicationUser { Id = "po-3", UserName = "po3" };
        var admin = new ApplicationUser { Id = "admin-1", UserName = "admin" };

        await context.SaveChangesAsync();
        await userManager.CreateAsync(projectOfficer);
        await userManager.CreateAsync(admin);
        await userManager.AddToRoleAsync(projectOfficer, "Project Officer");
        await userManager.AddToRoleAsync(admin, "Admin");

        var clock = new FixedClock(new DateTimeOffset(2024, 12, 1, 9, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotUpdateService(context, userManager, clock);

        var officerPrincipal = CreatePrincipal(projectOfficer.Id);
        await service.SubmitAsync(3, "Prepared handover kit", null, officerPrincipal);

        var pendingUpdate = await context.ProjectTotProgressUpdates.SingleAsync();
        var adminPrincipal = CreatePrincipal(admin.Id);

        var decision = await service.DecideAsync(
            3,
            pendingUpdate.Id,
            approve: true,
            decisionRemarks: "Proceed",
            expectedRowVersion: pendingUpdate.RowVersion,
            principal: adminPrincipal);

        Assert.True(decision.IsSuccess);

        await context.Entry(pendingUpdate).ReloadAsync();
        Assert.Equal(ProjectTotProgressUpdateState.Approved, pendingUpdate.State);
        Assert.Equal(ProjectTotUpdateActorRole.Administrator, pendingUpdate.DecidedByRole);
        Assert.Equal("Proceed", pendingUpdate.DecisionRemarks);
        Assert.NotNull(pendingUpdate.PublishedOnUtc);
    }

    [Fact]
    public async Task DecideAsync_HoDRejectsPendingUpdate()
    {
        await using var context = CreateContext();
        using var userManager = CreateUserManager(context);

        context.Roles.AddRange(
            new IdentityRole { Id = "role-hod", Name = "HoD", NormalizedName = "HOD" },
            new IdentityRole { Id = "role-po", Name = "Project Officer", NormalizedName = "PROJECT OFFICER" });

        context.Projects.Add(new Project
        {
            Id = 4,
            Name = "Rigel",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator",
            LeadPoUserId = "po-4",
            HodUserId = "hod-4",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        });

        var po = new ApplicationUser { Id = "po-4", UserName = "po4" };
        var hod = new ApplicationUser { Id = "hod-4", UserName = "hod4" };

        await context.SaveChangesAsync();
        await userManager.CreateAsync(po);
        await userManager.CreateAsync(hod);
        await userManager.AddToRoleAsync(po, "Project Officer");
        await userManager.AddToRoleAsync(hod, "HoD");

        var clock = new FixedClock(new DateTimeOffset(2024, 12, 5, 8, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotUpdateService(context, userManager, clock);

        await service.SubmitAsync(4, "Testing prototype", new DateOnly(2024, 11, 30), CreatePrincipal(po.Id));
        var pending = await context.ProjectTotProgressUpdates.SingleAsync();

        var rejection = await service.DecideAsync(
            4,
            pending.Id,
            approve: false,
            decisionRemarks: "Hold until documentation is ready",
            expectedRowVersion: pending.RowVersion,
            principal: CreatePrincipal(hod.Id));

        Assert.True(rejection.IsSuccess);

        await context.Entry(pending).ReloadAsync();
        Assert.Equal(ProjectTotProgressUpdateState.Rejected, pending.State);
        Assert.Equal("Hold until documentation is ready", pending.DecisionRemarks);
        Assert.Null(pending.PublishedOnUtc);
    }

    [Fact]
    public async Task SubmitAsync_UnassignedProjectOfficer_ReturnsForbidden()
    {
        await using var context = CreateContext();
        using var userManager = CreateUserManager(context);

        context.Roles.Add(new IdentityRole { Id = "role-po", Name = "Project Officer", NormalizedName = "PROJECT OFFICER" });

        context.Projects.Add(new Project
        {
            Id = 5,
            Name = "Altair",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator",
            LeadPoUserId = "po-assigned",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        });

        var intruder = new ApplicationUser { Id = "po-intruder", UserName = "intruder" };
        await context.SaveChangesAsync();
        await userManager.CreateAsync(intruder);
        await userManager.AddToRoleAsync(intruder, "Project Officer");

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 10, 7, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotUpdateService(context, userManager, clock);

        var result = await service.SubmitAsync(5, "Attempted update", null, CreatePrincipal(intruder.Id));

        Assert.Equal(ProjectTotProgressUpdateActionStatus.Forbidden, result.Status);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext context)
    {
        return new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(context),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static ClaimsPrincipal CreatePrincipal(string userId)
        => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "Test"));

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }
}
