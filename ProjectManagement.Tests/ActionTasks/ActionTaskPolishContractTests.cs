using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ProjectManagement.Tests.ActionTasks;

public sealed class ActionTaskPolishContractTests
{
    [Fact]
    public void SprintAndBacklogMoves_SitUnderMore_WhileEditReassignPriorityAndDateStayDirect()
    {
        var bar = ReadRepoFile("Pages", "ActionTasks", "_TaskActionBar.cshtml");
        var script = ReadRepoFile("wwwroot", "js", "pages", "action-tasks", "task-interaction.js");

        var more = bar.IndexOf("data-at-task-action-more", StringComparison.Ordinal);
        Assert.True(more > 0);
        foreach (var direct in new[] { "data-at-v22-open=\"edit-task\"", "data-at-v22-open=\"reassign\"", "data-at-v22-open=\"priority\"", "data-at-v22-open=\"change-date\"" })
        {
            Assert.InRange(bar.IndexOf(direct, StringComparison.Ordinal), 1, more);
        }

        foreach (var planning in new[] { "data-at-v22-open=\"assign-sprint\"", "data-at-v22-open=\"add-sprint\"", "data-at-v22-open=\"remove-sprint\"", "data-at-v22-open=\"backlog\"" })
        {
            Assert.True(bar.IndexOf(planning, StringComparison.Ordinal) > more, planning);
        }

        Assert.Contains("opener.closest('[data-at-task-action-more]')?.removeAttribute('open');", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_LinkEachOfficerToTheirTasks_AndOfferTheDataCorrectionFilterOnlyWhenNeeded()
    {
        var reports = ReadRepoFile("Pages", "ActionTasks", "_TaskReports.cshtml");
        var model = ReadRepoFile("Pages", "ActionTasks", "Index.cshtml.cs");
        var board = ReadRepoFile("Pages", "ActionTasks", "_PlanningExecuteTab.cshtml");

        Assert.Contains("asp-route-FilterAssigneeUserId=\"@item.ResponsiblePersonUserId\"", reports, StringComparison.Ordinal);
        Assert.Contains("@foreach (var bucket in Model.ReportBucketOptions)", reports, StringComparison.Ordinal);
        Assert.DoesNotContain("Invalid State", reports + model, StringComparison.Ordinal);
        Assert.Contains("InvalidStateRows.Any() || string.Equals(currentFilter, \"Invalid\"", model, StringComparison.Ordinal);
        Assert.Contains("asp-route-FilterBucket=\"OutsideSprint\"", board, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return File.ReadAllText(Path.Combine(new[] { current.FullName }.Concat(segments).ToArray()));
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }
}
