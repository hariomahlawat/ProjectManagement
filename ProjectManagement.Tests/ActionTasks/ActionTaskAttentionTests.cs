using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;
using Xunit;

namespace ProjectManagement.Tests.ActionTasks;

public sealed class ActionTaskAttentionTests
{
    private static readonly DateTime IstToday = new(2026, 10, 23);

    [Theory]
    // Submitted at 23:00 IST on 20 Oct (17:30 UTC): three IST days ago.
    [InlineData("2026-10-20T17:30:00", 3)]
    // Submitted at 00:30 IST on 23 Oct (19:00 UTC the day before): today.
    [InlineData("2026-10-22T19:00:00", 0)]
    public void DaysAwaitingClosure_CountsIstCalendarDays(string submittedUtc, int expected)
    {
        var task = new ActionTaskItem { Status = ActionTaskStatuses.Submitted, SubmittedOn = DateTime.Parse(submittedUtc) };

        Assert.Equal(expected, ActionTaskAttention.DaysAwaitingClosure(task, IstToday));
    }

    [Fact]
    public void DaysAwaitingClosure_IsNullUnlessAwaitingClosure_AndFallsBackToTheAssignmentDate()
    {
        Assert.Null(ActionTaskAttention.DaysAwaitingClosure(new ActionTaskItem { Status = ActionTaskStatuses.InProgress, SubmittedOn = IstToday }, IstToday));
        // No submission time recorded: counted from the assignment, as Reports' pending-closure ageing does.
        Assert.Equal(5, ActionTaskAttention.DaysAwaitingClosure(
            new ActionTaskItem { Status = ActionTaskStatuses.Submitted, SubmittedOn = null, AssignedOn = new DateTime(2026, 10, 18, 5, 0, 0) },
            IstToday));
    }

    [Theory]
    [InlineData("2026-10-11", 12)]
    [InlineData("2026-10-23", 0)]
    [InlineData("2026-10-25", -2)]
    public void DaysPastDue_CountsFromTheDueDate(string due, int expected)
    {
        Assert.Equal(expected, ActionTaskAttention.DaysPastDue(new ActionTaskItem { DueDate = DateTime.Parse(due) }, IstToday));
    }

    [Fact]
    public async Task LatestBlocks_TakeTheMostRecentMoveToBlocked_PerTask()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.ActionTaskAuditLogs.AddRange(
            Log(1, ActionTaskStatuses.Blocked, "Old reason", new DateTime(2026, 10, 10, 5, 0, 0)),
            Log(1, ActionTaskStatuses.InProgress, "Resumed", new DateTime(2026, 10, 12, 5, 0, 0)),
            Log(1, ActionTaskStatuses.Blocked, "  Vendor has not replied.  ", new DateTime(2026, 10, 20, 5, 0, 0)),
            Log(2, ActionTaskStatuses.Blocked, "   ", new DateTime(2026, 10, 21, 5, 0, 0)),
            Log(3, ActionTaskStatuses.Blocked, "Not asked for", new DateTime(2026, 10, 21, 5, 0, 0)));
        await db.SaveChangesAsync();

        var blocks = await ActionTaskBlocks.LatestAsync(db, new[] { 1, 2 });

        Assert.Equal(2, blocks.Count);
        Assert.Equal("Vendor has not replied.", blocks[1].Reason);
        Assert.Equal(new DateTime(2026, 10, 20, 5, 0, 0), blocks[1].BlockedAtUtc);
        Assert.Equal(DateTimeKind.Utc, blocks[1].BlockedAtUtc.Kind);
        Assert.Null(blocks[2].Reason);
        Assert.Empty(await ActionTaskBlocks.LatestAsync(db, Array.Empty<int>()));
    }

    [Fact]
    public async Task Assigners_AreTheCreator_UnlessLaterAssignedFromTheBacklogOrReassigned()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.ActionTaskAuditLogs.AddRange(
            Audit(2, "TaskAssignedToSprint", "hod", new DateTime(2026, 10, 5, 5, 0, 0)),
            Audit(3, "TaskAssignedToSprint", "hod", new DateTime(2026, 10, 5, 5, 0, 0)),
            Audit(3, "TaskReassigned", "comdt", new DateTime(2026, 10, 9, 5, 0, 0)),
            Audit(3, "StatusUpdated", "officer", new DateTime(2026, 10, 12, 5, 0, 0)));
        await db.SaveChangesAsync();
        var tasks = new[]
        {
            new ActionTaskItem { Id = 1, CreatedByUserId = "comdt" },   // created with an assignee
            new ActionTaskItem { Id = 2, CreatedByUserId = "officer" }, // backlog item, assigned by the HoD
            new ActionTaskItem { Id = 3, CreatedByUserId = "officer" }  // then reassigned by the Comdt
        };

        var assigners = await ActionTaskAssigners.ResolveAsync(db, tasks);

        Assert.Equal("comdt", assigners[1]);
        Assert.Equal("hod", assigners[2]);
        Assert.Equal("comdt", assigners[3]);
    }

    private static ActionTaskAuditLog Audit(int taskId, string action, string by, DateTime at) => new()
    {
        TaskId = taskId,
        ActionType = action,
        PerformedByUserId = by,
        PerformedByRole = "HoD",
        PerformedAt = at
    };

    [Fact]
    public void Views_ShowWhatToActOn_WhoAssignedIt_AndOfferChat()
    {
        var dashboard = ReadRepoFile("Pages", "ActionTasks", "_TaskDashboard.cshtml");
        var register = ReadRepoFile("Pages", "ActionTasks", "_TaskRegister.cshtml");
        var peek = ReadRepoFile("Pages", "ActionTasks", "_TaskDetails.cshtml");
        var details = ReadRepoFile("Pages", "ActionTasks", "Details.cshtml");

        Assert.Contains("Model.TaskBlocks.TryGetValue(task.Id, out var block)", dashboard, StringComparison.Ordinal);
        Assert.Contains("Model.DaysAwaitingClosure(task)", dashboard, StringComparison.Ordinal);
        Assert.Contains("<span class=\"at-attention-late\">", dashboard, StringComparison.Ordinal);
        Assert.Contains("Model.DaysOverdue(task) is int daysOverdue", register, StringComparison.Ordinal);
        Assert.Contains("data-chat-user=\"@task.AssignedToUserId\"", register, StringComparison.Ordinal);
        Assert.Contains("assigned by <span data-chat-user=\"@Model.ResolveAssignerUserId(task)\"", peek, StringComparison.Ordinal);
        Assert.Contains("Model.ResolveAssignerName() is string assignerName", details, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var current = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(current.FullName, "ProjectManagement.csproj")))
            {
                return System.IO.File.ReadAllText(System.IO.Path.Combine(new[] { current.FullName }.Concat(segments).ToArray()));
            }

            current = current.Parent;
        }

        throw new System.IO.DirectoryNotFoundException("Could not locate the ProjectManagement repository root.");
    }

    private static ActionTaskAuditLog Log(int taskId, string newValue, string remarks, DateTime at) => new()
    {
        TaskId = taskId,
        ActionType = "StatusUpdated",
        PerformedByUserId = "srinath",
        PerformedByRole = "Project Officer",
        PerformedAt = at,
        NewValue = newValue,
        Remarks = remarks
    };
}
