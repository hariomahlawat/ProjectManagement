using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class MetaRequestFlowTests
{
    [Fact]
    public async Task PoRequestToHodApproveHappyPath()
    {
        await using var db = CreateContext();
        await db.ProjectCategories.AddAsync(new ProjectCategory
        {
            Id = 10,
            Name = "Simulation",
            IsActive = true
        });
        await db.TechnicalCategories.AddAsync(new TechnicalCategory
        {
            Id = 50,
            Name = "Networks",
            IsActive = true
        });

        await db.Projects.AddAsync(new Project
        {
            Id = 1,
            Name = "Alpha",
            Description = "Original",
            CaseFileNumber = "CF-101",
            CategoryId = 10,
            TechnicalCategoryId = 50,
            CreatedByUserId = "creator",
            LeadPoUserId = "po-user",
            HodUserId = "hod-user",
            RowVersion = new byte[] { 1, 0, 0 }
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var requestService = new ProjectMetaChangeRequestService(db, clock);

        var submission = new ProjectMetaChangeRequestSubmission
        {
            ProjectId = 1,
            Name = "Alpha",
            Description = "Original",
            CaseFileNumber = "CF-101",
            CategoryId = 10,
            TechnicalCategoryId = 50,
            Reason = "Routine refresh"
        };

        var submissionResult = await requestService.SubmitAsync(submission, "po-user", CancellationToken.None);
        Assert.Equal(ProjectMetaChangeRequestSubmissionOutcome.Success, submissionResult.Outcome);

        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal("Alpha", request.OriginalName);
        Assert.Equal("Original", request.OriginalDescription);
        Assert.Equal("CF-101", request.OriginalCaseFileNumber);
        Assert.Equal(10, request.OriginalCategoryId);
        Assert.Equal(50, request.OriginalTechnicalCategoryId);
        Assert.Equal(50, request.TechnicalCategoryId);

        clock.Set(clock.UtcNow.AddHours(2));
        var audit = new RecordingAudit();
        var decisionService = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance, audit);

        var decisionResult = await decisionService.DecideAsync(
            new ProjectMetaDecisionInput(request.Id, ProjectMetaDecisionAction.Approve, "looks good"),
            new ProjectMetaDecisionUser("hod-user", IsAdmin: false, IsHoD: true));

        Assert.Equal(ProjectMetaDecisionOutcome.Success, decisionResult.Outcome);

        var project = await db.Projects.SingleAsync();
        Assert.Equal("Alpha", project.Name);
        Assert.Equal("Original", project.Description);
        Assert.Equal("CF-101", project.CaseFileNumber);
        Assert.Equal(10, project.CategoryId);
        Assert.Equal(50, project.TechnicalCategoryId);

        var approvalHeader = audit.Entries.Single(e => e.Action == "Projects.MetaChangeApproved");
        Assert.Equal("false", approvalHeader.Data.TryGetValue("DriftDetected", out var detected) ? detected : null);
    }

    [Fact]
    public async Task DirectEditBeforeApprovalFlagsDrift()
    {
        await using var db = CreateContext();
        await db.ProjectCategories.AddAsync(new ProjectCategory
        {
            Id = 11,
            Name = "Training",
            IsActive = true
        });
        await db.TechnicalCategories.AddRangeAsync(
            new TechnicalCategory
            {
                Id = 60,
                Name = "Design",
                IsActive = true
            },
            new TechnicalCategory
            {
                Id = 61,
                Name = "Development",
                IsActive = true
            });

        await db.Projects.AddAsync(new Project
        {
            Id = 2,
            Name = "Bravo",
            Description = "Baseline",
            TechnicalCategoryId = 60,
            CreatedByUserId = "creator",
            LeadPoUserId = "po-user",
            HodUserId = "hod-user",
            RowVersion = new byte[] { 1, 1, 1 }
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 2, 9, 0, 0, TimeSpan.Zero));
        var requestService = new ProjectMetaChangeRequestService(db, clock);

        var submission = new ProjectMetaChangeRequestSubmission
        {
            ProjectId = 2,
            Name = "Bravo",
            Description = "Baseline",
            CaseFileNumber = null,
            CategoryId = null,
            TechnicalCategoryId = 60
        };

        var result = await requestService.SubmitAsync(submission, "po-user", CancellationToken.None);
        Assert.Equal(ProjectMetaChangeRequestSubmissionOutcome.Success, result.Outcome);

        var project = await db.Projects.SingleAsync(p => p.Id == 2);
        project.Name = "Bravo v2";
        project.TechnicalCategoryId = 61;
        project.RowVersion = new byte[] { 2, 2, 2 };
        await db.SaveChangesAsync();

        clock.Set(clock.UtcNow.AddHours(1));
        var audit = new RecordingAudit();
        var decisionService = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance, audit);

        var decision = await decisionService.DecideAsync(
            new ProjectMetaDecisionInput(result.RequestId!.Value, ProjectMetaDecisionAction.Approve, null),
            new ProjectMetaDecisionUser("hod-user", IsAdmin: false, IsHoD: true));

        Assert.Equal(ProjectMetaDecisionOutcome.Success, decision.Outcome);

        var header = audit.Entries.Single(e => e.Action == "Projects.MetaChangeApproved");
        Assert.Equal("true", header.Data.TryGetValue("DriftDetected", out var detected) ? detected : null);
        var driftFields = (header.Data.TryGetValue("DriftFields", out var fields) ? fields : string.Empty) ?? string.Empty;
        Assert.Contains("Name", driftFields.Split(',', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("ProjectRecord", driftFields.Split(',', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(ProjectMetaChangeDriftFields.TechnicalCategory, driftFields.Split(',', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task PendingUniquenessIsEnforced()
    {
        await using var db = CreateContext();
        await db.Projects.AddAsync(new Project
        {
            Id = 3,
            Name = "Charlie",
            Description = "Baseline",
            CreatedByUserId = "creator",
            LeadPoUserId = "po-user",
            HodUserId = "hod-user"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 3, 9, 0, 0, TimeSpan.Zero));
        var service = new ProjectMetaChangeRequestService(db, clock);

        var first = await service.SubmitAsync(new ProjectMetaChangeRequestSubmission
        {
            ProjectId = 3,
            Name = "Charlie",
            Description = "Baseline"
        }, "po-user", CancellationToken.None);

        Assert.Equal(ProjectMetaChangeRequestSubmissionOutcome.Success, first.Outcome);

        clock.Set(clock.UtcNow.AddHours(1));
        var second = await service.SubmitAsync(new ProjectMetaChangeRequestSubmission
        {
            ProjectId = 3,
            Name = "Charlie Updated",
            Description = "Revised"
        }, "po-user", CancellationToken.None);

        Assert.Equal(ProjectMetaChangeRequestSubmissionOutcome.Success, second.Outcome);
        Assert.Equal(first.RequestId, second.RequestId);

        var request = await db.ProjectMetaChangeRequests.SingleAsync();
        Assert.Equal("Charlie", request.OriginalName);
        Assert.Equal("Baseline", request.OriginalDescription);
        Assert.Contains("Charlie Updated", request.Payload);
    }

    [Fact]
    public async Task CategoryDeactivatesBetweenRequestAndApproveBlocksDecision()
    {
        await using var db = CreateContext();
        await db.ProjectCategories.AddAsync(new ProjectCategory
        {
            Id = 12,
            Name = "Ops",
            IsActive = true
        });

        await db.Projects.AddAsync(new Project
        {
            Id = 4,
            Name = "Delta",
            CreatedByUserId = "creator",
            LeadPoUserId = "po-user",
            HodUserId = "hod-user"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 4, 9, 0, 0, TimeSpan.Zero));
        var requestService = new ProjectMetaChangeRequestService(db, clock);

        var submission = await requestService.SubmitAsync(new ProjectMetaChangeRequestSubmission
        {
            ProjectId = 4,
            Name = "Delta",
            CategoryId = 12
        }, "po-user", CancellationToken.None);

        Assert.Equal(ProjectMetaChangeRequestSubmissionOutcome.Success, submission.Outcome);

        var category = await db.ProjectCategories.SingleAsync(c => c.Id == 12);
        category.IsActive = false;
        await db.SaveChangesAsync();

        clock.Set(clock.UtcNow.AddHours(3));
        var audit = new RecordingAudit();
        var decisionService = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance, audit);

        var decision = await decisionService.DecideAsync(
            new ProjectMetaDecisionInput(submission.RequestId!.Value, ProjectMetaDecisionAction.Approve, null),
            new ProjectMetaDecisionUser("hod-user", IsAdmin: false, IsHoD: true));

        Assert.Equal(ProjectMetaDecisionOutcome.ValidationFailed, decision.Outcome);
        Assert.Equal(ProjectValidationMessages.InactiveCategory, decision.Error);
    }

    [Fact]
    public async Task TechnicalCategoryDeactivatesBetweenRequestAndApproveBlocksDecision()
    {
        await using var db = CreateContext();
        await db.TechnicalCategories.AddAsync(new TechnicalCategory
        {
            Id = 70,
            Name = "Legacy",
            IsActive = true
        });

        await db.Projects.AddAsync(new Project
        {
            Id = 5,
            Name = "Echo",
            CreatedByUserId = "creator",
            LeadPoUserId = "po-user",
            HodUserId = "hod-user"
        });
        await db.SaveChangesAsync();

        var clock = FakeClock.AtUtc(new DateTimeOffset(2024, 10, 5, 9, 0, 0, TimeSpan.Zero));
        var requestService = new ProjectMetaChangeRequestService(db, clock);

        var submission = await requestService.SubmitAsync(new ProjectMetaChangeRequestSubmission
        {
            ProjectId = 5,
            Name = "Echo",
            TechnicalCategoryId = 70
        }, "po-user", CancellationToken.None);

        Assert.Equal(ProjectMetaChangeRequestSubmissionOutcome.Success, submission.Outcome);

        var techCategory = await db.TechnicalCategories.SingleAsync(c => c.Id == 70);
        techCategory.IsActive = false;
        await db.SaveChangesAsync();

        clock.Set(clock.UtcNow.AddHours(3));
        var audit = new RecordingAudit();
        var decisionService = new ProjectMetaChangeDecisionService(db, clock, NullLogger<ProjectMetaChangeDecisionService>.Instance, audit);

        var decision = await decisionService.DecideAsync(
            new ProjectMetaDecisionInput(submission.RequestId!.Value, ProjectMetaDecisionAction.Approve, null),
            new ProjectMetaDecisionUser("hod-user", IsAdmin: false, IsHoD: true));

        Assert.Equal(ProjectMetaDecisionOutcome.ValidationFailed, decision.Outcome);
        Assert.Equal(ProjectValidationMessages.InactiveTechnicalCategory, decision.Error);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
