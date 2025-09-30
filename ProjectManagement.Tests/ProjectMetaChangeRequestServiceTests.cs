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

    [Fact]
    public async Task SubmitAsync_CreatesRequestWithSnapshots()
    {
        await using var db = CreateContext();
        await db.ProjectCategories.AddAsync(new ProjectCategory
        {
            Id = 99,
            Name = "Training",
            IsActive = true
        });

        var project = new Project
        {
            Id = 10,
            Name = "Snapshot Project",
            Description = "Snapshot Description",
            CaseFileNumber = "CF-777",
            CategoryId = 99,
            CreatedByUserId = "creator",
            LeadPoUserId = "po-user",
            RowVersion = new byte[] { 1, 2, 3, 4 }
        };

        await db.Projects.AddAsync(project);
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new ProjectMetaChangeRequestService(db, clock);

        var submission = new ProjectMetaChangeRequestSubmission
        {
            ProjectId = 10,
            Name = "Snapshot Project",
            Description = "Snapshot Description",
            CaseFileNumber = "CF-777",
            CategoryId = 99
        };

        var result = await service.SubmitAsync(submission, "po-user", CancellationToken.None);

        Assert.Equal(ProjectMetaChangeRequestSubmissionOutcome.Success, result.Outcome);
        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal("Snapshot Project", request.OriginalName);
        Assert.Equal("Snapshot Description", request.OriginalDescription);
        Assert.Equal("CF-777", request.OriginalCaseFileNumber);
        Assert.Equal(99, request.OriginalCategoryId);
        Assert.NotNull(request.OriginalRowVersion);
        Assert.NotSame(project.RowVersion, request.OriginalRowVersion);
        Assert.Equal(project.RowVersion, request.OriginalRowVersion);
    }

    [Fact]
    public async Task SubmitAsync_InactiveCategory_ReturnsValidationError()
    {
        await using var db = CreateContext();
        await db.ProjectCategories.AddAsync(new ProjectCategory
        {
            Id = 10,
            Name = "Old",
            IsActive = false
        });

        await db.Projects.AddAsync(new Project
        {
            Id = 3,
            Name = "Editable",
            CreatedByUserId = "creator",
            LeadPoUserId = "po-user"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new ProjectMetaChangeRequestService(db, clock);

        var submission = new ProjectMetaChangeRequestSubmission
        {
            ProjectId = 3,
            Name = "Editable",
            CategoryId = 10
        };

        var result = await service.SubmitAsync(submission, "po-user", CancellationToken.None);

        Assert.Equal(ProjectMetaChangeRequestSubmissionOutcome.ValidationFailed, result.Outcome);
        Assert.True(result.Errors.TryGetValue("CategoryId", out var errors));
        var message = Assert.Single(errors);
        Assert.Equal(ProjectValidationMessages.InactiveCategory, message);
        Assert.Equal(0, await db.ProjectMetaChangeRequests.CountAsync());
    }

    [Fact]
    public async Task SubmitAsync_ReplacesExistingPendingRequest()
    {
        await using var db = CreateContext();
        await db.ProjectCategories.AddAsync(new ProjectCategory
        {
            Id = 20,
            Name = "Active",
            IsActive = true
        });

        await db.Projects.AddAsync(new Project
        {
            Id = 4,
            Name = "Project",
            Description = "Old",
            CreatedByUserId = "creator",
            LeadPoUserId = "po-user"
        });

        await db.ProjectMetaChangeRequests.AddAsync(new ProjectMetaChangeRequest
        {
            Id = 1,
            ProjectId = 4,
            ChangeType = ProjectMetaChangeRequestChangeTypes.Meta,
            Payload = "{\"name\":\"Project\"}",
            DecisionStatus = ProjectMetaDecisionStatuses.Pending,
            RequestedByUserId = "po-user",
            RequestedOnUtc = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 2, 0, 0, 0, TimeSpan.Zero));
        var service = new ProjectMetaChangeRequestService(db, clock);

        var submission = new ProjectMetaChangeRequestSubmission
        {
            ProjectId = 4,
            Name = "Updated",
            Description = "New description",
            CategoryId = 20,
            Reason = "Need to update details"
        };

        var result = await service.SubmitAsync(submission, "po-user", CancellationToken.None);

        Assert.Equal(ProjectMetaChangeRequestSubmissionOutcome.Success, result.Outcome);
        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal(1, request.Id);
        Assert.Equal(ProjectMetaDecisionStatuses.Pending, request.DecisionStatus);
        Assert.Equal("po-user", request.RequestedByUserId);
        Assert.Equal(clock.UtcNow, request.RequestedOnUtc);
        Assert.Equal("Need to update details", request.RequestNote);
        Assert.Equal("Project", request.OriginalName);
        Assert.Equal("Old", request.OriginalDescription);
        Assert.Null(request.OriginalCaseFileNumber);
        Assert.Null(request.OriginalCategoryId);

        var payload = System.Text.Json.JsonSerializer.Deserialize<ProjectMetaChangeRequestPayload>(request.Payload);
        Assert.NotNull(payload);
        Assert.Equal("Updated", payload!.Name);
        Assert.Equal("New description", payload.Description);
        Assert.Equal(20, payload.CategoryId);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
