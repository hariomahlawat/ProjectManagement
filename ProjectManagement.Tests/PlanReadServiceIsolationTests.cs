using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

public class PlanReadServiceIsolationTests
{
    [Theory]
    [InlineData(PlanVersionStatus.Draft)]
    [InlineData(PlanVersionStatus.PendingApproval)]
    public async Task ActorProposal_SuppliesAllInputsAndClearedDatesWithoutOtherActorPendingLeak(PlanVersionStatus status)
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var mine = new PlanVersion {
            ProjectId = 1, OwnerUserId = "actor", CreatedByUserId = "actor", VersionNo = 2, Status = status,
            AnchorDate = new DateOnly(2026, 11, 10), SkipWeekends = false, SkipHolidays = false,
            TransitionRule = PlanTransitionRule.SameDay, PncApplicable = false,
            SubmittedOn = status == PlanVersionStatus.PendingApproval ? new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) : null,
            StagePlans = new() {
                new() { StageCode = StageCodes.FS, DurationDays = 12 },
                new() { StageCode = "EXT-B", DurationDays = 4, SortOrder = 90 },
                new() { StageCode = "EXT-A", DurationDays = 0, SortOrder = 80 }
            }
        };
        db.PlanVersions.Add(mine);
        await db.SaveChangesAsync();
        var vm = await new PlanReadService(db).GetAsync(1, "actor");
        Assert.Equal(new DateOnly(2026, 11, 10), vm.Durations.AnchorStart);
        Assert.True(vm.Durations.IncludeWeekends);
        Assert.False(vm.Durations.SkipHolidays);
        Assert.Equal(NextStageStartPolicies.SameDay, vm.Durations.NextStageStartPolicy);
        Assert.False(vm.State.PncApplicable);
        Assert.Equal(12, vm.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.Null(vm.Durations.Rows.Single(r => r.Code == "EXT-A").DurationDays);
        Assert.Null(vm.Exact.Rows.Single(r => r.Code == StageCodes.FS).PlannedStart);
        Assert.Null(vm.Exact.Rows.Single(r => r.Code == StageCodes.FS).PlannedDue);
        Assert.Null(vm.Durations.Rows.Single(r => r.Code == StageCodes.FS).PreviewStart);
        Assert.Equal(new[] { "EXT-A", "EXT-B" }, vm.Durations.Rows.Where(r => r.Code.StartsWith("EXT-")).Select(r => r.Code));
        Assert.True(vm.State.HasPendingSubmission);
        Assert.Equal(status == PlanVersionStatus.PendingApproval, vm.State.IsLocked);
        Assert.Equal(PlanEditorModes.Durations, vm.ActiveMode);
    }

    [Fact]
    public async Task SupersededPending_DoesNotLockCorrectedRejectedDraftInEditor()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var older = await db.PlanVersions.SingleAsync(p => p.VersionNo == 4);
        older.OwnerUserId = "actor";
        older.SubmittedByUserId = "actor";
        var corrected = new PlanVersion
        {
            ProjectId = 1, VersionNo = 6, Status = PlanVersionStatus.Draft,
            CreatedByUserId = "actor", OwnerUserId = "actor",
            RejectedOn = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero),
            PlanningInputVersion = PlanningInputVersions.Current,
            AnchorDate = new DateOnly(2026, 12, 5),
            StagePlans = new() { new() { StageCode = StageCodes.FS, DurationDays = 9 } }
        };
        db.PlanVersions.Add(corrected);
        await db.SaveChangesAsync();

        var vm = await new PlanReadService(db).GetAsync(1, "actor");
        Assert.True(vm.State.HasDraft);
        Assert.True(vm.State.HasMyDraft);
        Assert.False(vm.State.IsLocked);
        Assert.False(vm.State.HasPendingSubmission);
        Assert.True(vm.State.CanSubmit);
        Assert.Null(vm.State.PendingSubmittedOn);
        Assert.Equal(PlanVersionStatus.Draft, vm.State.Status);
        Assert.Equal(6, vm.State.VersionNo);
        Assert.Equal(new DateOnly(2026, 12, 5), vm.Durations.AnchorStart);
        Assert.Equal(9, vm.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);

        var other = await new PlanReadService(db).GetAsync(1, "uninvolved");
        Assert.False(other.State.HasPendingSubmission);
        Assert.True(other.State.CanSubmit);
        Assert.False(other.State.IsLocked);
        Assert.DoesNotContain(other.Durations.Rows, r => r.Code == "OTHER-ONLY");
    }

    [Fact]
    public async Task ActorWithoutProposal_ReadsPublishedBaselineDespiteOtherActorDraftAndPending()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var vm = await new PlanReadService(db).GetAsync(1, "uninvolved");
        Assert.Equal(new DateOnly(2026, 11, 1), vm.Durations.AnchorStart);
        Assert.False(vm.Durations.IncludeWeekends);
        Assert.True(vm.Durations.SkipHolidays);
        Assert.Equal(NextStageStartPolicies.NextWorkingDay, vm.Durations.NextStageStartPolicy);
        Assert.False(vm.State.PncApplicable);
        Assert.Equal(5, vm.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.Equal(new DateOnly(2026, 11, 2), vm.Exact.Rows.Single(r => r.Code == StageCodes.FS).PlannedStart);
        Assert.Equal(new DateOnly(2026, 11, 6), vm.Durations.Rows.Single(r => r.Code == StageCodes.FS).PreviewDue);
        Assert.True(vm.State.HasPendingSubmission);
        Assert.False(vm.State.PendingOwnedByCurrentUser);
        Assert.False(vm.State.IsLocked);
        Assert.False(vm.State.CanSubmit);
        Assert.DoesNotContain(vm.Durations.Rows, r => r.Code == "OTHER-ONLY");
    }

    [Fact]
    public async Task OwnDraftPrecedesOwnPendingAndPublishedInputs()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.PlanVersions.AddRange(
            new PlanVersion { ProjectId = 1, OwnerUserId = "actor", CreatedByUserId = "actor", VersionNo = 2,
                Status = PlanVersionStatus.PendingApproval, AnchorDate = new DateOnly(2027, 1, 1), PncApplicable = true,
                StagePlans = new() { new() { StageCode = StageCodes.FS, DurationDays = 20 } } },
            new PlanVersion { ProjectId = 1, OwnerUserId = "actor", CreatedByUserId = "actor", VersionNo = 3,
                Status = PlanVersionStatus.Draft, AnchorDate = new DateOnly(2027, 2, 1), SkipHolidays = false, PncApplicable = false,
                StagePlans = new() { new() { StageCode = StageCodes.FS, DurationDays = 30 } } });
        await db.SaveChangesAsync();
        var vm = await new PlanReadService(db).GetAsync(1, "actor");
        Assert.Equal(new DateOnly(2027, 2, 1), vm.Durations.AnchorStart);
        Assert.Equal(30, vm.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.False(vm.Durations.SkipHolidays);
        Assert.False(vm.State.PncApplicable);
        Assert.True(vm.State.HasMyDraft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacySharedWorkingInputs_AreNeverAnUninvolvedActorsPublishedBaseline(bool discarded)
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        (await db.Projects.SingleAsync()).PublishedPlanningInputVersion = 0;
        if (discarded) db.PlanVersions.RemoveRange(await db.PlanVersions.ToListAsync());
        var settings = await db.ProjectScheduleSettings.SingleAsync();
        settings.AnchorStart = new DateOnly(2030, 1, 1);
        settings.IncludeWeekends = true;
        settings.SkipHolidays = false;
        settings.NextStageStartPolicy = NextStageStartPolicies.SameDay;
        (await db.ProjectPlanDurations.SingleAsync()).DurationDays = 99;
        db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = "PRIVATE-ONLY", DurationDays = 50 });
        await db.SaveChangesAsync();

        var vm = await new PlanReadService(db).GetAsync(1, "uninvolved");
        Assert.Null(vm.Durations.AnchorStart);
        Assert.False(vm.Durations.IncludeWeekends);
        Assert.True(vm.Durations.SkipHolidays);
        Assert.Equal(NextStageStartPolicies.NextWorkingDay, vm.Durations.NextStageStartPolicy);
        Assert.Null(vm.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.Equal(new DateOnly(2026, 11, 2), vm.Exact.Rows.Single(r => r.Code == StageCodes.FS).PlannedStart);
        Assert.DoesNotContain(vm.Durations.Rows, r => r.Code == "PRIVATE-ONLY");
        Assert.Equal(PlanEditorModes.Exact, vm.ActiveMode);
    }

    [Theory]
    [InlineData(PlanVersionStatus.Draft, false)]
    [InlineData(PlanVersionStatus.PendingApproval, true)]
    public async Task LegacyActorProposal_OwnsDatesAndPresentsDraftCandidatesWhilePendingUsesAcceptedPolicy(PlanVersionStatus status, bool proposalPnc)
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.PlanVersions.Add(new PlanVersion { ProjectId = 1, OwnerUserId = "actor", CreatedByUserId = "actor", VersionNo = 20,
            PlanningInputVersion = 0, Status = status, AnchorDate = new DateOnly(2040, 1, 1), SkipWeekends = false,
            SkipHolidays = false, TransitionRule = PlanTransitionRule.SameDay, PncApplicable = proposalPnc,
            StagePlans = new() { new() { StageCode = StageCodes.FS, DurationDays = 81,
                PlannedStart = new DateOnly(2027, 1, 4), PlannedDue = new DateOnly(2027, 1, 5) } } });
        await db.SaveChangesAsync();
        var vm = await new PlanReadService(db).GetAsync(1, "actor");
        Assert.Equal(status == PlanVersionStatus.Draft ? new DateOnly(2040, 1, 1) : new DateOnly(2026, 11, 1), vm.Durations.AnchorStart);
        Assert.Equal(status == PlanVersionStatus.Draft, vm.Durations.IncludeWeekends);
        Assert.True(vm.Durations.SkipHolidays);
        Assert.Equal(status == PlanVersionStatus.Draft ? NextStageStartPolicies.SameDay : NextStageStartPolicies.NextWorkingDay, vm.Durations.NextStageStartPolicy);
        Assert.Equal(status == PlanVersionStatus.Draft ? 81 : 5, vm.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.Equal(new DateOnly(2027, 1, 4), vm.Exact.Rows.Single(r => r.Code == StageCodes.FS).PlannedStart);
        Assert.Equal(proposalPnc, vm.State.PncApplicable);
        Assert.Equal(status == PlanVersionStatus.PendingApproval, vm.State.IsLocked);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task LegacyOwnDraft_PresentsPrivateDurationCandidatesWithoutPromotingPolicyOrLeakingToOtherActors(
        bool trustedBaseline, bool livePnc, bool proposalPnc)
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var project = await db.Projects.SingleAsync();
        project.PublishedPlanningInputVersion = trustedBaseline ? PlanningInputVersions.Current : PlanningInputVersions.Legacy;
        project.PncApplicable = livePnc;
        var mine = new PlanVersion { ProjectId = 1, OwnerUserId = "actor", CreatedByUserId = "actor", VersionNo = 20,
            PlanningInputVersion = PlanningInputVersions.Legacy, Status = PlanVersionStatus.Draft,
            AnchorDate = new DateOnly(2035, 1, 1), SkipWeekends = false, SkipHolidays = false,
            TransitionRule = PlanTransitionRule.SameDay, PncApplicable = proposalPnc,
            StagePlans = new() {
                new() { StageCode = StageCodes.FS, DurationDays = 99, PlannedStart = new DateOnly(2035, 1, 1), PlannedDue = new DateOnly(2035, 4, 9) },
                new() { StageCode = StageCodes.PNC, DurationDays = 15, PlannedStart = new DateOnly(2035, 4, 9), PlannedDue = new DateOnly(2035, 4, 23) },
                new() { StageCode = "PRIVATE", DurationDays = 45, SortOrder = 90,
                    PlannedStart = new DateOnly(2035, 4, 23), PlannedDue = new DateOnly(2035, 6, 6) }
            } };
        db.PlanVersions.Add(mine);
        await db.SaveChangesAsync();

        var vm = await new PlanReadService(db).GetAsync(1, "actor");
        Assert.Equal(new DateOnly(2035, 1, 1), vm.Durations.AnchorStart);
        Assert.True(vm.Durations.IncludeWeekends);
        Assert.True(vm.Durations.SkipHolidays); // Legacy revisions did not own this policy.
        Assert.Equal(NextStageStartPolicies.SameDay, vm.Durations.NextStageStartPolicy);
        Assert.Equal(99, vm.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.Equal(45, vm.Durations.Rows.Single(r => r.Code == "PRIVATE").DurationDays);
        Assert.Equal(new DateOnly(2035, 1, 1), vm.Exact.Rows.Single(r => r.Code == StageCodes.FS).PlannedStart);
        Assert.Equal(livePnc && proposalPnc, vm.State.PncApplicable);
        var pnc = vm.Durations.Rows.Single(r => r.Code == StageCodes.PNC);
        Assert.Equal(livePnc && proposalPnc ? 15 : (int?)null, pnc.DurationDays);
        Assert.Equal(livePnc && proposalPnc ? new DateOnly(2035, 4, 9) : (DateOnly?)null, pnc.PreviewStart);
        Assert.Equal(trustedBaseline ? PlanEditorModes.Durations : PlanEditorModes.Exact, vm.ActiveMode);

        var other = await new PlanReadService(db).GetAsync(1, "uninvolved");
        Assert.Equal(trustedBaseline ? new DateOnly(2026, 11, 1) : (DateOnly?)null, other.Durations.AnchorStart);
        Assert.Equal(trustedBaseline ? 5 : (int?)null, other.Durations.Rows.Single(r => r.Code == StageCodes.FS).DurationDays);
        Assert.DoesNotContain(other.Durations.Rows, r => r.Code == "PRIVATE");
        Assert.All(db.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        db.ChangeTracker.Clear();
        mine = await db.PlanVersions.Include(p => p.StagePlans).SingleAsync(p => p.OwnerUserId == "actor");
        Assert.Equal(PlanningInputVersions.Legacy, mine.PlanningInputVersion);
        Assert.False(mine.SkipHolidays);
        Assert.Equal(proposalPnc, mine.PncApplicable);
        Assert.Equal(15, mine.StagePlans.Single(s => s.StageCode == StageCodes.PNC).DurationDays);
        Assert.NotNull(mine.StagePlans.Single(s => s.StageCode == StageCodes.PNC).PlannedStart);
        Assert.Equal(45, mine.StagePlans.Single(s => s.StageCode == "PRIVATE").DurationDays);
    }

    [Fact]
    public async Task FalsePublishedPnc_ClearsStaleEditorInputsBeforeAnUnrelatedFormPost()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = StageCodes.PNC,
            PlannedStart = new DateOnly(2026, 8, 1), PlannedDue = new DateOnly(2026, 8, 8) });
        db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.PNC, DurationDays = 8 });
        await db.SaveChangesAsync();
        var vm = await new PlanReadService(db).GetAsync(1, "uninvolved");
        var exact = vm.Exact.Rows.Single(r => r.Code == StageCodes.PNC);
        var duration = vm.Durations.Rows.Single(r => r.Code == StageCodes.PNC);
        Assert.False(vm.State.PncApplicable);
        Assert.Null(exact.PlannedStart);
        Assert.Null(exact.PlannedDue);
        Assert.Null(duration.DurationDays);
        Assert.Null(duration.PreviewStart);
        Assert.Null(duration.PreviewDue);
        Assert.NotNull((await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC)).PlannedStart);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        db.Projects.Add(new Project { Id = 1, Name = "Isolated editor", WorkflowVersion = PlanConstants.StageTemplateVersionV1, PncApplicable = false, PublishedPlanningInputVersion = 1 });
        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings { ProjectId = 1, AnchorStart = new DateOnly(2026, 11, 1),
            IncludeWeekends = false, SkipHolidays = true, NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay });
        db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.FS, DurationDays = 5 });
        db.ProjectStages.Add(new ProjectStage { ProjectId = 1, StageCode = StageCodes.FS, Status = StageStatus.NotStarted,
            PlannedStart = new DateOnly(2026, 11, 2), PlannedDue = new DateOnly(2026, 11, 6) });
        db.PlanVersions.Add(new PlanVersion { ProjectId = 1, OwnerUserId = "someone-else", CreatedByUserId = "someone-else", VersionNo = 4,
            Status = PlanVersionStatus.PendingApproval, SubmittedOn = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero),
            AnchorDate = new DateOnly(2028, 1, 1), SkipHolidays = false, SkipWeekends = false, PncApplicable = true,
            StagePlans = new() { new() { StageCode = StageCodes.FS, DurationDays = 99 }, new() { StageCode = "OTHER-ONLY", DurationDays = 2 } } });
        db.PlanVersions.Add(new PlanVersion { ProjectId = 1, OwnerUserId = "third-user", CreatedByUserId = "third-user", VersionNo = 5,
            Status = PlanVersionStatus.Draft, AnchorDate = new DateOnly(2029, 1, 1), PncApplicable = true,
            StagePlans = new() { new() { StageCode = StageCodes.FS, DurationDays = 80 } } });
        await db.SaveChangesAsync();
    }
}
