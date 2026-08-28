using System;
using System.Collections.Generic;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests.Stages;

public sealed class StageDateSuggestionResolverTests
{
    [Fact]
    public void Resolve_UsesImmediateCompletedDependency()
    {
        var workflow = Workflow(
            new[]
            {
                StageDefinition("FS", "Feasibility Study"),
                StageDefinition("SOW", "SOW Vetting"),
                StageDefinition("IPA", "In-Principle Approval"),
                StageDefinition("AON", "Acceptance of Necessity"),
                StageDefinition("BID", "Bidding/Tendering")
            },
            ("SOW", "FS"),
            ("IPA", "SOW"),
            ("AON", "IPA"),
            ("BID", "AON"));

        var stages = new[]
        {
            Stage("AON", StageStatus.Completed, new DateOnly(2026, 6, 10)),
            Stage("BID", StageStatus.NotStarted)
        };

        var result = StageDateSuggestionResolver.Resolve(workflow, stages, "BID");

        Assert.Equal(new DateOnly(2026, 6, 11), result.SuggestedStartDate);
        Assert.Equal(new DateOnly(2026, 6, 10), result.EarliestAllowedStartDate);
        Assert.Equal(new DateOnly(2026, 6, 10), result.SourceCompletionDate);
        Assert.Equal("AON", result.SourceStageCode);
        Assert.Equal(0, result.SkippedStageCount);
    }

    [Fact]
    public void Resolve_WalksThroughSkippedDependencyUsingItsGraphAncestors()
    {
        var workflow = Workflow(
            new[]
            {
                StageDefinition("A", "Stage A"),
                StageDefinition("X", "Unrelated stage"),
                StageDefinition("B", "Stage B"),
                StageDefinition("C", "Stage C")
            },
            ("B", "A"),
            ("C", "B"));

        var stages = new[]
        {
            Stage("A", StageStatus.Completed, new DateOnly(2026, 5, 20)),
            Stage("X", StageStatus.Completed, new DateOnly(2026, 5, 30)),
            Stage("B", StageStatus.Skipped),
            Stage("C", StageStatus.NotStarted)
        };

        var result = StageDateSuggestionResolver.Resolve(workflow, stages, "C");

        Assert.Equal(new DateOnly(2026, 5, 21), result.SuggestedStartDate);
        Assert.Equal(new DateOnly(2026, 5, 20), result.EarliestAllowedStartDate);
        Assert.Equal("A", result.SourceStageCode);
        Assert.Equal(1, result.SkippedStageCount);
    }


    [Fact]
    public void Resolve_PreservesSkippedCountAcrossDependencyChain()
    {
        var workflow = Workflow(
            new[]
            {
                StageDefinition("A", "Stage A"),
                StageDefinition("B", "Stage B"),
                StageDefinition("C", "Stage C"),
                StageDefinition("D", "Stage D")
            },
            ("B", "A"),
            ("C", "B"),
            ("D", "C"));

        var stages = new[]
        {
            Stage("A", StageStatus.Completed, new DateOnly(2026, 5, 20)),
            Stage("B", StageStatus.Skipped),
            Stage("C", StageStatus.Skipped),
            Stage("D", StageStatus.NotStarted)
        };

        var result = StageDateSuggestionResolver.Resolve(workflow, stages, "D");

        Assert.Equal(new DateOnly(2026, 5, 20), result.EarliestAllowedStartDate);
        Assert.Equal("A", result.SourceStageCode);
        Assert.Equal(2, result.SkippedStageCount);
    }

    [Fact]
    public void Resolve_DoesNotJumpPastAnUnresolvedDependency()
    {
        var workflow = Workflow(
            new[]
            {
                StageDefinition("IPA", "In-Principle Approval"),
                StageDefinition("AON", "Acceptance of Necessity"),
                StageDefinition("BID", "Bidding/Tendering")
            },
            ("AON", "IPA"),
            ("BID", "AON"));

        var stages = new[]
        {
            Stage("IPA", StageStatus.Completed, new DateOnly(2026, 5, 20)),
            Stage("AON", StageStatus.InProgress),
            Stage("BID", StageStatus.NotStarted)
        };

        var result = StageDateSuggestionResolver.Resolve(workflow, stages, "BID");

        Assert.Null(result.SuggestedStartDate);
        Assert.Null(result.EarliestAllowedStartDate);
        Assert.Equal("AON", result.SourceStageCode);
    }

    [Fact]
    public void Resolve_BenchmarkingUsesBidRatherThanTechnicalEvaluation()
    {
        var workflow = ProcurementParallelWorkflow();
        var bidCompleted = new DateOnly(2026, 8, 6);

        var stages = new[]
        {
            Stage(StageCodes.BID, StageStatus.Completed, bidCompleted),
            Stage(StageCodes.TEC, StageStatus.Completed, new DateOnly(2026, 8, 21)),
            Stage(StageCodes.BM, StageStatus.NotStarted)
        };

        var result = StageDateSuggestionResolver.Resolve(workflow, stages, StageCodes.BM);

        Assert.Equal(bidCompleted, result.EarliestAllowedStartDate);
        Assert.Equal(bidCompleted.AddDays(1), result.SuggestedStartDate);
        Assert.Equal(StageCodes.BID, result.SourceStageCode);
        Assert.Equal("Bidding/Tendering", result.SourceStageName);
    }

