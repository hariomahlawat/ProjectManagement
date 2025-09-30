using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectMetaChangeRequestServiceTests
{
    [Fact]
    public async Task CreateAsync_DuplicateCaseFileNumber_ReturnsValidationFailed()
    {
        await using var db = CreateContext();
        await db.Projects.AddRangeAsync(
            new Project
            {
                Id = 1,
                Name = "Existing",
                CreatedByUserId = "creator",
                CaseFileNumber = "CF-100"
            },
            new Project
            {
                Id = 2,
                Name = "Target",
                CreatedByUserId = "creator",
                LeadPoUserId = "po-2"
            });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 5, 9, 0, 0, TimeSpan.Zero));
        var service = new ProjectMetaChangeRequestService(db, clock);

        var input = new ProjectMetaChangeRequestInput(
            ProjectId: 2,
            RequestedByUserId: "po-2",
            ChangeType: "Meta",
            Payload: "{}",
            ProposedCaseFileNumber: "  cf-100  ");

        var result = await service.CreateAsync(input);

        Assert.Equal(ProjectMetaChangeRequestOutcome.ValidationFailed, result.Outcome);
        Assert.Equal("Case file number already exists.", result.Error);
        Assert.False(result.RequestId.HasValue);
        Assert.Equal(0, await db.ProjectMetaChangeRequests.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_UniqueCaseFileNumber_PersistsRequest()
    {
        await using var db = CreateContext();
        await db.Projects.AddAsync(new Project
        {
            Id = 5,
            Name = "Target",
            CreatedByUserId = "creator",
            LeadPoUserId = "po-5"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 5, 10, 30, 0, TimeSpan.Zero));
        var service = new ProjectMetaChangeRequestService(db, clock);

        var input = new ProjectMetaChangeRequestInput(
            ProjectId: 5,
            RequestedByUserId: "po-5",
            ChangeType: "Meta",
            Payload: "{\"name\":\"Updated\"}",
            ProposedCaseFileNumber: "CF-999");

        var result = await service.CreateAsync(input);

        Assert.Equal(ProjectMetaChangeRequestOutcome.Success, result.Outcome);
        Assert.NotNull(result.RequestId);

        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal(5, request.ProjectId);
        Assert.Equal("Meta", request.ChangeType);
        Assert.Equal("{\"name\":\"Updated\"}", request.Payload);
        Assert.Equal("po-5", request.RequestedByUserId);
        Assert.Equal(clock.UtcNow, request.RequestedOnUtc);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
