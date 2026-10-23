using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services.Approvals;
using ProjectManagement.Services.Workspace;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using ProjectManagement.ViewModels.Workspace;
using Xunit;

namespace ProjectManagement.Tests.Workspace;

public sealed class CommandDeskServiceTests
{
    private const string ComdtId = "comdt-1";
    private const string HodId = "hod-1";
    private const string OfficerId = "po-1";
    private static readonly DateOnly Today = new(2026, 10, 22);

    [Fact]
    public async Task Comdt_SeesTasksSubmittedToThem_ButNotTheDecisionCentreQueue()
    {
        await using var db = NewDb();
        db.ActionTasks.AddRange(
            NewTask(1, "Draft QR for SPRINT", ActionTaskStatuses.Submitted, createdBy: ComdtId, dueInDays: 2),
            NewTask(2, "Assigned by the HoD", ActionTaskStatuses.Submitted, createdBy: HodId, dueInDays: 2));
        await db.SaveChangesAsync();

        var desk = await NewService(db).GetAsync(User(RoleNames.Comdt), ComdtId, null, CancellationToken.None);

        var decision = Assert.Single(desk.WaitingOnYou);
        Assert.Equal("Draft QR for SPRINT", decision.Title);
        Assert.Equal("Close or return", decision.ActionLabel);
        Assert.Equal("/ActionTasks/Details/1", decision.Url);
        Assert.False(desk.CanOpenDecisionCentre);
    }

    [Fact]
    public async Task SubmittedTask_WaitsOnWhoeverReassignedIt_NotItsCreator()
    {
        await using var db = NewDb();
        db.ActionTasks.Add(NewTask(1, "Reassigned by the HoD", ActionTaskStatuses.Submitted, createdBy: ComdtId, dueInDays: 2));
        db.ActionTaskAuditLogs.Add(new ActionTaskAuditLog
        {
            TaskId = 1,
            ActionType = "TaskReassigned",
            PerformedByUserId = HodId,
            PerformedByRole = RoleNames.HoD,
            PerformedAt = new DateTime(2026, 10, 10, 5, 0, 0, DateTimeKind.Utc),
            OldValue = "someone-else",
            NewValue = OfficerId
        });
        await db.SaveChangesAsync();

        var comdtDesk = await NewService(db).GetAsync(User(RoleNames.Comdt), ComdtId, null, CancellationToken.None);
        var hodDesk = await NewService(db).GetAsync(User(RoleNames.HoD), HodId, null, CancellationToken.None);

        Assert.Empty(comdtDesk.WaitingOnYou);
        Assert.Equal("Reassigned by the HoD", Assert.Single(hodDesk.WaitingOnYou, item => item.ActionLabel == "Close or return").Title);
    }

    [Fact]
    public async Task Hod_SeesTheDecisionCentreQueue()
    {
        await using var db = NewDb();

        var desk = await NewService(db).GetAsync(User(RoleNames.HoD), HodId, 15, CancellationToken.None);

        var decision = Assert.Single(desk.WaitingOnYou);
        Assert.Equal("SPRINT", decision.Title);
        Assert.Equal("Timeline plan", decision.TypeLabel);
        Assert.Equal("/Approvals/Pending/PlanApproval/7?returnUrl=%2FWorkspace%2FCommandDesk", decision.Url);
        Assert.True(desk.CanOpenDecisionCentre);
        Assert.Equal(15, desk.WindowDays);
    }

    [Fact]
    public async Task NoPdc_IgnoresAPendingPlanThatIsNoLongerActionable()
    {
        await using var db = NewDb();
        // Project 50: an old pending version, then a newer one that was rejected. The old one is
        // no longer actionable (not in the Decision Centre), so the project still lacks a PDC.
        // Project 51: a plan genuinely waiting for approval, so it is not counted.
        db.PlanVersions.AddRange(
            new ProjectManagement.Models.Plans.PlanVersion { Id = 1, ProjectId = 50, VersionNo = 1, Status = ProjectManagement.Models.Plans.PlanVersionStatus.PendingApproval, CreatedByUserId = OfficerId },
            new ProjectManagement.Models.Plans.PlanVersion { Id = 2, ProjectId = 50, VersionNo = 2, Status = ProjectManagement.Models.Plans.PlanVersionStatus.Rejected, CreatedByUserId = OfficerId, RejectedOn = DateTimeOffset.UtcNow },
            new ProjectManagement.Models.Plans.PlanVersion { Id = 3, ProjectId = 51, VersionNo = 1, Status = ProjectManagement.Models.Plans.PlanVersionStatus.PendingApproval, CreatedByUserId = OfficerId });
        await db.SaveChangesAsync();
        var workload = new FakeWorkload(
            new CommandOfficerProjectVm(50, "Stale pending", "AON", "Acceptance of Necessity", "/Projects/Overview/50"),
            new CommandOfficerProjectVm(51, "Waiting on the HoD", "AON", "Acceptance of Necessity", "/Projects/Overview/51"));

        var desk = await NewService(db, workload: workload).GetAsync(User(RoleNames.Comdt), ComdtId, null, CancellationToken.None);

        var noPdc = desk.Attention.Single(reason => reason.Key == "no-pdc");
        Assert.Equal("Stale pending", Assert.Single(Assert.Single(noPdc.ByOfficer).Projects!).Name);
    }

