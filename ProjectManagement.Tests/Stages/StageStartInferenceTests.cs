using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests.Stages;

/// <summary>
/// A stage with no recorded start is measured from the day after its required predecessors
/// finished; the timeline and every analytics figure share this rule.
/// </summary>
public sealed class StageStartInferenceTests : IDisposable
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;

    public StageStartInferenceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public void Infer_StartsACompletedStageTheDayAfterItsPredecessor()
    {
        var inferred = StageStartInference.Infer(StageCodes.SOW, StageStatus.Completed, Jan1.AddDays(20), Suggestion(Jan1.AddDays(9)));

        Assert.Equal(Jan1.AddDays(10), inferred);
    }

    [Fact]
    public void Infer_NeverStartsAStageAfterItsOwnCompletion()
    {
        // Both stages finished the same day: the later one is counted as a one-day stage.
        var inferred = StageStartInference.Infer(StageCodes.SOW, StageStatus.Completed, Jan1, Suggestion(Jan1));

        Assert.Equal(Jan1, inferred);
    }

    [Fact]
    public void Infer_StartsARunningStageAndLeavesOutOneThatHasNotBegun()
    {
        Assert.Equal(Jan1.AddDays(1), StageStartInference.Infer(StageCodes.SOW, StageStatus.InProgress, null, Suggestion(Jan1)));
        Assert.Null(StageStartInference.Infer(StageCodes.SOW, StageStatus.NotStarted, null, Suggestion(Jan1)));
        Assert.Null(StageStartInference.Infer(StageCodes.SOW, StageStatus.Completed, null, Suggestion(Jan1)));
        Assert.Null(StageStartInference.Infer(StageCodes.SOW, StageStatus.Completed, Jan1, StageDateSuggestion.None));
    }

    [Fact]
    public void Infer_LeavesTheStartOfAnArppManagedIpaUnrecorded()
    {
        Assert.Null(StageStartInference.Infer(StageCodes.IPA, StageStatus.Completed, Jan1.AddDays(20), Suggestion(Jan1), Authority(1, Jan1.AddDays(20))));
        Assert.Equal(Jan1.AddDays(1), StageStartInference.Infer(StageCodes.IPA, StageStatus.Completed, Jan1.AddDays(20), Suggestion(Jan1)));
    }

    [Fact]
    public async Task ResolveAsync_KeepsARecordedStartAndInfersTheMissingOne()
    {
        var project = await SeedAsync(
            Stage(StageCodes.FS, StageStatus.Completed, start: Jan1, completed: Jan1.AddDays(9)),
            Stage(StageCodes.SOW, StageStatus.Completed, start: null, completed: Jan1.AddDays(19)),
            Stage(StageCodes.IPA, StageStatus.InProgress, start: null, completed: null),
            Stage(StageCodes.AON, StageStatus.NotStarted, start: null, completed: null));

        var starts = (await ResolveAsync(project))[project.Id];

        Assert.Equal(new EffectiveStageStart(Jan1, IsInferred: false), starts[StageCodes.FS]);
        Assert.Equal(new EffectiveStageStart(Jan1.AddDays(10), IsInferred: true), starts[StageCodes.SOW]);
        Assert.Equal(new EffectiveStageStart(Jan1.AddDays(20), IsInferred: true), starts[StageCodes.IPA]);
        Assert.False(starts.ContainsKey(StageCodes.AON));
    }

    [Fact]
    public async Task ResolveAsync_StartsAConvergingStageAfterTheLaterParallelBranch()
    {
        var project = await SeedAsync(
            Stage(StageCodes.BID, StageStatus.Completed, start: Jan1, completed: Jan1.AddDays(30)),
            Stage(StageCodes.TEC, StageStatus.Completed, start: null, completed: Jan1.AddDays(40)),
            Stage(StageCodes.BM, StageStatus.Completed, start: null, completed: Jan1.AddDays(45)),
            Stage(StageCodes.COB, StageStatus.Completed, start: null, completed: Jan1.AddDays(50)));

        var starts = (await ResolveAsync(project))[project.Id];

        Assert.Equal(Jan1.AddDays(31), starts[StageCodes.TEC].Date);
        Assert.Equal(Jan1.AddDays(31), starts[StageCodes.BM].Date);
        Assert.Equal(Jan1.AddDays(46), starts[StageCodes.COB].Date);
    }

    [Fact]
    public async Task ResolveAsync_LooksPastASkippedPredecessor_AndNotPastAnUndatedOne()
    {
        // V2 order: FS → SoW → IPA → AoN → Bid.
        var project = await SeedAsync(
            Stage(StageCodes.FS, StageStatus.Completed, start: Jan1, completed: Jan1.AddDays(9)),
            Stage(StageCodes.SOW, StageStatus.Completed, start: null, completed: Jan1.AddDays(19)),
            Stage(StageCodes.IPA, StageStatus.Skipped, start: null, completed: null),
            Stage(StageCodes.AON, StageStatus.Completed, start: null, completed: Jan1.AddDays(40)),
            UndatedCompletion(StageCodes.BID),
            Stage(StageCodes.TEC, StageStatus.Completed, start: null, completed: Jan1.AddDays(70)));

        var starts = (await ResolveAsync(project))[project.Id];

        Assert.Equal(Jan1.AddDays(20), starts[StageCodes.AON].Date); // the day after SoW, past the skipped IPA
        Assert.False(starts.ContainsKey(StageCodes.BID)); // completed with no date: nothing to measure
        Assert.False(starts.ContainsKey(StageCodes.TEC)); // its predecessor gives no date to start from
    }

    [Fact]
    public async Task ResolveAsync_StartsTheStageAfterAnArppManagedIpaFromTheArppDate()
    {
        var project = await SeedAsync(
            Stage(StageCodes.FS, StageStatus.Completed, start: Jan1, completed: Jan1.AddDays(9)),
            Stage(StageCodes.SOW, StageStatus.Completed, start: Jan1.AddDays(10), completed: Jan1.AddDays(19)),
            Stage(StageCodes.IPA, StageStatus.Completed, start: null, completed: Jan1.AddDays(25)),
            Stage(StageCodes.AON, StageStatus.Completed, start: null, completed: Jan1.AddDays(60)));

        var service = new StageStartInferenceService(
            new ProjectStageWorkflowPolicy(_db, new WorkflowStageMetadataProvider()),
            new StubIpaAuthority(Authority(project.Id, Jan1.AddDays(29))));
        var starts = (await service.ResolveAsync(Facts(project)))[project.Id];

        Assert.False(starts.ContainsKey(StageCodes.IPA));
        Assert.Equal(Jan1.AddDays(30), starts[StageCodes.AON].Date);
    }

    [Fact]
    public async Task GetManyAsync_ReadsEachProjectsOwnWorkflowVersion()
    {
        var v1 = await SeedAsync(ProcurementWorkflow.VersionV1, Stage(StageCodes.FS, StageStatus.NotStarted, null, null));
        var v2 = await SeedAsync(ProcurementWorkflow.VersionV2, Stage(StageCodes.FS, StageStatus.NotStarted, null, null));
        var policy = new ProjectStageWorkflowPolicy(_db, new WorkflowStageMetadataProvider());

        var workflows = await policy.GetManyAsync(new[] { v1.Id, v2.Id, 9999 });

        Assert.Equal(2, workflows.Count);
        Assert.Equal(new[] { StageCodes.FS }, workflows[v1.Id].RequiredPredecessors(StageCodes.IPA));
        Assert.Equal(new[] { StageCodes.SOW }, workflows[v2.Id].RequiredPredecessors(StageCodes.IPA));
    }

    private async Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, EffectiveStageStart>>> ResolveAsync(Project project)
    {
        var service = new StageStartInferenceService(
            new ProjectStageWorkflowPolicy(_db, new WorkflowStageMetadataProvider()),
            new StubIpaAuthority(null));
        return await service.ResolveAsync(Facts(project));
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<StageStartFact>> Facts(Project project) =>
        new Dictionary<int, IReadOnlyList<StageStartFact>>
        {
            [project.Id] = project.ProjectStages
                .Select(stage => new StageStartFact(stage.StageCode, stage.Status, stage.ActualStart, stage.CompletedOn))
                .ToList()
        };

    private Task<Project> SeedAsync(params ProjectStage[] stages) =>
        SeedAsync(ProcurementWorkflow.VersionV2, stages);

    private async Task<Project> SeedAsync(string workflowVersion, params ProjectStage[] stages)
    {
        var project = new Project
        {
            Name = $"Project {Guid.NewGuid():N}",
            CreatedByUserId = "seed",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            WorkflowVersion = workflowVersion,
            PncApplicable = false
        };
        foreach (var stage in stages)
        {
            project.ProjectStages.Add(stage);
        }

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    private static ProjectStage Stage(string code, StageStatus status, DateOnly? start, DateOnly? completed) => new()
    {
        StageCode = code,
        SortOrder = Array.IndexOf(StageCodes.All, code) * 10,
        Status = status,
        ActualStart = start,
        CompletedOn = completed
    };

    /// <summary>A completion awaiting its backfilled date, the only way a completed stage has none.</summary>
    private static ProjectStage UndatedCompletion(string code)
    {
        var stage = Stage(code, StageStatus.Completed, start: null, completed: null);
        stage.RequiresBackfill = true;
        return stage;
    }

    private static StageDateSuggestion Suggestion(DateOnly predecessorCompleted) =>
        new(predecessorCompleted.AddDays(1), predecessorCompleted, StageCodes.FS, "Feasibility Study", predecessorCompleted, 0);

    private static ArppIpaStageAuthority Authority(int projectId, DateOnly issueDate) =>
        new(projectId, 2026, 1, ArppIssueKind.Original, 1, "ARPP 2026-27", issueDate, 1, "1", null);

    private sealed class StubIpaAuthority : IArppIpaStageAuthorityService
    {
        private readonly ArppIpaStageAuthority? _authority;

        public StubIpaAuthority(ArppIpaStageAuthority? authority) => _authority = authority;

        public Task<ArppIpaStageAuthority?> ResolveAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_authority?.ProjectId == projectId ? _authority : null);

        public Task<IReadOnlyDictionary<int, ArppIpaStageAuthority>> ResolveManyAsync(
            IReadOnlyCollection<int> projectIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, ArppIpaStageAuthority>>(
                _authority is not null && projectIds.Contains(_authority.ProjectId)
                    ? new Dictionary<int, ArppIpaStageAuthority> { [_authority.ProjectId] = _authority }
                    : new Dictionary<int, ArppIpaStageAuthority>());

        public Task<bool> IsManagedAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_authority?.ProjectId == projectId);

        public Task EnsureManualLifecycleMutationAllowedAsync(int projectId, string stageCode, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
