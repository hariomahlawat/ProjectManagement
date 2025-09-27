using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public class PlanDraftAndApprovalServiceTests
{
    [Fact]
    public async Task CreateOrGetDraft_IsScopedToCurrentUser()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersion,
            Code = StageCodes.EOI,
            Name = "Expression of Interest",
            Sequence = 10
        });

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Test Project",
            LeadPoUserId = "po-user"
        });

        db.PlanVersions.Add(new PlanVersion
        {
            ProjectId = 1,
            VersionNo = 1,
            Title = "Existing",
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = "other",
            OwnerUserId = "other",
            CreatedOn = DateTimeOffset.UtcNow.AddDays(-2)
        });

        await db.SaveChangesAsync();

        var service = new PlanDraftService(db, new TestClock(), NullLogger<PlanDraftService>.Instance);

        var draft = await service.CreateOrGetDraftAsync(1, "po-user");

        Assert.NotNull(draft);
        Assert.Equal("po-user", draft.OwnerUserId);
        Assert.NotEqual("other", draft.CreatedByUserId);
        Assert.Equal(2, draft.VersionNo);
        Assert.Equal(1, draft.StagePlans.Count);
        Assert.Equal(StageCodes.EOI, draft.StagePlans[0].StageCode);
    }

    [Fact]
    public async Task SubmitForApprovalBlocksWhenAnotherPendingExists()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.Projects.Add(new Project
        {
            Id = 7,
            Name = "Blocked",
            LeadPoUserId = "po-owner"
        });

        db.PlanVersions.Add(new PlanVersion
        {
            ProjectId = 7,
            VersionNo = 1,
            Title = "Pending",
            Status = PlanVersionStatus.PendingApproval,
            CreatedByUserId = "po-owner",
            OwnerUserId = "po-owner",
            CreatedOn = DateTimeOffset.UtcNow.AddDays(-1),
            SubmittedByUserId = "po-owner",
            SubmittedOn = DateTimeOffset.UtcNow.AddHours(-6)
        });

        db.PlanVersions.Add(new PlanVersion
        {
            ProjectId = 7,
            VersionNo = 2,
            Title = "My Draft",
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = "new-user",
            OwnerUserId = "new-user",
            CreatedOn = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        var approval = new PlanApprovalService(db, new TestClock(), NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db));

        await Assert.ThrowsAsync<DomainException>(() => approval.SubmitForApprovalAsync(7, "new-user"));
    }

    [Fact]
    public async Task ApproveLatestDraftAsync_PreventsSelfApproval()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.Projects.Add(new Project
        {
            Id = 9,
            Name = "Self Approval",
            LeadPoUserId = "po-user"
        });

        var plan = new PlanVersion
        {
            ProjectId = 9,
            VersionNo = 1,
            Title = "Pending",
            Status = PlanVersionStatus.PendingApproval,
            CreatedByUserId = "po-user",
            OwnerUserId = "hod-user",
            SubmittedByUserId = "hod-user",
            SubmittedOn = DateTimeOffset.UtcNow,
            CreatedOn = DateTimeOffset.UtcNow.AddDays(-3)
        };

        plan.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.EOI,
            PlannedStart = DateOnly.FromDateTime(DateTime.Today),
            PlannedDue = DateOnly.FromDateTime(DateTime.Today.AddDays(5))
        });

        db.PlanVersions.Add(plan);
        await db.SaveChangesAsync();

        var approval = new PlanApprovalService(db, new TestClock(), NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db));

        await Assert.ThrowsAsync<ForbiddenException>(() => approval.ApproveLatestDraftAsync(9, "hod-user"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
