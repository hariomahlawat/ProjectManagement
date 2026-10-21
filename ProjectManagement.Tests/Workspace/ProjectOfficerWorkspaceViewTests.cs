using ProjectManagement.Services.Workspace;
using Xunit;

namespace ProjectManagement.Tests.Workspace;

// Views folded into the briefing desk keep their addresses and land on the matching section.
public sealed class ProjectOfficerWorkspaceViewTests
{
    [Theory]
    [InlineData("actions", null)]
    [InlineData("action-queue", null)]
    [InlineData("tasks", "bd-section-task")]
    [InlineData("assigned-tasks", "bd-section-task")]
    [InlineData("ideas", "bd-section-idea")]
    [InlineData("my-ideas", "bd-section-idea")]
    [InlineData("follow-ups", "bd-week-title")]
    [InlineData("reminders", "bd-week-title")]
    public void FoldedViews_RedirectToTheDesk(string route, string? section)
    {
        var view = ProjectOfficerWorkspaceViewParser.Parse(route);

        Assert.True(view.IsFoldedIntoDesk(out var deskSection));
        Assert.Equal(section, deskSection);
    }

    [Theory]
    [InlineData("overview")]
    [InlineData("projects")]
    [InlineData("conference")]
    [InlineData("documents")]
    [InlineData("activity")]
    public void RemainingViews_AreServedDirectly(string route)
        => Assert.False(ProjectOfficerWorkspaceViewParser.Parse(route).IsFoldedIntoDesk(out _));
}
