using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Helpers;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public class PlanDraftAndApprovalServiceTests
{
    [Fact]
    public async Task C2_OpeningAndSavingUnchangedSuccessorPreservesExactPendingReview()
    {
        await using var db = NewC2Database();
        var submitted = await SeedC2PendingAsync(db);
        var drafts = new PlanDraftService(db, new TestClock(),
            NullLogger<PlanDraftService>.Instance, new FakeAudit(), new StubUserContext("po-owner"));

        var successor = await drafts.CreateOrGetDraftAsync(91);
        Assert.Equal(2, successor.VersionNo);
        Assert.Equal(PlanVersionStatus.Draft, successor.Status);
        Assert.Equal(submitted.AnchorDate, successor.AnchorDate);
        Assert.Equal(submitted.PncApplicable, successor.PncApplicable);
        Assert.Equal(submitted.SkipHolidays, successor.SkipHolidays);
        Assert.Equal(submitted.StagePlans[0].PlannedDue, successor.StagePlans[0].PlannedDue);
        Assert.NotEqual(submitted.StagePlans[0].Id, successor.StagePlans[0].Id);
        Assert.False(await drafts.SupersedePendingAfterSaveAsync(successor));
        Assert.Equal(PlanVersionStatus.PendingApproval, submitted.Status);
        Assert.Equal(submitted.Id, (await PlanRevisionConsistency.ActionablePending(db)
            .SingleAsync(p => p.ProjectId == 91)).Id);
        Assert.Empty(await db.PlanApprovalLogs.ToListAsync());
        Assert.Equal(successor.Id, (await drafts.CreateOrGetDraftAsync(91)).Id);
    }

    [Fact]
    public async Task C2_MaterialReplacementSupersedesExactReviewAndPreservesHistory()
    {
        await using var db = NewC2Database();
        var submitted = await SeedC2PendingAsync(db);
        var notifications = new NullPlanNotificationService();
        var drafts = new PlanDraftService(db, new TestClock(),
            NullLogger<PlanDraftService>.Instance, new FakeAudit(), new StubUserContext("po-owner"));
        var successor = await drafts.CreateOrGetDraftAsync(91);
        successor.StagePlans[0].PlannedDue = new DateOnly(2026, 12, 9);
        await db.SaveChangesAsync();
        Assert.True(await drafts.SupersedePendingAfterSaveAsync(successor));
        Assert.Equal(PlanVersionStatus.Superseded, submitted.Status);
        Assert.Equal(new DateOnly(2026, 11, 4), submitted.StagePlans[0].PlannedDue);
        Assert.Contains(await db.PlanApprovalLogs.ToListAsync(),
            log => log.PlanVersionId == submitted.Id && log.Action == "Superseded");
        Assert.Contains(await db.PlanApprovalLogs.ToListAsync(),
            log => log.PlanVersionId == successor.Id && log.Action == "CreatedAsReplacement");

        var approval = new PlanApprovalService(db, new TestClock(),
            NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db),
            notifications, new C2AlwaysValidTimeline());
        Assert.False(await approval.ApproveVersionAsync(
            submitted.Id, "hod-user", false, true, expectedProjectId: 91));
        Assert.False(await approval.RejectVersionAsync(
            submitted.Id, "hod-user", false, true, "Old tab", expectedProjectId: 91));
        Assert.False(await db.PlanVersions.AsNoTracking().AnyAsync(
            p => p.ProjectId == 91 && p.Status == PlanVersionStatus.Approved));
        await approval.SubmitAsync(91, "po-owner");
        Assert.Equal(PlanVersionStatus.PendingApproval, successor.Status);
        Assert.Equal(successor.Id, (await PlanRevisionConsistency.ActionablePending(db)
            .SingleAsync(p => p.ProjectId == 91)).Id);

        // Rejection of a C2 submission must not rewrite its reviewed payload.
        var reviewedEpoch = successor.SubmittedOn;
        Assert.True(await approval.RejectVersionAsync(successor.Id,
            "hod-user", false, true, "Revise timeline", expectedProjectId: 91));
        Assert.Equal(PlanVersionStatus.Rejected, successor.Status);
        Assert.Equal(reviewedEpoch, successor.SubmittedOn);
        Assert.Equal(new DateOnly(2026, 12, 9), successor.StagePlans[0].PlannedDue);
        var correction = await db.PlanVersions.Include(p => p.StagePlans)
            .SingleAsync(p => p.ProjectId == 91 && p.Status == PlanVersionStatus.Draft);
        Assert.Equal(3, correction.VersionNo);
        Assert.Equal(new DateOnly(2026, 12, 9), correction.StagePlans[0].PlannedDue);
        Assert.Equal("po-owner", correction.OwnerUserId);
        Assert.Equal(1, await db.PlanApprovalLogs.CountAsync(log =>
            log.PlanVersionId == successor.Id && log.Action == "Rejected"));
        Assert.Empty(await PlanRevisionConsistency.ActionablePending(db)
            .Where(p => p.ProjectId == 91).ToListAsync());
    }

    [Fact]
    public async Task C2_WithdrawalRequiresExactOwnerAndSubmissionEpoch()
    {
        await using var db = NewC2Database();
        var submitted = await SeedC2PendingAsync(db);
        var approval = new PlanApprovalService(db, new TestClock(),
            NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db),
            new NullPlanNotificationService(), new C2AlwaysValidTimeline());
        Assert.False(await approval.WithdrawVersionAsync(91, submitted.Id,
            submitted.SubmittedOn, "another-po"));
        Assert.False(await approval.WithdrawVersionAsync(91, submitted.Id,
            submitted.SubmittedOn!.Value.AddSeconds(-1), "po-owner"));
        Assert.True(await approval.WithdrawVersionAsync(91, submitted.Id,
            submitted.SubmittedOn, "po-owner"));
        Assert.Equal(PlanVersionStatus.Withdrawn, submitted.Status);
        var correction = await db.PlanVersions.Include(p => p.StagePlans)
            .SingleAsync(p => p.ProjectId == 91 && p.Status == PlanVersionStatus.Draft);
        Assert.Equal(2, correction.VersionNo);
        Assert.Equal("po-owner", correction.OwnerUserId);
        Assert.Equal(submitted.AnchorDate, correction.AnchorDate);
        Assert.Equal(submitted.PncApplicable, correction.PncApplicable);
        Assert.Equal(new DateOnly(2026, 11, 4), correction.StagePlans.Single().PlannedDue);
        Assert.Equal(new DateOnly(2026, 11, 4), submitted.StagePlans[0].PlannedDue);
        Assert.Equal(1, await db.PlanApprovalLogs.CountAsync(l =>
            l.PlanVersionId == submitted.Id && l.Action == "Withdrawn"));
        Assert.False(await approval.WithdrawVersionAsync(91, submitted.Id,
            submitted.SubmittedOn, "po-owner"));
        Assert.Empty(await PlanRevisionConsistency.ActionablePending(db)
            .Where(p => p.ProjectId == 91).ToListAsync());
    }

    private static ApplicationDbContext NewC2Database() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<PlanVersion> SeedC2PendingAsync(ApplicationDbContext db)
    {
        db.Projects.Add(new Project
        {
            Id = 91, Name = "C2 revisions", LeadPoUserId = "po-owner",
            WorkflowVersion = PlanConstants.StageTemplateVersionV1
        });
        var submitted = new PlanVersion
        {
            ProjectId = 91, VersionNo = 1, Status = PlanVersionStatus.PendingApproval,
            CreatedByUserId = "po-owner", OwnerUserId = "po-owner",
            SubmittedByUserId = "po-owner",
            SubmittedOn = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero),
            AnchorDate = new DateOnly(2026, 11, 1),
            SkipHolidays = false, PncApplicable = false,
            PlanningInputVersion = PlanningInputVersions.Current
        };
        submitted.StagePlans.Add(new StagePlan
        {
            StageCode = StageCodes.FS, SortOrder = 10, DurationDays = 4,
            PlannedStart = new DateOnly(2026, 11, 1),
            PlannedDue = new DateOnly(2026, 11, 4)
        });
        db.PlanVersions.Add(submitted);
        await db.SaveChangesAsync();
        return submitted;
    }

    private sealed class C2AlwaysValidTimeline : IPlannedTimelineValidationService
    {
        public Task<PlannedTimelineValidationResult> ValidateAsync(
            PlannedTimelineCandidate candidate, CancellationToken cancellationToken = default)
            => Task.FromResult(new PlannedTimelineValidationResult(Array.Empty<string>()));
    }

    [Fact]
    public async Task SubmitForApproval_RejectsOfficeHolidayChronologyAndLeavesDraftUntouched()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Projects.Add(new Project
        {
            Id = 41,
            Name = "Holiday validation",
            LeadPoUserId = "po-user",
            WorkflowVersion = PlanConstants.StageTemplateVersionV1
        });
        db.StageTemplates.AddRange(
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.IPA, Name = "In-Principle Approval", Sequence = 20 },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.SOW, Name = "SOW Vetting", Sequence = 30 });
        db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            FromStageCode = StageCodes.SOW,
            DependsOnStageCode = StageCodes.IPA
        });
        db.Holidays.Add(new Holiday
        {
            Date = new DateOnly(2026, 10, 26),
            Name = "Observed closure",
            Type = HolidayType.Gazetted,
            IsObservedAsOfficeHoliday = true
        });
        var draft = new PlanVersion
        {
            ProjectId = 41,
            VersionNo = 1,
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = "po-user",
            OwnerUserId = "po-user",
            AnchorStageCode = StageCodes.IPA,
            TransitionRule = PlanTransitionRule.NextWorkingDay,
            SkipWeekends = true,
            SkipHolidays = true,
            PlanningInputVersion = PlanningInputVersions.Current
        };
        draft.StagePlans.AddRange(new[]
        {
            new StagePlan { StageCode = StageCodes.IPA, PlannedStart = new DateOnly(2026, 10, 20), PlannedDue = new DateOnly(2026, 10, 23) },
            new StagePlan { StageCode = StageCodes.SOW, PlannedStart = new DateOnly(2026, 10, 26), PlannedDue = new DateOnly(2026, 10, 30) }
        });
        db.PlanVersions.Add(draft);
        await db.SaveChangesAsync();
        var notifications = new RecordingPlanNotifications();
        var approval = new PlanApprovalService(db, new TestClock(), NullLogger<PlanApprovalService>.Instance,
            new PlanSnapshotService(db), notifications);

        var error = await Assert.ThrowsAsync<PlanApprovalValidationException>(() =>
            approval.SubmitForApprovalAsync(41, "po-user"));

        Assert.Contains(error.Errors, message => message.Contains("27 Oct 2026"));
        Assert.Equal(PlanVersionStatus.Draft, draft.Status);
        Assert.Null(draft.SubmittedByUserId);
        Assert.Null(draft.SubmittedOn);
        Assert.Equal(0, notifications.Submissions);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task Approve_UsesPncApplicabilityDespiteStaleRows(bool applicable, bool existingPnc, bool hasPncRow)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Projects.Add(new Project { Id = 1, Name = "Excluded PNC", LeadPoUserId = "po-user", PncApplicable = !applicable });
        if (existingPnc)
        {
            db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = 1, StageCode = StageCodes.PNC, Status = StageStatus.Completed,
                PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 2),
                ActualStart = new DateOnly(2025, 1, 1), CompletedOn = new DateOnly(2025, 1, 2)
            });
        }
        db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = StageCodes.TOT, Status = StageStatus.Skipped });
        var plan = new PlanVersion
        {
            ProjectId = 1, VersionNo = 1, CreatedByUserId = "po-user", OwnerUserId = "po-user",
            SubmittedByUserId = "po-user", Status = PlanVersionStatus.PendingApproval, PncApplicable = applicable
        };
        if (hasPncRow)
        {
            plan.StagePlans.Add(new StagePlan
            {
                StageCode = StageCodes.PNC, PlannedStart = new DateOnly(2026, 10, 19),
                PlannedDue = new DateOnly(2026, 10, 20), DurationDays = 2
            });
        }
        plan.StagePlans.Add(new StagePlan { StageCode = StageCodes.TOT, PlannedStart = new DateOnly(2026, 10, 21), PlannedDue = new DateOnly(2026, 10, 22) });
        db.PlanVersions.Add(plan);
        await db.SaveChangesAsync();
        var approval = new PlanApprovalService(db, new TestClock(), NullLogger<PlanApprovalService>.Instance,
            new PlanSnapshotService(db), new NullPlanNotificationService());
        var workflowPolicy = new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider());
        Assert.Equal(!applicable, (await workflowPolicy.GetAsync(1)).PncApplicable);
        Assert.True(await approval.ApproveVersionAsync(plan.Id, "hod-reviewer", false, true));
        Assert.Equal(applicable, (await workflowPolicy.GetAsync(1)).PncApplicable);
        db.ChangeTracker.Clear();
        Assert.Equal(PlanningInputVersions.Current, (await db.Projects.SingleAsync()).PublishedPlanningInputVersion);
        if (!applicable && hasPncRow)
            Assert.Null((await db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.PNC)).DurationDays);
        var pnc = await db.ProjectStages.SingleOrDefaultAsync(s => s.StageCode == StageCodes.PNC);
        if (!existingPnc) Assert.Null(pnc);
        else
        {
            Assert.NotNull(pnc);
            Assert.Equal(applicable ? new DateOnly(2026, 10, 19) : (DateOnly?)null, pnc!.PlannedStart);
            Assert.Equal(applicable ? new DateOnly(2026, 10, 20) : (DateOnly?)null, pnc.PlannedDue);
            Assert.Equal(StageStatus.Completed, pnc.Status);
            Assert.Equal(new DateOnly(2025, 1, 1), pnc.ActualStart);
            Assert.Equal(new DateOnly(2025, 1, 2), pnc.CompletedOn);
            var snapshot = await db.ProjectPlanSnapshotRows.SingleAsync(s => s.StageCode == StageCodes.PNC);
            Assert.Equal(pnc.PlannedStart, snapshot.PlannedStart);
            Assert.Equal(pnc.PlannedDue, snapshot.PlannedDue);
        }
        var tot = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.TOT);
        Assert.Equal(StageStatus.Skipped, tot.Status);
        Assert.Equal(new DateOnly(2026, 10, 21), tot.PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 22), tot.PlannedDue);
        Assert.Equal(PlanVersionStatus.Approved, (await db.PlanVersions.SingleAsync()).Status);
    }

    [Fact]
    public async Task CreateOrGetDraft_IsScopedToCurrentUser()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            Code = StageCodes.FS,
            Name = "Feasibility Study",
            Sequence = 10
        });

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Test Project",
            LeadPoUserId = "po-user",
            WorkflowVersion = PlanConstants.StageTemplateVersionV1
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
            Version = PlanConstants.StageTemplateVersionV1,
            Code = StageCodes.FS,
            Name = "Feasibility Study",
            Sequence = 10
        });

        db.Projects.Add(new Project
        {
            Id = 2,
            Name = "Privacy",
            LeadPoUserId = "user-a",
            WorkflowVersion = PlanConstants.StageTemplateVersionV1
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
    public async Task CreateOrGetDraftAsync_ClaimsOwnOrphanDraft()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.StageTemplates.Add(new StageTemplate
        {
            Version = PlanConstants.StageTemplateVersionV1,
            Code = StageCodes.FS,
            Name = "Feasibility Study",
            Sequence = 10
        });

        db.Projects.Add(new Project
        {
            Id = 3,
            Name = "Orphan",
            LeadPoUserId = "owner",
            WorkflowVersion = PlanConstants.StageTemplateVersionV1
        });

        var orphan = new PlanVersion
        {
            ProjectId = 3,
            VersionNo = 1,
            Title = "Orphan",
            Status = PlanVersionStatus.Draft,
            CreatedByUserId = "new-owner",
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
            LeadPoUserId = "po-owner",
            WorkflowVersion = PlanConstants.StageTemplateVersionV1
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

        await Assert.ThrowsAsync<ForbiddenException>(() => approval.ApproveLatestDraftAsync(9, "hod-user", isAdmin: false, isHoD: true));
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
    public async Task RejectedRealignmentDraft_CannotBeDeletedToReactivateSupersededPendingVersion()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Projects.Add(new Project
        {
            Id = 41, Name = "Supersession", LeadPoUserId = "realignment-actor"
        });
        var older = new PlanVersion
        {
            ProjectId = 41, VersionNo = 2, Status = PlanVersionStatus.PendingApproval,
            CreatedByUserId = "older-po", OwnerUserId = "older-po",
            SubmittedByUserId = "older-po",
            SubmittedOn = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero)
        };
        var newer = new PlanVersion
        {
            ProjectId = 41, VersionNo = 3, Status = PlanVersionStatus.PendingApproval,
            CreatedByUserId = "realignment-actor", OwnerUserId = null,
            SubmittedByUserId = "realignment-actor",
            SubmittedOn = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero)
        };
        db.PlanVersions.AddRange(older, newer);
        await db.SaveChangesAsync();

        var decisions = new PlanApprovalService(db, new TestClock(),
            NullLogger<PlanApprovalService>.Instance, new PlanSnapshotService(db),
            new NullPlanNotificationService());
        Assert.True(await decisions.RejectVersionAsync(
            newer.Id, "hod-reviewer", false, true, "Revise newer", expectedProjectId: 41));
        db.ChangeTracker.Clear();

        // The creator can claim an ownerless rejected realignment draft, but
        // must not discard the durable revision and its approval history.
        var audit = new FakeAudit();
        var drafts = new PlanDraftService(db, new TestClock(),
            NullLogger<PlanDraftService>.Instance, audit,
            new StubUserContext("realignment-actor"));
        var claimed = await drafts.CreateOrGetDraftAsync(41);
        Assert.Equal(newer.Id, claimed.Id);
        Assert.Equal("realignment-actor", claimed.OwnerUserId);
        Assert.Equal(PlanDraftDeleteResult.Conflict, await drafts.DeleteDraftAsync(41));

        db.ChangeTracker.Clear();
        Assert.Equal(PlanVersionStatus.Draft,
            (await db.PlanVersions.SingleAsync(p => p.Id == newer.Id)).Status);
        Assert.Equal(PlanVersionStatus.PendingApproval,
            (await db.PlanVersions.SingleAsync(p => p.Id == older.Id)).Status);
        Assert.Single(await db.PlanApprovalLogs.ToListAsync());
        Assert.False(await decisions.ApproveVersionAsync(
            older.Id, "hod-reviewer", false, true, expectedProjectId: 41));
        Assert.False(await decisions.RejectVersionAsync(
            older.Id, "hod-reviewer", false, true, "Old stale form", expectedProjectId: 41));
        Assert.Empty(await db.ProjectPlanSnapshots.ToListAsync());
        Assert.Single(await db.PlanApprovalLogs.ToListAsync());
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task DeleteDraftAsync_PreservesSubmittedHistoryEvenIfRejectionTimestampIsMissing()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var draft = new PlanVersion
        {
            ProjectId = 42, VersionNo = 3, Status = PlanVersionStatus.Draft,
            CreatedByUserId = "owner", OwnerUserId = "owner"
        };
        draft.ApprovalLogs.Add(new PlanApprovalLog
        {
            Action = "Rejected", PerformedByUserId = "hod-reviewer",
            PerformedOn = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero)
        });
        db.PlanVersions.Add(draft);
        await db.SaveChangesAsync();
        var drafts = new PlanDraftService(db, new TestClock(),
            NullLogger<PlanDraftService>.Instance, new FakeAudit(), new StubUserContext("owner"));

        Assert.Null(draft.RejectedOn);
        Assert.Null(draft.SubmittedOn);
        Assert.Equal(PlanDraftDeleteResult.Conflict, await drafts.DeleteDraftAsync(42));
        Assert.True(await db.PlanVersions.AnyAsync(p => p.Id == draft.Id));
        Assert.Single(await db.PlanApprovalLogs.ToListAsync());
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

    private sealed class RecordingPlanNotifications : IPlanNotificationService
    {
        public int Submissions { get; private set; }

        public Task NotifyPlanApprovedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyPlanRejectedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyPlanSubmittedAsync(PlanVersion plan, Project project, string actorUserId, CancellationToken cancellationToken = default)
        {
            Submissions++;
            return Task.CompletedTask;
        }
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
