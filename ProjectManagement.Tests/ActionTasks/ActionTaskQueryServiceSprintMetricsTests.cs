using ProjectManagement.Models;
using ProjectManagement.Services.ActionTasks;

namespace ProjectManagement.Tests.ActionTasks;

public class ActionTaskQueryServiceSprintMetricsTests
{
    [Fact]
    public void BuildReadModel_ComputesActiveSprintDashboardMetricsAndClosureReview()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var activeSprint = new ActionSprint { Id = 7, Name = "Active Ops", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(14) };
        var nextSprint = new ActionSprint { Id = 8, Name = "Next Ops", Status = ActionSprintStatus.Planned, StartDate = today.AddDays(15), EndDate = today.AddDays(30) };
        var olderSprint = new ActionSprint { Id = 9, Name = "Older Ops", Status = ActionSprintStatus.Planned, StartDate = today.AddDays(-15), EndDate = today.AddDays(-1) };
        var sameStartSprint = new ActionSprint { Id = 10, Name = "Same Start Ops", Status = ActionSprintStatus.Planned, StartDate = today, EndDate = today.AddDays(7) };
        var closedLaterSprint = new ActionSprint { Id = 11, Name = "Closed Later Ops", Status = ActionSprintStatus.Closed, StartDate = today.AddDays(31), EndDate = today.AddDays(45) };
        var tasks = new[]
        {
            NewTask(1, activeSprint.Id, ActionTaskStatuses.Closed, today.AddDays(-1)),
            NewTask(2, activeSprint.Id, ActionTaskStatuses.InProgress, today.AddDays(1)),
            NewTask(3, activeSprint.Id, ActionTaskStatuses.Blocked, today.AddDays(-2)),
            NewTask(4, activeSprint.Id, ActionTaskStatuses.Submitted, today.AddDays(2)),
            NewTask(5, null, ActionTaskStatuses.Backlog, today.AddDays(3), assignedToUserId: string.Empty),
            NewTask(6, null, ActionTaskStatuses.Closed, today.AddDays(4)),
            NewTask(7, null, ActionTaskStatuses.Assigned, today.AddDays(5))
        };
        var service = CreateQueryService();

