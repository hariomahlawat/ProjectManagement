using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public class PlanGenerationServiceTests
{
    [Theory]
    [InlineData(null, NextStageStartPolicies.NextWorkingDay, false, false)]
    [InlineData(0, NextStageStartPolicies.NextWorkingDay, false, false)]
    [InlineData(-1, NextStageStartPolicies.NextWorkingDay, false, false)]
    [InlineData(null, NextStageStartPolicies.NextWorkingDay, true, false)]
    [InlineData(null, NextStageStartPolicies.NextWorkingDay, false, true)]
    [InlineData(null, NextStageStartPolicies.SameDay, false, false)]
    [InlineData(0, NextStageStartPolicies.SameDay, false, false)]
    public async Task Generate_LiveAndDraftAnchorFirstPositiveStageIdentically(
        int? leadingDuration, string policy, bool twoClearedStages, bool unmanagedLeading)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Projects.Add(new Project { Id = 1, Name = "Leading cleared stage", WorkflowVersion = PlanConstants.StageTemplateVersionV1 });
        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1, AnchorStart = new DateOnly(2026, 10, 19), IncludeWeekends = false,
            SkipHolidays = true, NextStageStartPolicy = policy
        });
        db.Holidays.Add(new Holiday { Date = new DateOnly(2026, 10, 20), Name = "Office holiday", Type = HolidayType.Gazetted });
        db.StageTemplates.AddRange(
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.FS, Name = "FS", Sequence = 10 },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.IPA, Name = "IPA", Sequence = 20 },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.SOW, Name = "SOW", Sequence = 30 },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.AON, Name = "AON", Sequence = 40 });
        db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.FS, SortOrder = 7, Status = StageStatus.Completed,
                ActualStart = new DateOnly(2025, 1, 1), CompletedOn = new DateOnly(2025, 1, 2),
                PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 2) },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.IPA },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.SOW },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.AON });
        if (!unmanagedLeading)
            db.ProjectPlanDurations.Add(new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.FS, DurationDays = leadingDuration, SortOrder = 10 });
        db.ProjectPlanDurations.AddRange(
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.IPA, DurationDays = twoClearedStages ? null : 2, SortOrder = 20 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.SOW, DurationDays = 2, SortOrder = 30 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.AON, DurationDays = 1, SortOrder = 40 });
        var plan = new PlanVersion { ProjectId = 1, VersionNo = 1, CreatedByUserId = "owner" };
        plan.AnchorDate = new DateOnly(2026, 10, 19);
        plan.SkipWeekends = true;
        plan.SkipHolidays = true;
        plan.TransitionRule = policy == NextStageStartPolicies.SameDay ? PlanTransitionRule.SameDay : PlanTransitionRule.NextWorkingDay;
        plan.StagePlans.AddRange(new[] {
            new StagePlan { StageCode = StageCodes.FS, DurationDays = 0, PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 2) },
            new StagePlan { StageCode = StageCodes.IPA, DurationDays = twoClearedStages ? 0 : 2, PlannedStart = new DateOnly(2024, 1, 3), PlannedDue = new DateOnly(2024, 1, 4) },
            new StagePlan { StageCode = StageCodes.SOW, DurationDays = 2 },
            new StagePlan { StageCode = StageCodes.AON, DurationDays = 1 } });
        db.PlanVersions.Add(plan);
        await db.SaveChangesAsync();
        var generation = new PlanGenerationService(db);
        await generation.GenerateAsync(1);
        await generation.GenerateDraftAsync(1, plan.Id);
        db.ChangeTracker.Clear();
        var live = await db.ProjectStages.ToDictionaryAsync(s => s.StageCode);
        var draft = await db.StagePlans.ToDictionaryAsync(s => s.StageCode);
        var first = twoClearedStages ? StageCodes.SOW : StageCodes.IPA;
        Assert.Equal(new DateOnly(2026, 10, 19), live[first].PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 19), draft[first].PlannedStart);
        Assert.Equal(new DateOnly(2026, 10, 21), live[first].PlannedDue); // 20 Oct is a holiday.
        foreach (var code in twoClearedStages ? new[] { StageCodes.SOW, StageCodes.AON } : new[] { StageCodes.IPA, StageCodes.SOW, StageCodes.AON })
        {
            Assert.Equal(live[code].PlannedStart, draft[code].PlannedStart);
            Assert.Equal(live[code].PlannedDue, draft[code].PlannedDue);
        }
        var next = twoClearedStages ? StageCodes.AON : StageCodes.SOW;
        Assert.Equal(policy == NextStageStartPolicies.SameDay ? new DateOnly(2026, 10, 21) : new DateOnly(2026, 10, 22), live[next].PlannedStart);
        if (!twoClearedStages)
        {
            Assert.Equal(policy == NextStageStartPolicies.SameDay ? new DateOnly(2026, 10, 22) : new DateOnly(2026, 10, 23), live[StageCodes.SOW].PlannedDue);
            Assert.Equal(policy == NextStageStartPolicies.SameDay ? new DateOnly(2026, 10, 22) : new DateOnly(2026, 10, 26), live[StageCodes.AON].PlannedStart);
        }
        Assert.Equal(twoClearedStages ? 0 : 20, live[StageCodes.IPA].SortOrder);
        Assert.Equal(30, live[StageCodes.SOW].SortOrder);
        Assert.Equal(40, live[StageCodes.AON].SortOrder);
        Assert.Equal(unmanagedLeading ? new DateOnly(2024, 1, 1) : (DateOnly?)null, live[StageCodes.FS].PlannedStart);
        Assert.Equal(unmanagedLeading ? new DateOnly(2024, 1, 2) : (DateOnly?)null, live[StageCodes.FS].PlannedDue);
        Assert.Null(draft[StageCodes.FS].PlannedStart);
        Assert.Null(draft[StageCodes.FS].PlannedDue);
        Assert.Equal(0, draft[StageCodes.FS].DurationDays);
        Assert.Equal(7, live[StageCodes.FS].SortOrder);
        Assert.Equal(StageStatus.Completed, live[StageCodes.FS].Status);
        Assert.Equal(new DateOnly(2025, 1, 1), live[StageCodes.FS].ActualStart);
        Assert.Equal(new DateOnly(2025, 1, 2), live[StageCodes.FS].CompletedOn);
        if (twoClearedStages)
        {
            Assert.Null(live[StageCodes.IPA].PlannedStart);
            Assert.Null(live[StageCodes.IPA].PlannedDue);
            Assert.Null(draft[StageCodes.IPA].PlannedStart);
            Assert.Null(draft[StageCodes.IPA].PlannedDue);
        }
    }

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, 0, false)]
    [InlineData(false, -1, false)]
    [InlineData(false, null, true)]
    [InlineData(true, null, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, -1, false)]
    public async Task Regenerate_PositiveThenRemovedDuration_ClearsScheduling(bool live, int? clearedDuration, bool removeRow)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Projects.Add(new Project { Id = 1, Name = "Duration removal", WorkflowVersion = PlanConstants.StageTemplateVersionV1 });
        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1, AnchorStart = new DateOnly(2026, 10, 19), IncludeWeekends = true,
            SkipHolidays = false, NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay
        });
        db.StageTemplates.AddRange(
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.FS, Name = "FS", Sequence = 10 },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.PNC, Name = "PNC", Sequence = 80, Optional = true },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.EAS, Name = "EAS", Sequence = 90 });
        db.ProjectPlanDurations.AddRange(
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.FS, DurationDays = 3, SortOrder = 10 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.PNC, DurationDays = 2, SortOrder = 80 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.EAS, DurationDays = 4, SortOrder = 90 });
        db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.FS },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.PNC, Status = StageStatus.Completed,
                ActualStart = new DateOnly(2025, 1, 1), CompletedOn = new DateOnly(2025, 1, 2) },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.EAS },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.TOT, Status = StageStatus.Skipped,
                PlannedStart = new DateOnly(2024, 1, 1), PlannedDue = new DateOnly(2024, 1, 2) });
        var plan = new PlanVersion { ProjectId = 1, VersionNo = 1, CreatedByUserId = "owner" };
        plan.AnchorDate = new DateOnly(2026, 10, 19);
        plan.SkipWeekends = false;
        plan.SkipHolidays = false;
        plan.StagePlans.AddRange(new[] {
            new StagePlan { StageCode = StageCodes.FS, DurationDays = 3 },
            new StagePlan { StageCode = StageCodes.PNC, DurationDays = 2 },
            new StagePlan { StageCode = StageCodes.EAS, DurationDays = 4 } });
        db.PlanVersions.Add(plan);
        await db.SaveChangesAsync();
        var generation = new PlanGenerationService(db);
        if (live) await generation.GenerateAsync(1);
        else await generation.GenerateDraftAsync(1, plan.Id);
        db.ChangeTracker.Clear();
        if (live) Assert.NotNull((await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC)).PlannedDue);
        else Assert.Equal(2, (await db.StagePlans.SingleAsync(s => s.StageCode == StageCodes.PNC)).DurationDays);

        var duration = await db.ProjectPlanDurations.SingleAsync(d => d.StageCode == StageCodes.PNC);
        if (removeRow) db.ProjectPlanDurations.Remove(duration);
        else duration.DurationDays = clearedDuration;
        if (!live)
            (await db.StagePlans.SingleAsync(s => s.PlanVersionId == plan.Id && s.StageCode == StageCodes.PNC)).DurationDays = 0;
        await db.SaveChangesAsync();
        if (live) await generation.GenerateAsync(1);
        else await generation.GenerateDraftAsync(1, plan.Id);
        db.ChangeTracker.Clear();

        if (live)
        {
            var pnc = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC);
            Assert.Null(pnc.PlannedStart);
            Assert.Null(pnc.PlannedDue);
            // Planned-date removal never removes lifecycle history or actual facts.
            Assert.Equal(StageStatus.Completed, pnc.Status);
            Assert.Equal(new DateOnly(2025, 1, 1), pnc.ActualStart);
            Assert.Equal(new DateOnly(2025, 1, 2), pnc.CompletedOn);
            Assert.Equal(new DateOnly(2026, 10, 22), (await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.EAS)).PlannedStart);
        }
        else
        {
            var pnc = await db.StagePlans.SingleAsync(s => s.StageCode == StageCodes.PNC);
            Assert.Null(pnc.PlannedStart);
            Assert.Null(pnc.PlannedDue);
            Assert.Equal(0, pnc.DurationDays);
            Assert.Equal(new DateOnly(2026, 10, 22), (await db.StagePlans.SingleAsync(s => s.StageCode == StageCodes.EAS)).PlannedStart);
        }
        var untouched = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.TOT);
        Assert.Equal(StageStatus.Skipped, untouched.Status);
        Assert.Equal(new DateOnly(2024, 1, 1), untouched.PlannedStart);
        Assert.Equal(new DateOnly(2024, 1, 2), untouched.PlannedDue);
    }

    [Fact]
    public async Task GenerateDraft_SkipsPnc_WhenDurationMissing()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Test Project",
            CreatedByUserId = "seed",
            WorkflowVersion = PlanConstants.StageTemplateVersionV1
        });

        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            AnchorStart = new DateOnly(2024, 1, 2),
            IncludeWeekends = false,
            SkipHolidays = true,
            NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay
        });

        db.StageTemplates.AddRange(
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.COB, Name = "Commercial Opening", Sequence = 70 },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.PNC, Name = "Price Negotiation", Sequence = 80, Optional = true },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.EAS, Name = "Expenditure Sanction", Sequence = 90 }
        );

        db.ProjectPlanDurations.AddRange(
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.COB, DurationDays = 5, SortOrder = 1 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.PNC, DurationDays = null, SortOrder = 2 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.EAS, DurationDays = 3, SortOrder = 3 }
        );

        var plan = new PlanVersion
        {
            ProjectId = 1,
            VersionNo = 1,
            CreatedByUserId = "seed",
            CreatedOn = DateTimeOffset.UtcNow,
            Title = PlanVersion.ProjectTimelineTitle,
            Status = PlanVersionStatus.Draft,
            AnchorStageCode = StageCodes.COB,
            AnchorDate = new DateOnly(2024, 1, 2),
            SkipWeekends = false,
            TransitionRule = PlanTransitionRule.NextWorkingDay,
            PncApplicable = true
        };

        plan.StagePlans.Add(new StagePlan { StageCode = StageCodes.COB, DurationDays = 5 });
        plan.StagePlans.Add(new StagePlan { StageCode = StageCodes.PNC });
        plan.StagePlans.Add(new StagePlan { StageCode = StageCodes.EAS, DurationDays = 3 });

        db.PlanVersions.Add(plan);

        await db.SaveChangesAsync();

        var service = new PlanGenerationService(db);
        await service.GenerateDraftAsync(1, plan.Id);

        var pnc = await db.StagePlans.SingleAsync(sp => sp.PlanVersionId == plan.Id && sp.StageCode == StageCodes.PNC);
        var eas = await db.StagePlans.SingleAsync(sp => sp.PlanVersionId == plan.Id && sp.StageCode == StageCodes.EAS);

        Assert.Null(pnc.PlannedStart);
        Assert.Null(pnc.PlannedDue);
        Assert.NotNull(eas.PlannedStart);
        Assert.NotNull(eas.PlannedDue);
    }

    [Fact]
    public async Task GenerateDraft_PreservesProposalMetadataDespiteContradictoryScheduleSettings()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Test Project",
            CreatedByUserId = "seed",
            WorkflowVersion = PlanConstants.StageTemplateVersionV1
        });

        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            AnchorStart = new DateOnly(2024, 5, 1),
            IncludeWeekends = true,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.SameDay
        });

        db.StageTemplates.AddRange(
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.BID, Name = "Bid Upload", Sequence = 50 },
            new StageTemplate { Version = PlanConstants.StageTemplateVersionV1, Code = StageCodes.TEC, Name = "Technical Evaluation Committee", Sequence = 60 }
        );

        db.ProjectPlanDurations.AddRange(
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.BID, DurationDays = 3, SortOrder = 1 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.TEC, DurationDays = 2, SortOrder = 2 }
        );

        var plan = new PlanVersion
        {
            ProjectId = 1,
            VersionNo = 1,
            CreatedByUserId = "seed",
            CreatedOn = DateTimeOffset.UtcNow,
            Title = PlanVersion.ProjectTimelineTitle,
            Status = PlanVersionStatus.Draft,
            AnchorStageCode = StageCodes.BID,
            AnchorDate = new DateOnly(2024, 5, 6),
            SkipWeekends = false,
            SkipHolidays = false,
            TransitionRule = PlanTransitionRule.SameDay,
            PncApplicable = true
        };

        plan.StagePlans.Add(new StagePlan { StageCode = StageCodes.BID, DurationDays = 3 });
        plan.StagePlans.Add(new StagePlan { StageCode = StageCodes.TEC, DurationDays = 2 });

        db.PlanVersions.Add(plan);

        await db.SaveChangesAsync();

        var service = new PlanGenerationService(db);
        await service.GenerateDraftAsync(1, plan.Id);

        await db.Entry(plan).ReloadAsync();

        Assert.False(plan.SkipWeekends);
        Assert.Equal(PlanTransitionRule.SameDay, plan.TransitionRule);
        Assert.Equal(new DateOnly(2024, 5, 6), plan.AnchorDate);
        Assert.False(plan.SkipHolidays);

        var bid = await db.StagePlans.SingleAsync(sp => sp.PlanVersionId == plan.Id && sp.StageCode == StageCodes.BID);
        var tec = await db.StagePlans.SingleAsync(sp => sp.PlanVersionId == plan.Id && sp.StageCode == StageCodes.TEC);

        Assert.NotNull(bid.PlannedStart);
        Assert.NotNull(bid.PlannedDue);
        Assert.NotNull(tec.PlannedStart);
        Assert.NotNull(tec.PlannedDue);
        Assert.Equal(bid.PlannedDue, tec.PlannedStart);
    }
    [Fact]
    public async Task GenerateAsync_UsesWorkflowV2Order_WhenLegacyDurationOrderDiffers()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(options);
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "V2 Project",
            CreatedByUserId = "seed",
            WorkflowVersion = ProcurementWorkflow.VersionV2
        });
        db.ProjectScheduleSettings.Add(new ProjectScheduleSettings
        {
            ProjectId = 1,
            AnchorStart = new DateOnly(2025, 1, 1),
            IncludeWeekends = true,
            SkipHolidays = false,
            NextStageStartPolicy = NextStageStartPolicies.NextWorkingDay
        });
        db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.FS, SortOrder = 0, Status = StageStatus.NotStarted },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.IPA, SortOrder = 1, Status = StageStatus.NotStarted },
            new ProjectStage { ProjectId = 1, StageCode = StageCodes.SOW, SortOrder = 2, Status = StageStatus.NotStarted });
        db.ProjectPlanDurations.AddRange(
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.FS, DurationDays = 1, SortOrder = 0 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.IPA, DurationDays = 1, SortOrder = 1 },
            new ProjectPlanDuration { ProjectId = 1, StageCode = StageCodes.SOW, DurationDays = 1, SortOrder = 2 });
        await db.SaveChangesAsync();

        var service = new PlanGenerationService(db);
        await service.GenerateAsync(1);

        var sow = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.SOW);
        var ipa = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);

        Assert.True(sow.SortOrder < ipa.SortOrder);
        Assert.NotNull(sow.PlannedStart);
        Assert.NotNull(ipa.PlannedStart);
        Assert.True(sow.PlannedStart!.Value < ipa.PlannedStart!.Value);
    }

}
