using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
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

        var service = new PlanDraftService(db, new TestClock(), NullLogger<PlanDraftService>.Instance, new FakeAudit());

        var draft = await service.CreateOrGetDraftAsync(1, "po-user");

        Assert.NotNull(draft);
        Assert.Equal("po-user", draft.OwnerUserId);
        Assert.NotEqual("other", draft.CreatedByUserId);
        Assert.Equal(2, draft.VersionNo);
        Assert.Single(draft.StagePlans);
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

    [Fact]
    public async Task DeleteDraftAsync_RemovesDraftForOwner()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.Projects.Add(new Project
        {
            Id = 15,
            Name = "Draft",
            LeadPoUserId = "owner"
        });

        var draft = new PlanVersion
        {
            ProjectId = 15,
            VersionNo = 1,
            Title = "My Draft",
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = "owner",
            OwnerUserId = "owner",
            CreatedOn = DateTimeOffset.UtcNow
        };

        draft.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.EOI,
            PlannedStart = new DateOnly(2024, 1, 1),
            PlannedDue = new DateOnly(2024, 1, 10)
        });

        db.PlanVersions.Add(draft);
        await db.SaveChangesAsync();

        var clock = new TestClock(new DateTimeOffset(2024, 2, 1, 12, 0, 0, TimeSpan.Zero));
        var audit = new FakeAudit();
        var service = new PlanDraftService(db, clock, NullLogger<PlanDraftService>.Instance, audit);

        var result = await service.DeleteDraftAsync(15, "owner");

        Assert.Equal(PlanDraftDeleteResult.Success, result);
        Assert.Empty(await db.PlanVersions.ToListAsync());
        Assert.Empty(await db.StagePlans.ToListAsync());
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("Plan.DraftDeleted", entry.Action);
        Assert.Equal("owner", entry.UserId);
        Assert.Equal("15", entry.Data["ProjectId"]);
    }

    [Fact]
    public async Task DeleteDraftAsync_DoesNotAllowOtherUsers()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.PlanVersions.Add(new PlanVersion
        {
            ProjectId = 22,
            VersionNo = 1,
            Title = "Draft",
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = "owner",
            OwnerUserId = "owner",
            CreatedOn = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        var service = new PlanDraftService(db, new TestClock(), NullLogger<PlanDraftService>.Instance, new FakeAudit());

        var result = await service.DeleteDraftAsync(22, "someone-else");

        Assert.Equal(PlanDraftDeleteResult.NotFound, result);
        Assert.Single(await db.PlanVersions.ToListAsync());
    }

    [Fact]
    public async Task DeleteDraftAsync_DoesNotDeleteSubmittedPlan()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.PlanVersions.Add(new PlanVersion
        {
            ProjectId = 30,
            VersionNo = 1,
            Title = "Submitted",
            Status = PlanVersionStatus.PendingApproval,
            CreatedByUserId = "owner",
            OwnerUserId = "owner",
            CreatedOn = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        var service = new PlanDraftService(db, new TestClock(), NullLogger<PlanDraftService>.Instance, new FakeAudit());

        var result = await service.DeleteDraftAsync(30, "owner");

        Assert.Equal(PlanDraftDeleteResult.Conflict, result);
        Assert.Single(await db.PlanVersions.ToListAsync());
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset? now = null)
        {
            UtcNow = now ?? DateTimeOffset.UtcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeAudit : IAuditService
    {
        public List<(string Action, IDictionary<string, string?> Data, string? UserId)> Entries { get; } = new();

        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, Microsoft.AspNetCore.Http.HttpContext? http = null)
        {
            Entries.Add((action, data ?? new Dictionary<string, string?>(), userId));
            return Task.CompletedTask;
        }
    }
}