    [Fact]
    public async Task Activities_CountOnlyOnceTheyHaveHappened()
    {
        await using var db = NewDb();
        // The clock is at noon IST on Today (06:30 UTC).
        var noonUtc = new DateTimeOffset(Today.Year, Today.Month, Today.Day, 6, 30, 0, TimeSpan.Zero);
        db.Activities.AddRange(
            new ProjectManagement.Models.Activities.Activity { Id = 1, Title = "This morning", ActivityTypeId = 4, CreatedByUserId = HodId, ScheduledStartUtc = noonUtc.AddHours(-3), CreatedAtUtc = noonUtc.AddHours(-2) },
            new ProjectManagement.Models.Activities.Activity { Id = 2, Title = "This evening", ActivityTypeId = 3, CreatedByUserId = HodId, ScheduledStartUtc = noonUtc.AddHours(5), CreatedAtUtc = noonUtc.AddHours(-1) },
            new ProjectManagement.Models.Activities.Activity { Id = 3, Title = "Deleted", ActivityTypeId = 4, CreatedByUserId = HodId, ScheduledStartUtc = noonUtc.AddDays(-2), IsDeleted = true });
        await db.SaveChangesAsync();

        var desk = await NewService(db).GetAsync(User(RoleNames.Comdt), ComdtId, null, CancellationToken.None);

        var activities = Assert.Single(desk.MovedForward, count => count.Label is "activity" or "activities");
        Assert.Equal((1, "activity"), (activities.Count, activities.Label));
    }

    [Fact]
    public async Task BlockedTask_CarriesTheLatestBlockingReason()
    {
        await using var db = NewDb();
        db.ActionTasks.Add(NewTask(3, "Close vendor clarifications", ActionTaskStatuses.Blocked, createdBy: HodId, dueInDays: -4));
        db.ActionTaskAuditLogs.AddRange(
            new ActionTaskAuditLog { TaskId = 3, ActionType = "StatusUpdated", NewValue = ActionTaskStatuses.Blocked, Remarks = "Old reason", PerformedAt = new DateTime(2026, 10, 1, 5, 0, 0, DateTimeKind.Utc), PerformedByUserId = OfficerId, PerformedByRole = "ProjectOfficer" },
            new ActionTaskAuditLog { TaskId = 3, ActionType = "StatusUpdated", NewValue = ActionTaskStatuses.Blocked, Remarks = "Vendor has not replied.", PerformedAt = new DateTime(2026, 10, 18, 5, 0, 0, DateTimeKind.Utc), PerformedByUserId = OfficerId, PerformedByRole = "ProjectOfficer" });
        await db.SaveChangesAsync();

        var desk = await NewService(db).GetAsync(User(RoleNames.Comdt), ComdtId, null, CancellationToken.None);

        var task = Assert.Single(desk.Tasks);
        Assert.Equal("Vendor has not replied.", task.BlockedReason);
        Assert.Equal("K Srinath · assigned by Hari Om Ahlawat", task.Who);
        Assert.Equal("blocked · overdue 4 days", task.StateLabel);
    }

