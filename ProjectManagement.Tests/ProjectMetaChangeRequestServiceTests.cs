using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public class ProjectMetaChangeRequestServiceTests
{
    [Fact]
    public async Task SubmitAsync_FlagsDuplicateCaseFileNumber()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        db.Projects.AddRange(
            new Project
            {
                Name = "Original",
                CreatedByUserId = "owner",
                CaseFileNumber = "CF-001"
            },
            new Project
            {
                Name = "Existing",
                CreatedByUserId = "owner",
                CaseFileNumber = "CF-123"
            });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var service = new ProjectMetaChangeRequestService(db, clock);

        var project = await db.Projects.FirstAsync(p => p.CaseFileNumber == "CF-001");

        var result = await service.SubmitAsync(new ProjectMetaChangeRequestInput
        {
            ProjectId = project.Id,
            ProposedCaseFileNumber = "  CF-123  ",
            RequestedByUserId = "po"
        });

        Assert.Equal(ProjectMetaChangeRequestOutcome.ValidationFailed, result.Outcome);
        Assert.True(result.Errors.TryGetValue("CaseFileNumber", out var errors));
        Assert.Contains("Case file number already exists.", errors);
        Assert.Empty(db.ProjectMetaChangeRequests);
    }

    [Fact]
    public async Task DirectEditAsync_FlagsDuplicateCaseFileNumber()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        db.Projects.AddRange(
            new Project
            {
                Name = "Target",
                CreatedByUserId = "owner",
                CaseFileNumber = "CF-111"
            },
            new Project
            {
                Name = "Other",
                CreatedByUserId = "owner",
                CaseFileNumber = "CF-222"
            });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(DateTimeOffset.UtcNow);
        var service = new ProjectMetaChangeRequestService(db, clock);

        var target = await db.Projects.FirstAsync(p => p.CaseFileNumber == "CF-111");

        var result = await service.DirectEditAsync(new ProjectMetaDirectEditInput
        {
            ProjectId = target.Id,
            CaseFileNumber = "CF-222"
        });

        Assert.Equal(ProjectMetaDirectEditOutcome.ValidationFailed, result.Outcome);
        Assert.True(result.Errors.TryGetValue("CaseFileNumber", out var errors));
        Assert.Contains("Case file number already exists.", errors);
        Assert.Equal("CF-111", (await db.Projects.FindAsync(target.Id))!.CaseFileNumber);
    }
}
