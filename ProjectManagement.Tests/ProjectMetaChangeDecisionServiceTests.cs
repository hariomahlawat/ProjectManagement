using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
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
        await SeedRequestAsync(db, 1, payloadName: "Project");

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 1, 8, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var service = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance, audit);

        var result = await service.DecideAsync(
            new ProjectMetaDecisionInput(1, ProjectMetaDecisionAction.Approve, null),
            new ProjectMetaDecisionUser("hod-stranger", IsAdmin: false, IsHoD: true));

        Assert.Equal(ProjectMetaDecisionOutcome.Forbidden, result.Outcome);

        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal(ProjectMetaDecisionStatuses.Pending, request.DecisionStatus);
    }

    [Fact]
    public async Task DecideAsync_AssignedHod_SucceedsAndUpdatesRequest()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, "hod-owner");
        await SeedRequestAsync(db, 1, payloadName: "Project");

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 1, 9, 30, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var service = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance, audit);

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
        await SeedRequestAsync(db, 1, payloadName: "Project");

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 2, 10, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var service = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance, audit);

        var result = await service.DecideAsync(
            new ProjectMetaDecisionInput(1, ProjectMetaDecisionAction.Reject, "missing info"),
            new ProjectMetaDecisionUser("admin-user", IsAdmin: true, IsHoD: false));

        Assert.Equal(ProjectMetaDecisionOutcome.Success, result.Outcome);

        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal(ProjectMetaDecisionStatuses.Rejected, request.DecisionStatus);
        Assert.Equal("missing info", request.DecisionNote);
        Assert.Equal("admin-user", request.DecidedByUserId);
    }

    [Fact]
    public async Task DecideAsync_ApproveAppliesChangesToProject()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, "hod-owner");
        await db.ProjectCategories.AddAsync(new ProjectCategory
        {
            Id = 5,
            Name = "Infrastructure",
            IsActive = true
        });
        await db.SaveChangesAsync();
        await SeedRequestAsync(db, 1, payloadName: "Updated", payloadDescription: "New", payloadCaseFile: "CF-100", payloadCategoryId: 5);

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 3, 9, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var service = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance, audit);

        var result = await service.DecideAsync(
            new ProjectMetaDecisionInput(1, ProjectMetaDecisionAction.Approve, null),
            new ProjectMetaDecisionUser("hod-owner", IsAdmin: false, IsHoD: true));

        Assert.Equal(ProjectMetaDecisionOutcome.Success, result.Outcome);

        var project = await db.Projects.SingleAsync();
        Assert.Equal("Updated", project.Name);
        Assert.Equal("New", project.Description);
        Assert.Equal("CF-100", project.CaseFileNumber);
        Assert.Equal(5, project.CategoryId);

        var header = audit.Entries.Single(e => e.Action == "Projects.MetaChangeApproved");
        Assert.Equal("false", header.Data.TryGetValue("DriftDetected", out var detected) ? detected : null);
        Assert.True(string.IsNullOrEmpty(header.Data.TryGetValue("DriftFields", out var fields) ? fields : null));

        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal(ProjectMetaDecisionStatuses.Approved, request.DecisionStatus);
    }

    [Fact]
    public async Task DecideAsync_ApproveWithDriftRecordsFields()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, "hod-owner");
        var project = await db.Projects.SingleAsync();
        project.RowVersion = new byte[] { 1, 2, 3 };
        await db.SaveChangesAsync();

        await SeedRequestAsync(db, 1, payloadName: "Updated", originalName: "Project", originalRowVersion: new byte[] { 1, 2, 3 });

        project = await db.Projects.SingleAsync();
        project.Name = "Project Direct Edit";
        project.RowVersion = new byte[] { 9, 9, 9 };
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 6, 9, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var service = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance, audit);

        var result = await service.DecideAsync(
            new ProjectMetaDecisionInput(1, ProjectMetaDecisionAction.Approve, null),
            new ProjectMetaDecisionUser("hod-owner", IsAdmin: false, IsHoD: true));

        Assert.Equal(ProjectMetaDecisionOutcome.Success, result.Outcome);

        var header = audit.Entries.Single(e => e.Action == "Projects.MetaChangeApproved");
        Assert.Equal("true", header.Data.TryGetValue("DriftDetected", out var detected) ? detected : null);
        var fields = (header.Data.TryGetValue("DriftFields", out var drift) ? drift : string.Empty) ?? string.Empty;
        Assert.Contains("Name", fields.Split(',', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("ProjectRecord", fields.Split(',', StringSplitOptions.RemoveEmptyEntries));

        var storedRequest = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal(ProjectMetaDecisionStatuses.Approved, storedRequest.DecisionStatus);

        var updatedProject = await db.Projects.SingleAsync();
        Assert.Equal("Updated", updatedProject.Name);
    }

    [Fact]
    public async Task DecideAsync_RejectLeavesProjectUnchanged()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, "hod-owner");
        await SeedRequestAsync(db, 1, payloadName: "Updated");

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 7, 9, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var service = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance, audit);

        var result = await service.DecideAsync(
            new ProjectMetaDecisionInput(1, ProjectMetaDecisionAction.Reject, "insufficient info"),
            new ProjectMetaDecisionUser("hod-owner", IsAdmin: false, IsHoD: true));

        Assert.Equal(ProjectMetaDecisionOutcome.Success, result.Outcome);

        var project = await db.Projects.SingleAsync();
        Assert.Equal("Project", project.Name);

        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal(ProjectMetaDecisionStatuses.Rejected, request.DecisionStatus);
        Assert.Equal("insufficient info", request.DecisionNote);

        var rejection = audit.Entries.Single(e => e.Action == "Projects.MetaChangeRejected");
        Assert.Equal("hod-owner", rejection.UserId);
    }

    [Fact]
    public async Task DecideAsync_ApproveInactiveCategory_ReturnsValidationError()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, "hod-owner");
        await db.ProjectCategories.AddAsync(new ProjectCategory
        {
            Id = 6,
            Name = "Old",
            IsActive = false
        });
        await db.SaveChangesAsync();
        await SeedRequestAsync(db, 1, payloadName: "Updated", payloadCategoryId: 6);

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 4, 9, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var service = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance, audit);

        var result = await service.DecideAsync(
            new ProjectMetaDecisionInput(1, ProjectMetaDecisionAction.Approve, null),
            new ProjectMetaDecisionUser("hod-owner", IsAdmin: false, IsHoD: true));

        Assert.Equal(ProjectMetaDecisionOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(ProjectValidationMessages.InactiveCategory, result.Error);
    }

    [Fact]
    public async Task DecideAsync_ApproveDuplicateCaseFile_ReturnsValidationError()
    {
        await using var db = CreateContext();
        await SeedProjectAsync(db, "hod-owner");
        await db.Projects.AddAsync(new Project
        {
            Id = 2,
            Name = "Other",
            CreatedByUserId = "creator",
            CaseFileNumber = "CF-200"
        });
        await db.SaveChangesAsync();
        await SeedRequestAsync(db, 1, payloadName: "Updated", payloadCaseFile: "CF-200");

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 5, 9, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAudit();
        var service = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance, audit);

        var result = await service.DecideAsync(
            new ProjectMetaDecisionInput(1, ProjectMetaDecisionAction.Approve, null),
            new ProjectMetaDecisionUser("hod-owner", IsAdmin: false, IsHoD: true));

        Assert.Equal(ProjectMetaDecisionOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(ProjectValidationMessages.DuplicateCaseFileNumber, result.Error);
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

    private static async Task SeedRequestAsync(
        ApplicationDbContext db,
        int projectId,
        string payloadName,
        string? payloadDescription = null,
        string? payloadCaseFile = null,
        int? payloadCategoryId = null,
        string? originalName = null,
        string? originalDescription = null,
        string? originalCaseFile = null,
        int? originalCategoryId = null,
        byte[]? originalRowVersion = null)
    {
        var payload = JsonSerializer.Serialize(new ProjectMetaChangeRequestPayload
        {
            Name = payloadName,
            Description = payloadDescription,
            CaseFileNumber = payloadCaseFile,
            CategoryId = payloadCategoryId
        });

        originalName ??= "Project";

        await db.ProjectMetaChangeRequests.AddAsync(new ProjectMetaChangeRequest
        {
            Id = 1,
            ProjectId = projectId,
            ChangeType = ProjectMetaChangeRequestChangeTypes.Meta,
            Payload = payload,
            DecisionStatus = ProjectMetaDecisionStatuses.Pending,
            RequestedByUserId = "po-user",
            RequestedOnUtc = DateTimeOffset.UtcNow,
            OriginalName = originalName!,
            OriginalDescription = originalDescription,
            OriginalCaseFileNumber = originalCaseFile,
            OriginalCategoryId = originalCategoryId,
            OriginalRowVersion = originalRowVersion
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