    [Fact]
    public async Task Registers_FlagOnlyThoseSilentPastTheirFrequency()
    {
        await using var db = NewDb();
        db.Visits.Add(new Visit { Id = Guid.NewGuid(), VisitTypeId = Guid.NewGuid(), DateOfVisit = Today.AddDays(-3), VisitorName = "Delegation", CreatedByUserId = HodId });
        db.SocialMediaEvents.Add(new SocialMediaEvent { Id = Guid.NewGuid(), SocialMediaEventTypeId = Guid.NewGuid(), DateOfEvent = Today.AddDays(-45), Title = "Post", CreatedByUserId = HodId });
        await db.SaveChangesAsync();

        var desk = await NewService(db).GetAsync(User(RoleNames.Comdt), ComdtId, null, CancellationToken.None);

        Assert.DoesNotContain(desk.QuietRegisters, register => register.Label == "Visits");
        var social = Assert.Single(desk.QuietRegisters, register => register.Label == "Social media");
        Assert.Equal(45, social.DaysSince);
        Assert.Contains(desk.QuietRegisters, register => register.Label == "Training" && register.LastOn is null);
        Assert.Equal(1, desk.CurrentRegisterCount);
    }

    [Fact]
    public async Task Directions_AnsweredOnlyByARealOfficerReply()
    {
        await using var db = NewDb();
        var conference = new FakeConference(new OfficerConferenceVm
        {
            OfficerUserId = OfficerId,
            OfficerName = "K Srinath",
            Sections = new[]
            {
                new OfficerConferenceSectionVm
                {
                    Kind = ConferenceItemKind.Project,
                    Items = new[]
                    {
                        DirectedItem(1, "Placeholder only", new ConferenceProgressEntryVm { Label = "Project Officer", EmptyText = "Progress update awaited." }),
                        DirectedItem(2, "MCO only",
                            new ConferenceProgressEntryVm { Label = "Project Officer", EmptyText = "Progress update awaited." },
                            new ConferenceProgressEntryVm { Label = "MCO", Body = "Filed.", ActivityAtUtc = DateTime.UtcNow }),
                        DirectedItem(3, "Answered", new ConferenceProgressEntryVm { Label = "Project Officer", Body = "Done.", ActivityAtUtc = DateTime.UtcNow })
                    }
                }
            }
        });

        var desk = await NewService(db, conference).GetAsync(User(RoleNames.HoD), HodId, null, CancellationToken.None);

        Assert.Equal(new[] { "MCO only", "Placeholder only" }, desk.Directions.Select(item => item.Title));
        Assert.All(desk.Directions, item => Assert.Equal("K Srinath", item.OfficerName));
    }

    [Fact]
    public async Task TotCompletions_FollowTheTrackerEligibilityRule()
    {
        await using var db = NewDb();
        db.Projects.AddRange(
            new Project { Id = 20, Name = "Eligible", CreatedByUserId = HodId, LifecycleStatus = ProjectLifecycleStatus.Completed },
            new Project { Id = 21, Name = "Archived", CreatedByUserId = HodId, LifecycleStatus = ProjectLifecycleStatus.Completed, IsArchived = true },
            new Project { Id = 22, Name = "Trashed", CreatedByUserId = HodId, LifecycleStatus = ProjectLifecycleStatus.Completed, IsDeleted = true });
        foreach (var projectId in new[] { 20, 21, 22 })
        {
            db.ProjectTots.Add(new ProjectTot { ProjectId = projectId, Status = ProjectTotStatus.Completed, CompletedOn = Today.AddDays(-5) });
        }
        await db.SaveChangesAsync();

        var desk = await NewService(db).GetAsync(User(RoleNames.Comdt), ComdtId, null, CancellationToken.None);

        var tot = Assert.Single(desk.MovedForward, count => count.Label.StartsWith("ToT", StringComparison.Ordinal));
        Assert.Equal(1, tot.Count);
        Assert.Contains(desk.Wins, win => win.Text == "ToT completed: Eligible");
        Assert.DoesNotContain(desk.Wins, win => win.Text.Contains("Archived", StringComparison.Ordinal) || win.Text.Contains("Trashed", StringComparison.Ordinal));
    }

