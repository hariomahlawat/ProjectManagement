using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests;

public class PlanGenerationServiceTests
{
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
            CreatedByUserId = "seed"
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
            new StageTemplate { Version = PlanConstants.StageTemplateVersion, Code = StageCodes.COB, Name = "Commercial Opening", Sequence = 70 },
            new StageTemplate { Version = PlanConstants.StageTemplateVersion, Code = StageCodes.PNC, Name = "Price Negotiation", Sequence = 80, Optional = true },
            new StageTemplate { Version = PlanConstants.StageTemplateVersion, Code = StageCodes.EAS, Name = "Expenditure Sanction", Sequence = 90 }
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

        plan.StagePlans.Add(new StagePlan { StageCode = StageCodes.COB });
        plan.StagePlans.Add(new StagePlan { StageCode = StageCodes.PNC });
        plan.StagePlans.Add(new StagePlan { StageCode = StageCodes.EAS });

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
}
