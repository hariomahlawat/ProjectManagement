using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class WorkspaceActionQueueBuilderTests
{
    [Fact]
    public void Build_IncludesProjectTimelineActionsInTheHeadlineCount()
    {
        var rows = new[]
        {
            new WorkspaceProjectMatrixRowVm
            {
                ProjectId = 1,
                ProjectName = "Current-stage project",
                HasCurrentStageIssue = true,
                IsCurrentStagePdcMissing = true,
                TimelineUrl = "/Projects/1/Timeline"
            },
            new WorkspaceProjectMatrixRowVm
            {
                ProjectId = 2,
                ProjectName = "Historical project",
                HasBackfill = true,
                TimelineUrl = "/Projects/2/Timeline"
            }
        };
        var remarks = new[]
        {
            new WorkspaceAttentionItemVm
            {
                Title = "Current-stage project",
                Detail = "No PO remark in last 16 days",
                Severity = "Warning",
                ActionUrl = "/Projects/1/Remarks"
            }
        };

        var result = WorkspaceActionQueueBuilder.Build(
            Array.Empty<WorkspaceAttentionItemVm>(),
            Array.Empty<WorkspaceTaskVm>(),
            remarks,
            Array.Empty<WorkspaceIdeaVm>(),
            Array.Empty<WorkspaceAotsDocumentVm>(),
            0,
            rows);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Items.Count(item => item.Type == "Timeline"));
        Assert.Contains(result.Items, item => item.Title == "Current-stage project" && item.ActionText == "Update dates");
        Assert.Contains(result.Items, item => item.Title == "Historical project" && item.ActionText == "Complete timeline");
    }

    [Fact]
    public void Build_PrioritisesCurrentStageTimelineWorkBeforeRoutineUpdates()
    {
        var rows = new[]
        {
            new WorkspaceProjectMatrixRowVm
            {
                ProjectId = 1,
                ProjectName = "Missing PDC",
                HasCurrentStageIssue = true,
                IsCurrentStagePdcMissing = true,
                TimelineUrl = "/Projects/1/Timeline"
            }
        };
        var remarks = new[]
        {
            new WorkspaceAttentionItemVm
            {
                Title = "Stale update",
                Detail = "No PO remark in last 16 days",
                Severity = "Warning",
                ActionUrl = "/Projects/2/Remarks"
            }
        };

        var result = WorkspaceActionQueueBuilder.Build(
            Array.Empty<WorkspaceAttentionItemVm>(),
            Array.Empty<WorkspaceTaskVm>(),
            remarks,
            Array.Empty<WorkspaceIdeaVm>(),
            Array.Empty<WorkspaceAotsDocumentVm>(),
            0,
            rows);

        Assert.Equal("Timeline", result.Items[0].Type);
        Assert.Equal("Current-stage PDC missing", result.Items[0].PriorityReason);
    }

    [Theory]
    [InlineData("No PO remark in last 16 days", "No Project Officer update for 16 days")]
    [InlineData("No PO remark has been added yet", "No Project Officer update has been recorded")]
    public void NormalizeProjectOfficerUpdateDetail_UsesClearUserFacingLanguage(string input, string expected)
    {
        Assert.Equal(expected, WorkspaceActionQueueBuilder.NormalizeProjectOfficerUpdateDetail(input));
    }

    [Fact]
    public void Build_PreservesTheTrueAotsUnreadCountWhenThePreviewIsCapped()
    {
        var documents = new[]
        {
            new WorkspaceAotsDocumentVm
            {
                Subject = "Document one",
                Office = "Main Office",
                Category = "AOTS",
                OpenUrl = "/Documents/1"
            }
        };

        var result = WorkspaceActionQueueBuilder.Build(
            Array.Empty<WorkspaceAttentionItemVm>(),
            Array.Empty<WorkspaceTaskVm>(),
            Array.Empty<WorkspaceAttentionItemVm>(),
            Array.Empty<WorkspaceIdeaVm>(),
            documents,
            4,
            Array.Empty<WorkspaceProjectMatrixRowVm>());

        Assert.Equal(4, result.TotalCount);
        Assert.Single(result.Items);
    }
}