        // SECTION: Act
        var model = service.BuildReadModel(
            tasks,
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, false, false, activeSprint.Id, new[] { activeSprint, nextSprint, olderSprint, sameStartSprint, closedLaterSprint }, null, null, null, null, null, null, null),
            new Dictionary<string, string> { ["assignee"] = "Assignee" },
            new Dictionary<int, DateTime?>());

        // SECTION: Assert
        Assert.Equal("Active Ops", model.ActiveSprintMetrics.ActiveSprintName);
        Assert.Equal(4, model.ActiveSprintMetrics.TotalTasks);
        Assert.Equal(1, model.ActiveSprintMetrics.CompletedTasks);
        Assert.Equal(1, model.ActiveSprintMetrics.InProgressTasks);
        Assert.Equal(1, model.ActiveSprintMetrics.BlockedTasks);
        Assert.Equal(1, model.ActiveSprintMetrics.OverdueTasks);
        Assert.Equal(1, model.ActiveSprintMetrics.SubmittedPendingClosureTasks);
        Assert.Equal(0, model.ActiveSprintMetrics.InvalidStateTasks);
        Assert.Equal(3, model.ActiveSprintMetrics.CarryForwardCandidateTasks);
        Assert.Equal(new[] { 5 }, model.BacklogTasks.Select(t => t.Id));
        Assert.Equal(new[] { 5 }, model.SprintReadModel.BacklogTasks.Select(t => t.Id));
        Assert.Equal(new[] { new ActionTaskQueryService.CountSummary("Assignee", 1) }, model.Reports.OutsideSprintWorkloadCounts);
        Assert.Equal(1, model.SprintReadModel.ClosureReview.CompletedTasks.Count);
        Assert.Equal(3, model.SprintReadModel.ClosureReview.UnfinishedTasks.Count);
        Assert.Equal(nextSprint.Id, model.SprintReadModel.ClosureReview.TargetSprintOptions.Single().Id);
    }


    [Fact]
    public void BuildReadModel_AppliesReportsFiltersAcrossAnalyticalSections()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 21, Name = "Filtered Sprint", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(7) };
        var otherSprint = new ActionSprint { Id = 22, Name = "Other Sprint", Status = ActionSprintStatus.Planned, StartDate = today.AddDays(8), EndDate = today.AddDays(14) };
        var tasks = new[]
        {
            NewTask(11, sprint.Id, ActionTaskStatuses.Blocked, today.AddDays(2), "High", "assignee", today.AddDays(-9)),
            NewTask(12, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(4), "Normal", "assignee", today.AddDays(-1)),
            NewTask(13, otherSprint.Id, ActionTaskStatuses.Blocked, today.AddDays(2), "High", "other", today.AddDays(-16)),
            NewTask(14, null, ActionTaskStatuses.Backlog, today.AddDays(2), "High", string.Empty, today.AddDays(-5)),
            NewTask(15, null, ActionTaskStatuses.Assigned, today.AddDays(2), "Normal", "assignee", today.AddDays(-3))
        };
        var service = CreateQueryService();

        // SECTION: Act
        var model = service.BuildReadModel(
            tasks,
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, false, false, sprint.Id, new[] { sprint, otherSprint }, null, null, null, null, null, null, null, sprint.Id, "assignee", today, today.AddDays(3), ActionTaskStatuses.Blocked, "High"),
            new Dictionary<string, string> { ["assignee"] = "Responsible One", ["other"] = "Other Person" },
            new Dictionary<int, DateTime?>());

        // SECTION: Assert
        Assert.Equal(5, model.Reports.TotalTaskCount);
        Assert.Equal(1, model.Reports.FilteredTaskCount);
        Assert.Equal(new[] { new ActionTaskQueryService.CountSummary("Responsible One", 1) }, model.Reports.AssigneePendingCounts);
        Assert.Equal(new[] { new ActionTaskQueryService.CountSummary("High", 1) }, model.Reports.PriorityCounts);
        Assert.Equal(new[] { new ActionTaskQueryService.CountSummary(ActionTaskStatuses.Blocked, 1) }, model.Reports.StatusCounts);
        Assert.Equal(1, model.Reports.BlockedAgeingBuckets.Single(x => x.Name == "8-14 days assigned").Count);
        Assert.Equal(new[] { new ActionTaskQueryService.CountSummary("Filtered Sprint", 1) }, model.Reports.CarryForwardBySprint);
    }


    [Fact]
    public void BuildReadModel_RegisterOpenScopeExcludesClosedBeforeFilters()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 41, Name = "Register Sprint", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(7) };
        var tasks = new[]
        {
            NewTask(31, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(1)),
            NewTask(32, sprint.Id, ActionTaskStatuses.Closed, today.AddDays(1)),
            NewTask(33, null, ActionTaskStatuses.Backlog, today.AddDays(1), assignedToUserId: string.Empty)
        };
        var service = CreateQueryService();

        // SECTION: Act
        var model = service.BuildReadModel(
            tasks,
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, true, false, sprint.Id, new[] { sprint }, null, null, null, null, null, null, null, TaskScope: ActionTaskRegisterScopes.Open),
            new Dictionary<string, string> { ["assignee"] = "Assignee" },
            new Dictionary<int, DateTime?>());

        // SECTION: Assert
        Assert.Equal(new[] { 31, 33 }, model.TaskListTasks.Select(t => t.Id));
    }

    [Fact]
    public void BuildReadModel_RegisterAllScopeIncludesClosedAndAllowsClosedStatusFilter()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 42, Name = "Register Sprint", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(7) };
        var tasks = new[]
        {
            NewTask(34, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(1)),
            NewTask(35, sprint.Id, ActionTaskStatuses.Closed, today.AddDays(1))
        };
        var service = CreateQueryService();

        // SECTION: Act
        var model = service.BuildReadModel(
            tasks,
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, true, false, sprint.Id, new[] { sprint }, ActionTaskStatuses.Closed, null, null, null, null, null, null, TaskScope: ActionTaskRegisterScopes.All),
            new Dictionary<string, string> { ["assignee"] = "Assignee" },
            new Dictionary<int, DateTime?>());

        // SECTION: Assert
        Assert.Equal(new[] { 35 }, model.TaskListTasks.Select(t => t.Id));
    }

    [Fact]
    public void BuildReadModel_ReportBacklogSprintFilter_RendersBacklogAgeingOnly()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 31, Name = "Sprint Scope", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(7) };
        var tasks = new[]
        {
            NewTask(21, null, ActionTaskStatuses.Backlog, today.AddDays(2), "Normal", string.Empty, today.AddDays(-5)),
            NewTask(22, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(2), "Normal", "assignee", today.AddDays(-12)),
            NewTask(23, null, ActionTaskStatuses.Closed, today.AddDays(2), "Normal", "assignee", today.AddDays(-20)),
            NewTask(24, null, ActionTaskStatuses.Assigned, today.AddDays(2), "Normal", "assignee", today.AddDays(-20))
        };
        var service = CreateQueryService();

        // SECTION: Act
        var model = service.BuildReadModel(
            tasks,
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, false, false, sprint.Id, new[] { sprint }, null, null, null, null, null, null, null, ReportBucket: "Backlog"),
            new Dictionary<string, string> { ["assignee"] = "Assignee" },
            new Dictionary<int, DateTime?>());

        // SECTION: Assert
        Assert.Equal(1, model.Reports.FilteredTaskCount);
        Assert.DoesNotContain(model.Reports.StatusCounts, item => string.Equals(item.Name, ActionTaskStatuses.Closed, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, model.Reports.BacklogAgeingBuckets.Single(x => x.Name == "4-7 days in backlog").Count);
        Assert.Equal(0, model.Reports.BacklogAgeingBuckets.Single(x => x.Name == "15+ days in backlog").Count);
        Assert.Empty(model.Reports.OutsideSprintWorkloadCounts);
        Assert.All(model.Reports.CarryForwardBySprint, item => Assert.Equal(0, item.Count));
    }


    [Fact]
    public void BuildReadModel_ReportsResponsibleWorkload_UsesAssignedBucketsAndSortsByRisk()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 61, Name = "Execution Sprint", Status = ActionSprintStatus.Active, StartDate = today.AddDays(-2), EndDate = today.AddDays(5) };
        var tasks = new[]
        {
            NewTask(61, null, ActionTaskStatuses.Backlog, today.AddDays(3), assignedToUserId: string.Empty, assignedOn: today.AddDays(-1)),
            NewTask(62, null, ActionTaskStatuses.Assigned, today.AddDays(-1), assignedToUserId: "one"),
            NewTask(63, null, ActionTaskStatuses.InProgress, today.AddDays(2), assignedToUserId: "one"),
            NewTask(64, sprint.Id, ActionTaskStatuses.Blocked, today.AddDays(-2), assignedToUserId: "two"),
            NewTask(65, sprint.Id, ActionTaskStatuses.Submitted, today.AddDays(-3), assignedToUserId: "two"),
            NewTask(66, null, ActionTaskStatuses.Closed, today.AddDays(-4), assignedToUserId: "one")
        };
        var service = CreateQueryService();

        // SECTION: Act
        var model = service.BuildReadModel(
            tasks,
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, false, false, sprint.Id, new[] { sprint }, null, null, null, null, null, null, null),
            new Dictionary<string, string> { ["one"] = "One", ["two"] = "Two" },
            new Dictionary<int, DateTime?>());

        // SECTION: Assert
        Assert.Equal(new[] { "Backlog", "Outside Sprint", "Sprint", "Closed" }, model.Reports.BucketDistribution.Select(x => x.Name).ToArray());
        Assert.Equal(5, model.Reports.WorkloadSummary.OpenTasks);
        Assert.Equal(2, model.Reports.WorkloadSummary.Overdue);
        Assert.Equal(1, model.Reports.WorkloadSummary.PendingClosure);
        Assert.Equal(1, model.Reports.WorkloadSummary.BacklogItems);
        Assert.Equal(new[] { "One", "Two" }, model.Reports.ResponsiblePersonWorkloads.Select(x => x.ResponsiblePerson).ToArray());
        Assert.All(model.Reports.ResponsiblePersonWorkloads, item => Assert.Equal(2, item.Open));
        Assert.DoesNotContain(model.Reports.ResponsiblePersonWorkloads, item => item.ResponsiblePerson == "Unassigned");
        Assert.Equal(1, model.Reports.AssignedTaskAgeingBuckets.Single(x => x.Name == "0-3 days assigned").Count);
        Assert.Equal(1, model.Reports.BacklogAgeingBuckets.Single(x => x.Name == "0-3 days in backlog").Count);
        Assert.Equal(0, model.Reports.OverdueAgeingBuckets.Single(x => x.Name == "8+ days overdue").Count);
    }

    [Fact]
    public void BuildReadModel_ReportsInvalidStates_RenderOnlyWhenPresent()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 71, Name = "Invalid Sprint", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(7) };
        var validTasks = new[]
        {
            NewTask(71, null, ActionTaskStatuses.Backlog, today.AddDays(2), assignedToUserId: string.Empty),
            NewTask(72, null, ActionTaskStatuses.Assigned, today.AddDays(2), assignedToUserId: "assignee")
        };
        var invalidTasks = validTasks.Concat(new[]
        {
            NewTask(73, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(2), assignedToUserId: string.Empty)
        }).ToArray();
        var service = CreateQueryService();
        var request = new ActionTaskQueryService.ActionTaskQueryRequest("user", false, false, false, sprint.Id, new[] { sprint }, null, null, null, null, null, null, null);

        // SECTION: Act
        var validModel = service.BuildReadModel(validTasks, request, new Dictionary<string, string> { ["assignee"] = "Assignee" }, new Dictionary<int, DateTime?>());
        var invalidModel = service.BuildReadModel(invalidTasks, request, new Dictionary<string, string> { ["assignee"] = "Assignee" }, new Dictionary<int, DateTime?>());

        // SECTION: Assert
        Assert.DoesNotContain(validModel.Reports.BucketDistribution, item => item.Name == "Invalid State");
        Assert.Empty(validModel.Reports.InvalidStateRows);
        Assert.Contains(invalidModel.Reports.BucketDistribution, item => item.Name == "Invalid State" && item.Count == 1);
        var invalidRow = Assert.Single(invalidModel.Reports.InvalidStateRows);
        Assert.Contains("SprintId exists", invalidRow.Issue, StringComparison.Ordinal);
        Assert.Contains("Assign responsible person", invalidRow.SuggestedCorrection, StringComparison.Ordinal);
    }


    [Fact]
    public void BuildReadModel_RegisterBucketFilter_ReturnsMatchingBuckets()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 41, Name = "Sprint", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(7) };
        var tasks = new[]
        {
            NewTask(31, null, ActionTaskStatuses.Backlog, today.AddDays(2), assignedToUserId: string.Empty),
            NewTask(32, null, ActionTaskStatuses.Assigned, today.AddDays(2), assignedToUserId: "assignee"),
            NewTask(33, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(2), assignedToUserId: "assignee"),
            NewTask(34, null, ActionTaskStatuses.Closed, today.AddDays(2), assignedToUserId: "assignee"),
            NewTask(35, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(2), assignedToUserId: string.Empty)
        };
        var service = CreateQueryService();

        // SECTION: Act
        var backlog = BuildWithBucket(service, tasks, sprint, "Backlog");
        var outside = BuildWithBucket(service, tasks, sprint, "OutsideSprint");
        var sprintItems = BuildWithBucket(service, tasks, sprint, "Sprint");
        var closed = BuildWithBucket(service, tasks, sprint, "Closed");
        var invalid = BuildWithBucket(service, tasks, sprint, "Invalid");

        // SECTION: Assert
        Assert.Equal(new[] { 31 }, backlog.TaskListTasks.Select(t => t.Id));
        Assert.Equal(new[] { 32 }, outside.TaskListTasks.Select(t => t.Id));
        Assert.Equal(new[] { 33 }, sprintItems.TaskListTasks.Select(t => t.Id));
        Assert.Equal(new[] { 34 }, closed.TaskListTasks.Select(t => t.Id));
        Assert.Equal(new[] { 35 }, invalid.TaskListTasks.Select(t => t.Id));
    }

    [Fact]
    public void BuildReadModel_RegisterSearch_MatchesTitleAndAtNumber()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 51, Name = "Sprint", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(7) };
        var tasks = new[]
        {
            NewTask(51, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(3), assignedToUserId: "assignee", title: "Monthly readiness review"),
            NewTask(52, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(1), assignedToUserId: "assignee", title: "Fleet compliance check"),
            NewTask(53, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(2), assignedToUserId: "assignee", title: "Inventory audit"),
            NewTask(152, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(4), assignedToUserId: "assignee", title: "Engine room inspection"),
            NewTask(520, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(5), assignedToUserId: "assignee", title: "AT-52 reference notes")
        };
        var service = CreateQueryService();

        // SECTION: Act
        var titleMatch = BuildWithSearch(service, tasks, sprint, "readiness");
        var atNumberMatch = BuildWithSearch(service, tasks, sprint, "AT-52");
        var numericMatch = BuildWithSearch(service, tasks, sprint, "52");

        // SECTION: Assert
        Assert.Equal(new[] { 51 }, titleMatch.TaskListTasks.Select(t => t.Id));
        Assert.Equal(new[] { 52 }, atNumberMatch.TaskListTasks.Select(t => t.Id));
        Assert.Equal(new[] { 52 }, numericMatch.TaskListTasks.Select(t => t.Id));
    }


    [Fact]
    public void BuildReadModel_SelectedSprintIncludesInvalidSprintLinkedTasksForClosureReview()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 81, Name = "Active Sprint", Status = ActionSprintStatus.Active, StartDate = today, EndDate = today.AddDays(7) };
        var tasks = new[]
        {
            NewTask(81, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(1), assignedToUserId: "assignee"),
            NewTask(82, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(2), assignedToUserId: string.Empty)
        };
        var service = CreateQueryService();

        // SECTION: Act
        var model = service.BuildReadModel(
            tasks,
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, false, false, sprint.Id, new[] { sprint }, null, null, null, null, null, null, null),
            new Dictionary<string, string> { ["assignee"] = "Assignee" },
            new Dictionary<int, DateTime?>());

        // SECTION: Assert
        Assert.Equal(new[] { 81, 82 }, model.SprintReadModel.SelectedSprintTasks.Select(t => t.Id));
        Assert.Equal(new[] { 81, 82 }, model.SprintReadModel.ClosureReview.UnfinishedTasks.Select(t => t.Id));
        Assert.Equal(1, model.ActiveSprintMetrics.InvalidStateTasks);
    }

    [Fact]
    public void BuildReadModel_SprintPerformanceCountsOnlyTasksClosedAfterDueDateAsLate()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 91, Name = "Closed Sprint", Status = ActionSprintStatus.Closed, StartDate = today.AddDays(-14), EndDate = today.AddDays(-1), ClosedAtUtc = today.AddDays(5) };
        var closedOnTime = NewTask(91, sprint.Id, ActionTaskStatuses.Closed, today.AddDays(-5));
        closedOnTime.ClosedOn = today.AddDays(-5);
        var closedLate = NewTask(92, sprint.Id, ActionTaskStatuses.Closed, today.AddDays(-6));
        closedLate.ClosedOn = today.AddDays(-4);
        var service = CreateQueryService();

        // SECTION: Act
        var model = service.BuildReadModel(
            new[] { closedOnTime, closedLate },
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, false, false, sprint.Id, new[] { sprint }, null, null, null, null, null, null, null),
            new Dictionary<string, string> { ["assignee"] = "Assignee" },
            new Dictionary<int, DateTime?>());

        // SECTION: Assert
        var row = model.Reports.SprintPerformanceRows.Single();
        Assert.Equal(1, row.ClosedLate);
        Assert.Equal(0, row.Unfinished);
    }

    [Fact]
    public void BuildReadModel_SprintPerformanceSeparatesActiveOverdueFromClosedLate()
    {
        // SECTION: Arrange
        var today = DateTime.UtcNow.Date;
        var sprint = new ActionSprint { Id = 92, Name = "Active Sprint", Status = ActionSprintStatus.Active, StartDate = today.AddDays(-7), EndDate = today.AddDays(7) };
        var overdueOpen = NewTask(93, sprint.Id, ActionTaskStatuses.Assigned, today.AddDays(-2));
        var futureOpen = NewTask(94, sprint.Id, ActionTaskStatuses.InProgress, today.AddDays(2));
        var service = CreateQueryService();

        // SECTION: Act
        var model = service.BuildReadModel(
            new[] { overdueOpen, futureOpen },
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, false, false, sprint.Id, new[] { sprint }, null, null, null, null, null, null, null),
            new Dictionary<string, string> { ["assignee"] = "Assignee" },
            new Dictionary<int, DateTime?>());

        // SECTION: Assert
        var row = model.Reports.SprintPerformanceRows.Single();
        Assert.Equal(1, row.OverdueNow);
        Assert.Equal(2, row.Unfinished);
        Assert.Equal(0, row.ClosedLate);
    }

    // SECTION: Test query service helper
    [Fact]
    public void ResolveBucket_WhenSprintExistsWithoutResponsiblePerson_ReturnsInvalid()
    {
        // SECTION: Arrange
        var task = NewTask(99, 7, ActionTaskStatuses.Assigned, DateTime.UtcNow.Date, assignedToUserId: string.Empty);

        // SECTION: Act
        var bucket = ActionTaskCategorization.ResolveBucket(task);

        // SECTION: Assert
        Assert.Equal(ActionTaskBucket.Invalid, bucket);
    }


    private static ActionTaskQueryService CreateQueryService()
    {
        var clock = new SystemActionTrackerClock();
        return new ActionTaskQueryService(clock, new ActionTaskReportBuilder(clock));
    }


    private static ActionTaskQueryService.ActionTaskReadModel BuildWithBucket(ActionTaskQueryService service, IReadOnlyList<ActionTaskItem> tasks, ActionSprint sprint, string bucket)
        => service.BuildReadModel(
            tasks,
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, true, false, sprint.Id, new[] { sprint }, null, null, null, null, null, null, null, FilterBucket: bucket, TaskScope: ActionTaskRegisterScopes.All),
            new Dictionary<string, string> { ["assignee"] = "Assignee" },
            new Dictionary<int, DateTime?>());

    private static ActionTaskQueryService.ActionTaskReadModel BuildWithSearch(ActionTaskQueryService service, IReadOnlyList<ActionTaskItem> tasks, ActionSprint sprint, string search)
        => service.BuildReadModel(
            tasks,
            new ActionTaskQueryService.ActionTaskQueryRequest("user", false, true, false, sprint.Id, new[] { sprint }, null, null, null, null, search, null, null),
            new Dictionary<string, string> { ["assignee"] = "Assignee" },
            new Dictionary<int, DateTime?>());

    // SECTION: Test data helper
    private static ActionTaskItem NewTask(int id, int? sprintId, string status, DateTime dueDate, string priority = "Normal", string assignedToUserId = "assignee", DateTime? assignedOn = null, string? title = null)
        => new()
        {
            Id = id,
            Title = title ?? $"Task {id}",
            Description = "Task",
            CreatedByUserId = "creator",
            AssignedToUserId = assignedToUserId,
            CreatedByRole = "HoD",
            AssignedToRole = string.IsNullOrWhiteSpace(assignedToUserId) ? string.Empty : "TA",
            AssignedOn = assignedOn ?? dueDate.AddDays(-2),
            DueDate = dueDate,
            Priority = priority,
            Status = status,
            SprintId = sprintId,
            ClosedOn = status == ActionTaskStatuses.Closed ? dueDate : null,
            SubmittedOn = status == ActionTaskStatuses.Submitted ? dueDate.AddDays(-1) : null
        };
}
