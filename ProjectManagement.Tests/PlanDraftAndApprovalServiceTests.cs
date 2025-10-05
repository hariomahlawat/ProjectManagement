using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
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
            Code = StageCodes.FS,
            Name = "Feasibility Study",
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

        var userContext = new StubUserContext("po-user");
        var service = new PlanDraftService(db, new TestClock(), NullLogger<PlanDraftService>.Instance, new FakeAudit(), userContext);

        var draft = await service.CreateOrGetDraftAsync(1);

        Assert.NotNull(draft);
        Assert.Equal("po-user", draft.OwnerUserId);
        Assert.NotEqual("other", draft.CreatedByUserId);
        Assert.Equal(2, draft.VersionNo);
        Assert.Single(draft.StagePlans);
        Assert.Equal(StageCodes.FS, draft.StagePlans[0].StageCode);
    }

    [Fact]
    public async Task CreateOrGetDraftAsync_AllowsIndependentDraftsPerUser()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersion,
            Code = StageCodes.FS,
            Name = "Feasibility Study",
            Sequence = 10
        });

        db.Projects.Add(new Project
        {
            Id = 2,
            Name = "Privacy",
            LeadPoUserId = "user-a"
        });

        await db.SaveChangesAsync();

        var userAContext = new StubUserContext("user-a");
        var serviceA = new PlanDraftService(db, new TestClock(), NullLogger<PlanDraftService>.Instance, new FakeAudit(), userAContext);

        var draftA = await serviceA.CreateOrGetDraftAsync(2);

        var userBContext = new StubUserContext("user-b");
        var serviceB = new PlanDraftService(db, new TestClock(), NullLogger<PlanDraftService>.Instance, new FakeAudit(), userBContext);

        var existingForB = await serviceB.GetMyDraftAsync(2);
        Assert.Null(existingForB);

        var draftB = await serviceB.CreateOrGetDraftAsync(2);

        Assert.Equal("user-a", draftA.OwnerUserId);
        Assert.Equal("user-b", draftB.OwnerUserId);
        Assert.NotEqual(draftA.Id, draftB.Id);
        Assert.Equal(2, await db.PlanVersions.CountAsync());
    }

    [Fact]
    public async Task CreateOrGetDraftAsync_ClaimsOrphanDraft()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersion,
            Code = StageCodes.FS,
            Name = "Feasibility Study",
            Sequence = 10
        });

        db.Projects.Add(new Project
        {
            Id = 3,
            Name = "Orphan",
            LeadPoUserId = "owner"
        });

        var orphan = new PlanVersion
        {
            ProjectId = 3,
            VersionNo = 1,
            Title = "Orphan",
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = "creator",
            OwnerUserId = null,
            CreatedOn = DateTimeOffset.UtcNow
        };

        orphan.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.FS,
            PlannedStart = new DateOnly(2024, 1, 1),
            PlannedDue = new DateOnly(2024, 1, 5)
        });

        db.PlanVersions.Add(orphan);
        await db.SaveChangesAsync();

        var userContext = new StubUserContext("new-owner");
        var service = new PlanDraftService(db, new TestClock(), NullLogger<PlanDraftService>.Instance, new FakeAudit(), userContext);

        var draft = await service.CreateOrGetDraftAsync(3);

        Assert.Equal(orphan.Id, draft.Id);
        Assert.Equal("new-owner", draft.OwnerUserId);
        Assert.Equal(1, await db.PlanVersions.CountAsync());
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

        var approval = new PlanApprovalService(db, new TestClock(), NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db), new NullPlanNotificationService());

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
            StageCode = StageCodes.FS,
            PlannedStart = DateOnly.FromDateTime(DateTime.Today),
            PlannedDue = DateOnly.FromDateTime(DateTime.Today.AddDays(5))
        });

        db.PlanVersions.Add(plan);
        await db.SaveChangesAsync();

        var approval = new PlanApprovalService(db, new TestClock(), NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db), new NullPlanNotificationService());

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
            StageCode = StageCodes.FS,
            PlannedStart = new DateOnly(2024, 1, 1),
            PlannedDue = new DateOnly(2024, 1, 10)
        });

        db.PlanVersions.Add(draft);
        await db.SaveChangesAsync();

        var clock = new TestClock(new DateTimeOffset(2024, 2, 1, 12, 0, 0, TimeSpan.Zero));
        var audit = new FakeAudit();
        var userContext = new StubUserContext("owner");
        var service = new PlanDraftService(db, clock, NullLogger<PlanDraftService>.Instance, audit, userContext);

        var result = await service.DeleteDraftAsync(15);

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

        var userContext = new StubUserContext("someone-else");
        var service = new PlanDraftService(db, new TestClock(), NullLogger<PlanDraftService>.Instance, new FakeAudit(), userContext);

        var result = await service.DeleteDraftAsync(22);

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

        var userContext = new StubUserContext("owner");
        var service = new PlanDraftService(db, new TestClock(), NullLogger<PlanDraftService>.Instance, new FakeAudit(), userContext);

        var result = await service.DeleteDraftAsync(30);

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

    private sealed class NullPlanNotificationService : IPlanNotificationService
    {
        public Task NotifyPlanApprovedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyPlanRejectedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyPlanSubmittedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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

    private sealed class StubUserContext : IUserContext
    {
        public StubUserContext(string? userId)
        {
            SetUser(userId);
        }

        public ClaimsPrincipal User { get; private set; } = new ClaimsPrincipal(new ClaimsIdentity());

        public string? UserId { get; private set; }

        public void SetUser(string? userId)
        {
            UserId = userId;

            if (string.IsNullOrWhiteSpace(userId))
            {
                User = new ClaimsPrincipal(new ClaimsIdentity());
                return;
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userId)
            };

            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }
    }
}
