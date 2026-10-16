using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Tests.Stages;

public sealed class StageExecutionDependencyTests
{
    [Theory]
    [InlineData(ProcurementWorkflow.VersionV1, true)]
    [InlineData(ProcurementWorkflow.VersionV1, false)]
    [InlineData(ProcurementWorkflow.VersionV2, true)]
    [InlineData(ProcurementWorkflow.VersionV2, false)]
    public void Snapshot_ExposesEffectiveConditionalGraphAndOrderedClosure(string version, bool applicable)
    {
        var workflow = Snapshot(version, applicable);
        Assert.Equal(applicable ? new[] { StageCodes.COB, StageCodes.PNC } : new[] { StageCodes.COB }, workflow.RequiredPredecessors(StageCodes.EAS));
        Assert.Equal(workflow.RequiredPredecessors(StageCodes.EAS), workflow.Dependencies[StageCodes.EAS]);
        var closure = workflow.RequiredPredecessorClosure(StageCodes.EAS);
        Assert.Contains(StageCodes.COB, closure);
        Assert.Equal(applicable, closure.Contains(StageCodes.PNC));
        Assert.Equal(closure.OrderBy(workflow.OrderOf).ThenBy(c => c, StringComparer.OrdinalIgnoreCase), closure);
        Assert.Equal(closure, workflow.RequiredPredecessorClosure(StageCodes.EAS));
    }

    public static IEnumerable<object[]> EasCases()
    {
        foreach (var version in new[] { ProcurementWorkflow.VersionV1, ProcurementWorkflow.VersionV2 })
        foreach (var applicable in new[] { true, false })
        foreach (var status in Enum.GetValues<StageStatus>())
            yield return new object[] { version, applicable, status };
    }

    [Theory]
    [MemberData(nameof(EasCases))]
    public void Eas_AllExecutionGuardsUseApplicabilityAndTerminalPredecessors(string version, bool applicable, StageStatus pncStatus)
    {
        using var db = Context();
        var rules = new StageRulesService(db, new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider()));
        var workflow = Snapshot(version, applicable);
        StageRulesContext ContextFor(StageStatus eas) => new(new[]
        {
            new StageSnapshot(StageCodes.COB, StageStatus.Completed, null, new DateOnly(2026, 1, 1)),
            new StageSnapshot(StageCodes.PNC, pncStatus, null, null),
            new StageSnapshot(StageCodes.EAS, eas, null, null)
        }, workflow.Dependencies);
        var allowed = !applicable || pncStatus is StageStatus.Completed or StageStatus.Skipped;
        Assert.Equal(allowed, rules.CanStart(ContextFor(StageStatus.NotStarted), StageCodes.EAS).Allowed);
        Assert.Equal(allowed, rules.CanComplete(ContextFor(StageStatus.InProgress), StageCodes.EAS).Allowed);
        Assert.Equal(allowed, rules.CanSkip(ContextFor(StageStatus.NotStarted), StageCodes.EAS).Allowed);
    }

    public static IEnumerable<object[]> CobCases()
    {
        foreach (var version in new[] { ProcurementWorkflow.VersionV1, ProcurementWorkflow.VersionV2 })
        foreach (var tec in Enum.GetValues<StageStatus>())
        foreach (var bm in Enum.GetValues<StageStatus>())
            yield return new object[] { version, tec, bm };
    }

    [Theory]
    [MemberData(nameof(CobCases))]
    public void Cob_AllExecutionGuardsAcceptCompletedOrSkippedOnly(string version, StageStatus tec, StageStatus bm)
    {
        using var db = Context();
        var rules = new StageRulesService(db, new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider()));
        StageRulesContext ContextFor(StageStatus cob) => new(new[]
        {
            new StageSnapshot(StageCodes.TEC, tec, null, null),
            new StageSnapshot(StageCodes.BM, bm, null, null),
            new StageSnapshot(StageCodes.COB, cob, null, null)
        }, Snapshot(version, true).Dependencies);
        var allowed = (tec is StageStatus.Completed or StageStatus.Skipped) && (bm is StageStatus.Completed or StageStatus.Skipped);
        Assert.Equal(allowed, rules.CanStart(ContextFor(StageStatus.NotStarted), StageCodes.COB).Allowed);
        Assert.Equal(allowed, rules.CanComplete(ContextFor(StageStatus.InProgress), StageCodes.COB).Allowed);
        Assert.Equal(allowed, rules.CanSkip(ContextFor(StageStatus.NotStarted), StageCodes.COB).Allowed);
    }

    [Theory]
    [InlineData(ProcurementWorkflow.VersionV1, false)]
    [InlineData(ProcurementWorkflow.VersionV1, true)]
    [InlineData(ProcurementWorkflow.VersionV2, false)]
    [InlineData(ProcurementWorkflow.VersionV2, true)]
    public async Task Guards_DoNotAdvertiseLaterAdvancementWithUnresolvedApplicablePncAncestor(string version, bool missingPnc)
    {
        await using var db = Context();
        db.Projects.Add(new ProjectManagement.Models.Project
        {
            Id = 1, Name = "Project", CreatedByUserId = "seed",
            WorkflowVersion = version, PncApplicable = true
        });
        var codes = new WorkflowStageMetadataProvider().GetStages(version).Select(s => s.Code);
        foreach (var code in codes)
        {
            if (code == StageCodes.PNC && missingPnc) continue;
            db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = 1, StageCode = code,
                Status = code is StageCodes.SO or StageCodes.PNC ? StageStatus.NotStarted : StageStatus.Completed
            });
        }
        await db.SaveChangesAsync();
        var rules = new StageRulesService(db, new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider()));
        var context = await rules.GetContextAsync(1, CancellationToken.None);
        Assert.False(rules.CanStart(context, StageCodes.SO).Allowed);
        Assert.False(rules.CanSkip(context, StageCodes.SO).Allowed);
        var so = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.SO);
        so.Status = StageStatus.InProgress;
        await db.SaveChangesAsync();
        Assert.False(rules.CanComplete(await rules.GetContextAsync(1, CancellationToken.None), StageCodes.SO).Allowed);
    }

    private static ProjectStageWorkflowSnapshot Snapshot(string version, bool applicable) => new(
        version, new WorkflowStageMetadataProvider().GetStages(version),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [StageCodes.COB] = new[] { StageCodes.TEC, StageCodes.BM },
            [StageCodes.PNC] = new[] { StageCodes.COB },
            [StageCodes.EAS] = new[] { StageCodes.COB }
        }, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { StageCodes.PNC }, applicable);

    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
