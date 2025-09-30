using System;
using System.Threading;
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
    public async Task SubmitAsync_DuplicateCaseFileNumber_ReturnsValidationError()
    {
        await using var db = CreateContext();
        await db.Projects.AddRangeAsync(
            new Project
            {
                Id = 1,
                Name = "Existing",
                CaseFileNumber = "CF-999",
                CreatedByUserId = "creator"
            },
            new Project
            {
                Id = 2,
                Name = "Editable",
                CreatedByUserId = "creator",
                LeadPoUserId = "po-user"
            });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new ProjectMetaChangeRequestService(db, clock);

        var submission = new ProjectMetaChangeRequestSubmission
        {
            ProjectId = 2,
            Name = "Editable",
            Description = "Updated description",
            CaseFileNumber = "  CF-999  "
        };

        var result = await service.SubmitAsync(submission, "po-user", CancellationToken.None);

        Assert.Equal(ProjectMetaChangeRequestSubmissionOutcome.ValidationFailed, result.Outcome);
        Assert.True(result.Errors.TryGetValue("CaseFileNumber", out var errors));
        var message = Assert.Single(errors);
        Assert.Equal(ProjectValidationMessages.DuplicateCaseFileNumber, message);
        Assert.Equal(0, await db.ProjectMetaChangeRequests.CountAsync());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
