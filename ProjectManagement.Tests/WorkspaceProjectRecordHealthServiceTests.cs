using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Workspace;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class WorkspaceProjectRecordHealthServiceTests
{
    [Fact]
    public async Task HistoricalTimeline_UsesOnlyActualDates_AndAwardsPartialCredit()
    {
        await using var db = CreateContext();
        var project = NewProject();
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.FS,
            SortOrder = 1,
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2026, 1, 1),
            CompletedOn = null,
            PlannedStart = null,
            PlannedDue = null
        });
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.SOW,
            SortOrder = 2,
            Status = StageStatus.Completed,
            CompletedOn = new DateOnly(2026, 1, 20)
        });
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.IPA,
            SortOrder = 3,
            Status = StageStatus.NotStarted,
            PlannedStart = new DateOnly(2026, 2, 1),
            PlannedDue = new DateOnly(2026, 2, 15)
        });

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.CalculateForProjectsAsync(new[] { project }, "po-1", default);
        var health = result[project.Id];

        // 15 profile + 25 no procurement fields applicable + 10/20 historical
        // + 15 current-stage planning + 0 documents + 0 media = 65.
        Assert.Equal(65, health.HealthPercent);
        Assert.Contains(health.GapDetails, gap => gap.Code == "FS_ACTUAL_COMPLETION" && gap.FieldLabel == "FS — Actual Completion");
        // The briefing desk turns these gaps into its required completion-date step by their code.
        var completionGap = Assert.Single(health.GapDetails, BriefingDeskBuilder.IsCompletionDateGap);
        Assert.Equal("FS", completionGap.StageCode);
        Assert.DoesNotContain(health.GapDetails, gap => gap.FieldLabel.Contains("planned", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Procurement_UsesLatestOverviewValues_AndOnlyCompletedStageFields()
    {
        await using var db = CreateContext();
        var project = NewProject();
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.FS,
            SortOrder = 1,
            Status = StageStatus.Completed,
            CompletedOn = new DateOnly(2025, 12, 20)
        });
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.SOW,
            SortOrder = 2,
            Status = StageStatus.Completed,
            CompletedOn = new DateOnly(2025, 12, 28)
        });
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.IPA,
            SortOrder = 3,
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2026, 1, 1),
            CompletedOn = new DateOnly(2026, 1, 3)
        });
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.AON,
            SortOrder = 4,
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2026, 1, 4),
            CompletedOn = new DateOnly(2026, 1, 5)
        });
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.BM,
            SortOrder = 5,
            Status = StageStatus.NotStarted,
            PlannedStart = new DateOnly(2026, 1, 6),
            PlannedDue = new DateOnly(2026, 1, 20)
        });

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectIpaFacts.Add(new ProjectIpaFact
        {
            ProjectId = project.Id,
            IpaCost = 65m,
            CreatedByUserId = "seed",
            CreatedOnUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        db.ProjectAonFacts.AddRange(
            new ProjectAonFact
            {
                ProjectId = project.Id,
                AonCost = 64.9m,
                CreatedByUserId = "seed",
                CreatedOnUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new ProjectAonFact
            {
                ProjectId = project.Id,
                AonCost = 0m,
                CreatedByUserId = "seed",
                CreatedOnUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.CalculateForProjectsAsync(new[] { project }, "po-1", default);
        var health = result[project.Id];

        Assert.Contains(health.GapDetails, gap => gap.Code == "AON_COST" && gap.FieldLabel == "AoN Cost");
        Assert.DoesNotContain(health.GapDetails, gap => gap.Code == "BENCHMARK_COST"); // BM is not completed yet.
        // BID and TEC have no persisted rows. The canonical current stage is BID,
        // so BM planning dates do not earn the 15 current-stage timeline points.
        Assert.Contains(health.GapDetails, gap => gap.Code == "CURRENT_STAGE_PDC" && gap.FieldLabel == "BID — PDC");
        // The briefing desk recognises current-stage gaps by this code prefix, not the label.
        Assert.All(
            health.GapDetails.Where(gap => gap.Component == "Current-stage timeline (PDC)"),
            gap => Assert.True(BriefingDeskBuilder.IsCurrentStageGap(gap)));
        Assert.Equal(48, health.HealthPercent);
    }

    [Fact]
    public async Task CurrentStageTimeline_PdcAndStartReceiveIndependentCredit()
    {
        await using var db = CreateContext();
        var project = NewProject();
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.FS,
            SortOrder = 1,
            Status = StageStatus.InProgress,
            ActualStart = new DateOnly(2026, 1, 1),
            PlannedDue = null
        });

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.CalculateForProjectsAsync(new[] { project }, "po-1", default);
        var health = result[project.Id];

        // 15 profile + 25 procurement N/A + 20 no historical stages + 6/15 current stage.
        Assert.Equal(66, health.HealthPercent);
        Assert.Contains(health.GapDetails, gap => gap.Code == "CURRENT_STAGE_PDC" && gap.FieldLabel == "FS — PDC");
        Assert.DoesNotContain(health.GapDetails, gap => gap.Code == "CURRENT_STAGE_ACTUAL_START");
    }


    [Fact]
    public async Task CoreProfileGap_UsesProjectOfficerUpdateRouteForAssignedOfficer()
    {
        await using var db = CreateContext();
        var project = NewProject();
        project.Description = null;
        project.LeadPoUserId = "po-1";

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.CalculateForProjectsAsync(new[] { project }, "PO-1", default);
        var gap = Assert.Single(result[project.Id].GapDetails.Where(item => item.Code == "PROJECT_DESCRIPTION"));

        Assert.Equal($"/Projects/Meta/Request/{project.Id}", gap.ActionUrl);
    }

    [Fact]
    public async Task CoreProfileGap_UsesDirectEditRouteForHodOrAdminContext()
    {
        await using var db = CreateContext();
        var project = NewProject();
        project.Description = null;
        project.LeadPoUserId = "po-1";

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.CalculateForProjectsAsync(new[] { project }, "hod-1", default);
        var gap = Assert.Single(result[project.Id].GapDetails.Where(item => item.Code == "PROJECT_DESCRIPTION"));

        Assert.Equal($"/Projects/Meta/Edit/{project.Id}", gap.ActionUrl);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(null, false)]
    public async Task Procurement_PncCostGapUsesCurrentPublishedApplicability(
        bool? approvedPlanApplicability,
        bool publishedApplicability)
    {
        await using var db = CreateContext();
        var project = NewProject();
        project.PncApplicable = publishedApplicability;
        project.ProjectStages.Add(new ProjectStage
        {
            StageCode = StageCodes.PNC,
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2026, 1, 1),
            CompletedOn = new DateOnly(2026, 1, 2)
        });
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        if (approvedPlanApplicability.HasValue)
        {
            db.PlanVersions.Add(new PlanVersion
            {
                ProjectId = project.Id,
                VersionNo = 1,
                Status = PlanVersionStatus.Approved,
                PncApplicable = approvedPlanApplicability.Value,
                CreatedByUserId = "seed",
                ApprovedOn = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            });
            await db.SaveChangesAsync();
        }

        // A retained completed PNC row has no procurement cost fact. Current
        // publication alone decides whether that missing cost is a health gap.
        var result = await CreateService(db).CalculateForProjectIdsAsync(
            new[] { project.Id }, "po-1", default);
        var health = result[project.Id];
        var procurement = Assert.Single(health.Components.Where(component => component.Code == "PROCUREMENT"));

        if (publishedApplicability)
        {
            var gap = Assert.Single(procurement.Gaps);
            Assert.Equal("PNC_COST", gap.Code);
            Assert.Equal(StageCodes.PNC, gap.StageCode);
            Assert.Contains(health.GapDetails, item => item.Code == "PNC_COST");
            Assert.Equal(0m, procurement.EarnedPoints);
        }
        else
        {
            Assert.Empty(procurement.Gaps);
            Assert.DoesNotContain(health.GapDetails, gap => gap.Code == "PNC_COST");
            Assert.Equal(procurement.MaximumPoints, procurement.EarnedPoints);
        }
    }
    private static ProjectRecordHealthService CreateService(ApplicationDbContext db)
        => new(db, new ProjectProcurementReadService(db));

    private static Project NewProject()
        => new()
        {
            Name = "Test Project",
            Description = "A sufficiently detailed project description for completeness scoring.",
            CreatedByUserId = "seed"
        };

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
