using ProjectManagement.Models;
using ProjectManagement.Services.Workspace;
using ProjectManagement.ViewModels.Workspace;
using Xunit;

namespace ProjectManagement.Tests.Workspace;

public sealed class CommandDeskBuilderTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    // SECTION: What moved forward
    [Theory]
    [InlineData(15, 1)]
    [InlineData(30, 2)]
    public void MovedForward_CountsOnlyEventsInsideTheChosenWindow(int window, int expectedStages)
    {
        var desk = Build(window: window, progress: new[]
        {
            new CommandDeskProgressEvent(CommandDeskProgressKind.StageCompleted, Today.AddDays(-3), Text: "A completed AON"),
            new CommandDeskProgressEvent(CommandDeskProgressKind.StageCompleted, Today.AddDays(-20), Text: "B completed DEVP"),
            new CommandDeskProgressEvent(CommandDeskProgressKind.StageCompleted, Today.AddDays(-40), Text: "C completed TEC"),
            new CommandDeskProgressEvent(CommandDeskProgressKind.StageCompleted, Today.AddDays(2), Text: "Future")
        });

        var count = Assert.Single(desk.MovedForward);
        Assert.Equal(expectedStages, count.Count);
        Assert.Equal(expectedStages == 1 ? "stage completed" : "stages completed", count.Label);
        Assert.Equal(expectedStages, desk.Wins.Count);
        Assert.Equal("A completed AON", desk.Wins[0].Text);
    }

    [Fact]
    public void MovedForward_SumsProliferatedUnits_AndSkipsKindsWithNothing()
    {
        var desk = Build(progress: new[]
        {
            new CommandDeskProgressEvent(CommandDeskProgressKind.UnitsProliferated, Today.AddDays(-1), 40),
            new CommandDeskProgressEvent(CommandDeskProgressKind.UnitsProliferated, Today.AddDays(-9), 80),
            new CommandDeskProgressEvent(CommandDeskProgressKind.FfcDelivered, Today)
        });

        Assert.Collection(desk.MovedForward,
            count => Assert.Equal((120, "units proliferated"), (count.Count, count.Label)),
            count => Assert.Equal((1, "FFC delivery"), (count.Count, count.Label)));
    }

    [Fact]
    public void MovedForward_CountsVisits_PeopleTrained_AndActivities()
    {
        var desk = Build(progress: new[]
        {
            new CommandDeskProgressEvent(CommandDeskProgressKind.Visit, Today.AddDays(-3)),
            new CommandDeskProgressEvent(CommandDeskProgressKind.Visit, Today.AddDays(-12)),
            new CommandDeskProgressEvent(CommandDeskProgressKind.PeopleTrained, Today.AddDays(-5), 40),
            new CommandDeskProgressEvent(CommandDeskProgressKind.PeopleTrained, Today.AddDays(-20), 25),
            new CommandDeskProgressEvent(CommandDeskProgressKind.Activity, Today.AddDays(-1)),
            new CommandDeskProgressEvent(CommandDeskProgressKind.Activity, Today.AddDays(-40)) // outside the window
        });

        Assert.Collection(desk.MovedForward,
            count => Assert.Equal((2, "visits", "/ProjectOfficeReports/Visits"), (count.Count, count.Label, count.Url)),
            count => Assert.Equal((65, "people trained", "/ProjectOfficeReports/Training"), (count.Count, count.Label, count.Url)),
            count => Assert.Equal((1, "activity", "/Activities"), (count.Count, count.Label, count.Url)));
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData(7, 30)]
    [InlineData(15, 15)]
    public void Window_IsEither15Or30Days(int? requested, int expected)
        => Assert.Equal(expected, CommandDeskBuilder.NormaliseWindow(requested));

    // SECTION: Waiting on you
    [Fact]
    public void WaitingOnYou_ListsOldestFirst()
    {
        var desk = Build(decisions: new[]
        {
            Decision("SPRINT", hoursAgo: 20),
            Decision("Draft QR for SPRINT", hoursAgo: 75)
        });

        Assert.Equal(new[] { "Draft QR for SPRINT", "SPRINT" }, desk.WaitingOnYou.Select(item => item.Title));
        Assert.Equal("3 days", desk.WaitingOnYou[0].AgeLabel);
        Assert.Equal(2, desk.WaitingOnYouTotal);
    }

    // SECTION: PDC watch
    [Fact]
    public void PdcWatch_PutsOverdueFirst_KeepsTheNext30Days_AndNotesPlansWithHod()
    {
        var desk = Build(projects: new[]
        {
            Project(1, "Later", pdcInDays: 31),
            Project(2, "Soon", pdcInDays: 7),
            Project(3, "Late", pdcInDays: -19, planWithHod: true),
            Project(4, "Today", pdcInDays: 0),
            Project(5, "No PDC", pdcInDays: null)
        });

        Assert.Equal(new[] { "Late", "Today", "Soon" }, desk.PdcWatch.Select(item => item.ProjectName));
        Assert.Equal("overdue 19 days", desk.PdcWatch[0].WhenLabel);
        Assert.Equal("Revised plan with HoD", desk.PdcWatch[0].Note);
        Assert.Equal("due today", desk.PdcWatch[1].WhenLabel);
        Assert.Equal(1, desk.PdcOverdueCount);
        Assert.Equal(2, desk.PdcDueSoonCount);
    }

    // SECTION: Directions
    [Fact]
    public void Directions_ShowOnlyUnanswered_OldestFirst_AndHighlightOlderThanAWeek()
    {
        var desk = Build(directions: new[]
        {
            Direction("Answered", daysAgo: 20, answered: true),
            Direction("Fresh", daysAgo: 7),
            Direction("Old", daysAgo: 14)
        });

        Assert.Equal(new[] { "Old", "Fresh" }, desk.Directions.Select(item => item.Title));
        Assert.True(desk.Directions[0].IsOlderThanWeek);
        Assert.Equal("14 days, no reply", desk.Directions[0].AgeLabel);
        Assert.False(desk.Directions[1].IsOlderThanWeek);
        Assert.Equal(1, desk.DirectionsOlderThanWeekCount);
    }

    // SECTION: Tasks
    [Fact]
    public void Tasks_PutBlockedAndOverdueFirst_ThenDueWithinAWeek_AndSkipSubmittedClosedOrBacklog()
    {
        var desk = Build(tasks: new[]
        {
            Task(1, "Due tomorrow", ActionTaskStatuses.InProgress, dueInDays: 1, priority: "High"),
            Task(2, "Blocked", ActionTaskStatuses.Blocked, dueInDays: -4, reason: " Vendor has not replied. "),
            Task(3, "Far off", ActionTaskStatuses.Assigned, dueInDays: 20),
            Task(4, "Submitted", ActionTaskStatuses.Submitted, dueInDays: -2),
            Task(5, "Closed", ActionTaskStatuses.Closed, dueInDays: -2),
            Task(7, "Backlog", ActionTaskStatuses.Backlog, dueInDays: -30),
            Task(6, "Overdue", ActionTaskStatuses.InProgress, dueInDays: -1)
        });

        Assert.Equal(new[] { "Blocked", "Overdue", "Due tomorrow" }, desk.Tasks.Select(item => item.Title));
        Assert.Equal("blocked · overdue 4 days", desk.Tasks[0].StateLabel);
        Assert.Equal("Vendor has not replied.", desk.Tasks[0].BlockedReason);
        Assert.True(desk.Tasks[1].IsDanger);
        Assert.Equal("due tomorrow", desk.Tasks[2].StateLabel);
        Assert.EndsWith("· High", desk.Tasks[2].Who);
        Assert.Equal((1, 1, 1), (desk.TasksBlockedCount, desk.TasksOverdueCount, desk.TasksDueSoonCount));
    }

    // SECTION: Needs attention
    [Fact]
    public void Attention_GroupsByOfficer()
    {
        var desk = Build(projects: new[]
        {
            Project(1, "A", pdcInDays: null, officer: "srinath", lastUpdateDaysAgo: 2),
            Project(2, "B", pdcInDays: null, officer: "srinath", lastUpdateDaysAgo: 10),
            Project(3, "C", pdcInDays: 40, officer: "arpan", lastUpdateDaysAgo: null),
            Project(4, "D", pdcInDays: 40, officer: "arpan", lastUpdateDaysAgo: 9)
        });

        Assert.Collection(desk.Attention,
            reason =>
            {
                Assert.Equal("no-pdc", reason.Key);
                Assert.Equal(2, reason.Count);
                Assert.Equal("srinath", Assert.Single(reason.ByOfficer).OfficerName);
                Assert.Equal("/Workspace/Conference/srinath", reason.ByOfficer[0].Url);
            },
            reason =>
            {
                Assert.Equal("no-update", reason.Key);
                Assert.Equal(2, reason.Count);
                Assert.Equal(new[] { "arpan", "srinath" }, reason.ByOfficer.Select(officer => officer.OfficerName));
            });
    }

    // SECTION: What's gone quiet
    [Fact]
    public void Quiet_ListsRegistersPastTheirFrequency_LongestSilenceFirst()
    {
        var desk = Build(registers: new[]
        {
            new CommandDeskRegisterInput("Visits", Today.AddDays(-15), 15, "/v"),
            new CommandDeskRegisterInput("Misc activities", Today.AddDays(-16), 15, "/a"),
            new CommandDeskRegisterInput("Training", Today.AddDays(-151), 30, "/t"),
            new CommandDeskRegisterInput("Social media", null, 30, "/s")
        });

        Assert.Equal(new[] { "Social media", "Training", "Misc activities" }, desk.QuietRegisters.Select(item => item.Label));
        Assert.Equal(151, desk.QuietRegisters[1].DaysSince);
        Assert.Equal(1, desk.CurrentRegisterCount);
    }

    [Fact]
    public void EmptyInput_HasNothingLagging()
    {
        var desk = Build();

        Assert.True(desk.IsLaggingEmpty);
        Assert.Empty(desk.MovedForward);
        Assert.Empty(desk.WaitingOnYou);
    }

    // SECTION: Acting from the desk
    [Fact]
    public void PdcWatch_ToAnApprover_SaysTheRevisedPlanWaitsOnThem_AndLinksToItsReview()
    {
        var desk = Build(projects: new[]
        {
            Project(1, "Mine to decide", pdcInDays: -20, planWithHod: true, planReviewUrl: "/Approvals/Pending/PlanApproval/7"),
            Project(2, "Someone else's", pdcInDays: -10, planWithHod: true)
        });

        var mine = desk.PdcWatch.Single(item => item.ProjectName == "Mine to decide");
        Assert.Equal("Revised plan · waiting on you", mine.Note);
        Assert.Equal("/Approvals/Pending/PlanApproval/7", mine.NoteUrl);
        var other = desk.PdcWatch.Single(item => item.ProjectName == "Someone else's");
        Assert.Equal("Revised plan with HoD", other.Note);
        Assert.Null(other.NoteUrl);
    }

    [Fact]
    public void NoPdc_DoesNotCountAProjectWhosePlanIsWaitingForApproval()
    {
        var desk = Build(projects: new[]
        {
            Project(1, "UIFOS", pdcInDays: null, officer: "srinath"),
            Project(2, "SPRINT", pdcInDays: null, planWithHod: true, officer: "srinath"),
            Project(3, "AFS", pdcInDays: null, planWithHod: true, officer: "srinath")
        });

        var noPdc = desk.Attention.Single(reason => reason.Key == "no-pdc");
        Assert.Equal(1, noPdc.Count);
        Assert.Equal("UIFOS", Assert.Single(noPdc.ByOfficer[0].Projects!).Name);
    }

    [Fact]
    public void ABlockedTaskTheViewerAssigned_AlsoWaitsOnThem_WithItsReason()
    {
        var since = new DateTimeOffset(2026, 10, 19, 5, 0, 0, TimeSpan.Zero);
        var desk = Build(tasks: new[]
        {
            Task(1, "Close vendor clarifications", ActionTaskStatuses.Blocked, dueInDays: -4,
                reason: "Vendor has not replied; escalated to HoD.", assignerUserId: "hod", blockedSinceUtc: since),
            Task(2, "Someone else's blocked task", ActionTaskStatuses.Blocked, dueInDays: 3, reason: "Waiting.")
        });

        var waiting = Assert.Single(desk.WaitingOnYou);
        Assert.Equal("Close vendor clarifications", waiting.Title);
        Assert.Equal("Task blocked: “Vendor has not replied; escalated to HoD.”", waiting.TypeLabel);
        Assert.Equal("Open task", waiting.ActionLabel);
        Assert.Equal("/ActionTasks/Details/1", waiting.Url);
        Assert.Equal("3 days", waiting.AgeLabel);
        Assert.Equal(2, desk.Tasks.Count); // still under Tasks, which everyone sees
        Assert.Empty(Build(tasks: new[] { Task(1, "x", ActionTaskStatuses.Blocked, -1, assignerUserId: "hod") }, viewer: null).WaitingOnYou);
    }

    [Fact]
    public void Attention_ListsTheProjectsBehindEachOfficersCount_AndOffersAMessage()
    {
        var desk = Build(projects: new[]
        {
            Project(1, "Beta", pdcInDays: null, officer: "srinath"),
            Project(2, "Alpha", pdcInDays: null, officer: "srinath"),
            Project(3, "Own project", pdcInDays: null, officer: "hod")
        });

        var noPdc = desk.Attention.Single(reason => reason.Key == "no-pdc");
        var srinath = noPdc.ByOfficer.Single(officer => officer.OfficerName == "srinath");
        Assert.Equal(new[] { "Alpha", "Beta" }, srinath.Projects!.Select(project => project.Name));
        Assert.Equal("/Projects/Overview/2", srinath.Projects![0].Url);
        Assert.Equal("/Chat?with=srinath", srinath.MessageUrl);
        Assert.Null(noPdc.ByOfficer.Single(officer => officer.OfficerName == "hod").MessageUrl); // not to yourself
    }

    [Fact]
    public void Directions_OfferAMessageToTheOfficer()
    {
        var desk = Build(directions: new[] { Direction("Old", daysAgo: 14), Direction("Unknown officer", daysAgo: 3, officerUserId: null) });

        Assert.Equal("/Chat?with=srinath-id", desk.Directions.Single(item => item.Title == "Old").MessageUrl);
        Assert.Null(desk.Directions.Single(item => item.Title == "Unknown officer").MessageUrl);
    }

    // SECTION: Builders
    private static CommandDeskVm Build(
        int window = 30,
        IReadOnlyList<CommandDeskProgressEvent>? progress = null,
        IReadOnlyList<CommandDeskDecisionInput>? decisions = null,
        IReadOnlyList<CommandDeskProjectInput>? projects = null,
        IReadOnlyList<CommandDeskDirectionInput>? directions = null,
        IReadOnlyList<CommandDeskTaskInput>? tasks = null,
        IReadOnlyList<CommandDeskRegisterInput>? registers = null,
        string? viewer = "hod")
        => CommandDeskBuilder.Build(new CommandDeskInput(
            Today,
            window,
            progress ?? Array.Empty<CommandDeskProgressEvent>(),
            decisions ?? Array.Empty<CommandDeskDecisionInput>(),
            CanOpenDecisionCentre: true,
            projects ?? Array.Empty<CommandDeskProjectInput>(),
            directions ?? Array.Empty<CommandDeskDirectionInput>(),
            tasks ?? Array.Empty<CommandDeskTaskInput>(),
            registers ?? Array.Empty<CommandDeskRegisterInput>(),
            viewer));

    // Noon IST on Today, minus the given hours.
    private static CommandDeskDecisionInput Decision(string title, int hoursAgo)
        => new(title, "Timeline plan", "K Srinath", new DateTimeOffset(2026, 10, 22, 6, 30, 0, TimeSpan.Zero).AddHours(-hoursAgo), "Review", "/x");

    private static CommandDeskProjectInput Project(
        int id,
        string name,
        int? pdcInDays,
        bool planWithHod = false,
        string officer = "officer",
        int? lastUpdateDaysAgo = 1,
        string? planReviewUrl = null)
        => new(id, name, officer, officer, "DEVP",
            pdcInDays.HasValue ? Today.AddDays(pdcInDays.Value) : null,
            planWithHod,
            lastUpdateDaysAgo.HasValue ? Today.AddDays(-lastUpdateDaysAgo.Value) : null,
            planReviewUrl);

    private static CommandDeskDirectionInput Direction(string title, int daysAgo, bool answered = false, string? officerUserId = "srinath-id")
        => new(ConferenceItemKind.Project, title, "K Srinath", "Set the PDC.", "Brig Ashish Johar", Today.AddDays(-daysAgo), answered, "/c", officerUserId);

    private static CommandDeskTaskInput Task(
        int id,
        string title,
        string status,
        int dueInDays,
        string priority = "Normal",
        string? reason = null,
        string? assignerUserId = "someone-else",
        DateTimeOffset? blockedSinceUtc = null)
        => new(id, title, "K Srinath", "Hari Om Ahlawat", priority, status, Today.AddDays(dueInDays), reason, assignerUserId, blockedSinceUtc);
}
