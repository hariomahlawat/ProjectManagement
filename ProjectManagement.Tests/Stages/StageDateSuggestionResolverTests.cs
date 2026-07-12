using System;
using System.Collections.Generic;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;
using Xunit;

namespace ProjectManagement.Tests.Stages;

public sealed class StageDateSuggestionResolverTests
{
    private static readonly IReadOnlyList<WorkflowStageDefinition> Workflow = new[]
    {
        new WorkflowStageDefinition("FS", "Feasibility Study"),
        new WorkflowStageDefinition("SOW", "SOW Vetting"),
        new WorkflowStageDefinition("IPA", "In-Principle Approval"),
        new WorkflowStageDefinition("AON", "Acceptance of Necessity"),
        new WorkflowStageDefinition("BID", "Bidding/Tendering")
    };

    [Fact]
    public void Resolve_UsesImmediateCompletedPredecessor()
    {
        var stages = new[]
        {
            Stage("AON", StageStatus.Completed, new DateOnly(2026, 6, 10)),
            Stage("BID", StageStatus.NotStarted)
        };

        var result = StageDateSuggestionResolver.Resolve(Workflow, stages, "BID");

        Assert.Equal(new DateOnly(2026, 6, 11), result.SuggestedStartDate);
        Assert.Equal("AON", result.SourceStageCode);
        Assert.Equal(0, result.SkippedStageCount);
    }

    [Fact]
    public void Resolve_WalksBackThroughConsecutiveSkippedStages()
    {
        var stages = new[]
        {
            Stage("SOW", StageStatus.Completed, new DateOnly(2026, 5, 20)),
            Stage("IPA", StageStatus.Skipped),
            Stage("AON", StageStatus.Skipped),
            Stage("BID", StageStatus.NotStarted)
        };

        var result = StageDateSuggestionResolver.Resolve(Workflow, stages, "BID");

        Assert.Equal(new DateOnly(2026, 5, 21), result.SuggestedStartDate);
        Assert.Equal("SOW", result.SourceStageCode);
        Assert.Equal(2, result.SkippedStageCount);
    }

    [Fact]
    public void Resolve_DoesNotJumpPastANonSkippedPredecessorWithoutCompletionDate()
    {
        var stages = new[]
        {
            Stage("IPA", StageStatus.Completed, new DateOnly(2026, 5, 20)),
            Stage("AON", StageStatus.InProgress),
            Stage("BID", StageStatus.NotStarted)
        };

        var result = StageDateSuggestionResolver.Resolve(Workflow, stages, "BID");

        Assert.Null(result.SuggestedStartDate);
        Assert.Equal("AON", result.SourceStageCode);
    }

    [Fact]
    public void Resolve_ReturnsNoSuggestionForFirstWorkflowStage()
    {
        var result = StageDateSuggestionResolver.Resolve(
            Workflow,
            new[] { Stage("FS", StageStatus.NotStarted) },
            "FS");

        Assert.Null(result.SuggestedStartDate);
        Assert.Null(result.SourceStageCode);
    }

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