    [Fact]
    public void Resolve_ConvergingStageUsesLatestDependencyCompletion()
    {
        var workflow = ProcurementParallelWorkflow();
        var technicalEvaluationCompleted = new DateOnly(2026, 8, 25);
        var benchmarkingCompleted = new DateOnly(2026, 8, 21);

        var stages = new[]
        {
            Stage(StageCodes.BID, StageStatus.Completed, new DateOnly(2026, 8, 6)),
            Stage(StageCodes.TEC, StageStatus.Completed, technicalEvaluationCompleted),
            Stage(StageCodes.BM, StageStatus.Completed, benchmarkingCompleted),
            Stage(StageCodes.COB, StageStatus.NotStarted)
        };

        var result = StageDateSuggestionResolver.Resolve(workflow, stages, StageCodes.COB);

        Assert.Equal(technicalEvaluationCompleted, result.EarliestAllowedStartDate);
        Assert.Equal(technicalEvaluationCompleted.AddDays(1), result.SuggestedStartDate);
        Assert.Equal(StageCodes.TEC, result.SourceStageCode);
    }

    [Fact]
    public void Resolve_ConvergingStageUsesBenchmarkingWhenItCompletesLater()
    {
        var workflow = ProcurementParallelWorkflow();
        var technicalEvaluationCompleted = new DateOnly(2026, 8, 21);
        var benchmarkingCompleted = new DateOnly(2026, 8, 25);

        var stages = new[]
        {
            Stage(StageCodes.BID, StageStatus.Completed, new DateOnly(2026, 8, 6)),
            Stage(StageCodes.TEC, StageStatus.Completed, technicalEvaluationCompleted),
            Stage(StageCodes.BM, StageStatus.Completed, benchmarkingCompleted),
            Stage(StageCodes.COB, StageStatus.NotStarted)
        };

        var result = StageDateSuggestionResolver.Resolve(workflow, stages, StageCodes.COB);

        Assert.Equal(benchmarkingCompleted, result.EarliestAllowedStartDate);
        Assert.Equal(benchmarkingCompleted.AddDays(1), result.SuggestedStartDate);
        Assert.Equal(StageCodes.BM, result.SourceStageCode);
    }

    [Fact]
    public void Resolve_ReturnsNoBoundaryWhenAnyMandatoryDependencyIsUnresolved()
    {
        var workflow = ProcurementParallelWorkflow();
        var stages = new[]
        {
            Stage(StageCodes.TEC, StageStatus.Completed, new DateOnly(2026, 8, 21)),
            Stage(StageCodes.BM, StageStatus.InProgress),
            Stage(StageCodes.COB, StageStatus.NotStarted)
        };

        var result = StageDateSuggestionResolver.Resolve(workflow, stages, StageCodes.COB);

        Assert.Null(result.EarliestAllowedStartDate);
        Assert.Null(result.SuggestedStartDate);
        Assert.Equal(StageCodes.BM, result.SourceStageCode);
    }

    [Fact]
    public void Resolve_ReturnsNoSuggestionForStageWithoutDependencies()
    {
        var workflow = Workflow(
            new[] { StageDefinition("FS", "Feasibility Study") });

        var result = StageDateSuggestionResolver.Resolve(
            workflow,
            new[] { Stage("FS", StageStatus.NotStarted) },
            "FS");

        Assert.Null(result.SuggestedStartDate);
        Assert.Null(result.EarliestAllowedStartDate);
        Assert.Null(result.SourceStageCode);
    }

    private static ProjectStageWorkflowSnapshot ProcurementParallelWorkflow() => Workflow(
        new[]
        {
            StageDefinition(StageCodes.BID, "Bidding/Tendering"),
            StageDefinition(StageCodes.TEC, "Technical Evaluation"),
            StageDefinition(StageCodes.BM, "Benchmarking"),
            StageDefinition(StageCodes.COB, "Commercial Bid Opening")
        },
        (StageCodes.TEC, StageCodes.BID),
        (StageCodes.BM, StageCodes.BID),
        (StageCodes.COB, StageCodes.TEC),
        (StageCodes.COB, StageCodes.BM));

    private static ProjectStageWorkflowSnapshot Workflow(
        IReadOnlyList<WorkflowStageDefinition> stages,
        params (string Stage, string Predecessor)[] dependencies)
    {
        var dependencyMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (stage, predecessor) in dependencies)
        {
            if (!dependencyMap.TryGetValue(stage, out var existing))
            {
                dependencyMap[stage] = new[] { predecessor };
                continue;
            }

            var expanded = new string[existing.Count + 1];
            for (var index = 0; index < existing.Count; index++)
            {
                expanded[index] = existing[index];
            }
            expanded[^1] = predecessor;
            dependencyMap[stage] = expanded;
        }

        return new ProjectStageWorkflowSnapshot(
            ProcurementWorkflow.VersionV2,
            stages,
            dependencyMap,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            pncApplicable: true);
    }

    private static WorkflowStageDefinition StageDefinition(string code, string name) => new(code, name);

    private static ProjectStage Stage(
        string code,
        StageStatus status,
        DateOnly? completedOn = null) => new()
        {
            StageCode = code,
            Status = status,
            CompletedOn = completedOn
        };
}
