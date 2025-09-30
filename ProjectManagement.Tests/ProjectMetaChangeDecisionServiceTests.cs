using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectMetaChangeDecisionServiceTests
{
    [Fact]
    public async Task DecideAsync_UnrelatedHod_ReturnsForbidden()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, "hod-owner");
        await SeedRequestAsync(db, 1);

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 1, 8, 0, 0, TimeSpan.Zero));
        var service = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance);

        var result = await service.DecideAsync(
            new ProjectMetaDecisionInput(1, ProjectMetaDecisionAction.Approve, null),
            new ProjectMetaDecisionUser("hod-stranger", IsAdmin: false, IsHoD: true));

        Assert.Equal(ProjectMetaDecisionOutcome.Forbidden, result.Outcome);

        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal(ProjectMetaDecisionStatuses.Pending, request.DecisionStatus);
    }

    [Fact]
    public async Task DecideAsync_AssignedHod_Succeeds()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, "hod-owner");
        await SeedRequestAsync(db, 1);

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 1, 9, 30, 0, TimeSpan.Zero));
        var service = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance);

        var result = await service.DecideAsync(
            new ProjectMetaDecisionInput(1, ProjectMetaDecisionAction.Approve, "looks good"),
            new ProjectMetaDecisionUser("hod-owner", IsAdmin: false, IsHoD: true));

        Assert.Equal(ProjectMetaDecisionOutcome.Success, result.Outcome);

        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal(ProjectMetaDecisionStatuses.Approved, request.DecisionStatus);
        Assert.Equal("looks good", request.DecisionNote);
        Assert.Equal("hod-owner", request.DecidedByUserId);
        Assert.Equal(clock.UtcNow, request.DecidedOnUtc);
    }

    [Fact]
    public async Task DecideAsync_AdminBypassesHodAssignment()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, "hod-owner");
        await SeedRequestAsync(db, 1);

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 2, 10, 0, 0, TimeSpan.Zero));
        var service = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance);

        var result = await service.DecideAsync(
            new ProjectMetaDecisionInput(1, ProjectMetaDecisionAction.Reject, "missing info"),
            new ProjectMetaDecisionUser("admin-user", IsAdmin: true, IsHoD: false));

        Assert.Equal(ProjectMetaDecisionOutcome.Success, result.Outcome);

        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal(ProjectMetaDecisionStatuses.Rejected, request.DecisionStatus);
        Assert.Equal("missing info", request.DecisionNote);
        Assert.Equal("admin-user", request.DecidedByUserId);
    }

    private static async Task SeedProjectAsync(ApplicationDbContext db, string hodUserId)
    {
        await db.Projects.AddAsync(new Project
        {
            Id = 1,
            Name = "Project",
            CreatedByUserId = "creator",
            HodUserId = hodUserId
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedRequestAsync(ApplicationDbContext db, int projectId)
    {
        await db.ProjectMetaChangeRequests.AddAsync(new ProjectMetaChangeRequest
        {
            Id = 1,
            ProjectId = projectId,
            ChangeType = "Name",
            Payload = "{}",
            DecisionStatus = ProjectMetaDecisionStatuses.Pending
        });

        await db.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