    // SECTION: Helpers
    private static ApplicationDbContext NewDb()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Users.AddRange(
            new ApplicationUser { Id = ComdtId, UserName = "comdt", FullName = "Ashish Johar" },
            new ApplicationUser { Id = HodId, UserName = "hod", FullName = "Hari Om Ahlawat" },
            new ApplicationUser { Id = OfficerId, UserName = "srinath", FullName = "K Srinath" });
        db.SaveChanges();
        return db;
    }

    private static CommandDeskService NewService(ApplicationDbContext db, FakeConference? conference = null, FakeWorkload? workload = null)
        => new(
            db,
            FakeClock.ForIstDate(Today.Year, Today.Month, Today.Day, 12),
            workload ?? new FakeWorkload(),
            conference ?? new FakeConference(),
            new FakeApprovals());

    private static ClaimsPrincipal User(string role)
        => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test"));

    private static ActionTaskItem NewTask(int id, string title, string status, string createdBy, int dueInDays)
        => new()
        {
            Id = id,
            Title = title,
            Description = title,
            Status = status,
            CreatedByUserId = createdBy,
            CreatedByRole = RoleNames.HoD,
            AssignedToUserId = OfficerId,
            AssignedToRole = RoleNames.ProjectOfficer,
            AssignedOn = new DateTime(2026, 10, 1, 5, 0, 0, DateTimeKind.Utc),
            SubmittedOn = status == ActionTaskStatuses.Submitted ? new DateTime(2026, 10, 19, 5, 0, 0, DateTimeKind.Utc) : null,
            DueDate = Today.AddDays(dueInDays).ToDateTime(TimeOnly.MinValue),
            RowVersion = new byte[] { 1 }
        };

    private static OfficerConferenceItemVm DirectedItem(int id, string title, params ConferenceProgressEntryVm[] entries)
        => new()
        {
            Kind = ConferenceItemKind.Project,
            ItemId = id,
            Title = title,
            LatestDirection = new ConferenceDirectionVm { Body = "Set the PDC.", AuthorName = "Hari Om Ahlawat", CreatedAtUtc = new DateTime(2026, 10, 8, 5, 0, 0, DateTimeKind.Utc) },
            ProgressEntries = entries
        };

    private sealed class FakeWorkload : IOfficerWorkloadReadService
    {
        private readonly IReadOnlyList<CommandOfficerWorkloadVm> _officers;

        public FakeWorkload(params CommandOfficerProjectVm[] projects)
        {
            _officers = projects.Length == 0
                ? Array.Empty<CommandOfficerWorkloadVm>()
                : new[] { new CommandOfficerWorkloadVm { UserId = OfficerId, OfficerName = "K Srinath", Projects = projects } };
        }

        public Task<IReadOnlyList<CommandOfficerWorkloadVm>> GetAllAsync(string requestingUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(_officers);

        public Task<CommandOfficerWorkloadVm?> GetOfficerAsync(string officerUserId, CancellationToken cancellationToken = default)
            => Task.FromResult<CommandOfficerWorkloadVm?>(null);

        public Task<int> CountActiveOfficersAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeApprovals : IApprovalQueueService
    {
        public Task<IReadOnlyList<ApprovalQueueItemVm>> GetPendingAsync(ApprovalQueueQuery query, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ApprovalQueueItemVm>>(new[]
            {
                new ApprovalQueueItemVm(ApprovalQueueType.PlanApproval, "7", 10, "SPRINT", OfficerId, "K Srinath",
                    new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero), "Timeline plan v2", ApprovalQueueModule.Projects, "Pending", null)
            });

        public Task<int> GetPendingCountAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task<ApprovalQueueDetailVm?> GetDetailAsync(ApprovalQueueType type, string requestId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => Task.FromResult<ApprovalQueueDetailVm?>(null);
    }

    private sealed class FakeConference : IOfficerConferenceReadService
    {
        private readonly OfficerConferenceVm[] _conferences;

        public FakeConference(params OfficerConferenceVm[] conferences) => _conferences = conferences;

        public Task<IReadOnlyList<OfficerConferenceVm>> GetAllForCommandAsync(string requestingUserId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OfficerConferenceVm>>(_conferences);

        public Task<IReadOnlyList<OfficerConferenceOfficerOptionVm>> GetOfficerOptionsAsync(string requestingUserId, string? selectedOfficerUserId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OfficerConferenceVm?> GetAsync(string requestingUserId, string officerUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OfficerConferenceVm?> GetForProjectOfficerAsync(string projectOfficerUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConferenceDirectionDigestVm?> GetLatestDirectionDigestAsync(string requestingUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConferenceDirectionHistoryVm?> GetDirectionHistoryAsync(string requestingUserId, string officerUserId, ConferenceItemKind kind, int itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConferenceDirectionHistoryVm?> GetDirectionHistoryForProjectOfficerAsync(string projectOfficerUserId, ConferenceItemKind kind, int itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
