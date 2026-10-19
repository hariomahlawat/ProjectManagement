using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Stages;

/// <summary>
/// Phase D-1c: a lifecycle decision publishes the request decision, the stage
/// state and its mandatory history in one relational transaction, and runs the
/// secondary audit and notification only after that transaction commits.
/// Concurrent decisions are covered on PostgreSQL by
/// <see cref="StageDecisionConcurrencyPostgresFixture"/>.
/// </summary>
public sealed class StageDecisionTransactionTests
{
    private const int ProjectId = 1;
    private static readonly DateOnly Today = new(2026, 3, 31);

    // SECTION: Functional outcomes
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Approve_PublishesDecisionStageAndHistoryTogether_ThenNotifiesAfterCommit(bool isAdmin, bool isHoD)
    {
        await using var fixture = await Fixture.CreateAsync(
            Stage(StageCodes.FS, 1, StageStatus.Completed, Jan(1), Jan(10)),
            Stage(StageCodes.IPA, 2, StageStatus.NotStarted));
        var requestId = await fixture.SeedRequestAsync(StageCodes.IPA, StageStatus.InProgress, Jan(12));
        await using var db = fixture.NewContext();
        var sideEffects = new SideEffects(db);

        var result = await fixture.Decisions(db, sideEffects)
            .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Approve, " ok "), "hod", isAdmin, isHoD);

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        Assert.Equal(StageStatus.InProgress, result.Stage!.Status);
        await using var verify = fixture.NewContext();
        var request = await verify.StageChangeRequests.SingleAsync();
        Assert.Equal("Approved", request.DecisionStatus);
        Assert.Equal("hod", request.DecidedByUserId);
        Assert.Equal("ok", request.DecisionNote);
        var ipa = await verify.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.InProgress, ipa.Status);
        Assert.Equal(Jan(12), ipa.ActualStart);
        Assert.Equal(new[] { "Approved", "Applied" }, await LogActionsAsync(verify));

        Assert.Equal(1, sideEffects.Notifications);
        Assert.Contains(sideEffects.Audit.Entries, entry => entry.Action == "Stages.StageStatusChanged");
        Assert.True(sideEffects.AllRanOutsideTransaction);
    }

    [Fact]
    public async Task Reject_RecordsDecisionAndHistory_WithoutLifecycleChangeOrNotification()
    {
        await using var fixture = await Fixture.CreateAsync(
            Stage(StageCodes.FS, 1, StageStatus.Completed, Jan(1), Jan(10)),
            Stage(StageCodes.IPA, 2, StageStatus.InProgress, Jan(11)));
        var requestId = await fixture.SeedRequestAsync(StageCodes.IPA, StageStatus.Completed, Jan(20));
        await using var db = fixture.NewContext();
        var sideEffects = new SideEffects(db);

        var result = await fixture.Decisions(db, sideEffects)
            .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Reject, " needs evidence "), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        await using var verify = fixture.NewContext();
        var request = await verify.StageChangeRequests.SingleAsync();
        Assert.Equal("Rejected", request.DecisionStatus);
        Assert.Equal("needs evidence", request.DecisionNote);
        var ipa = await verify.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.InProgress, ipa.Status);
        Assert.Null(ipa.CompletedOn);
        var log = await verify.StageChangeLogs.SingleAsync();
        Assert.Equal("Rejected", log.Action);
        Assert.Equal(Jan(11), log.FromActualStart);
        Assert.Equal(0, sideEffects.Notifications);
    }

    [Theory]
    [InlineData("Approved", StageDecisionAction.Approve)]
    [InlineData("Approved", StageDecisionAction.Reject)]
    [InlineData("Rejected", StageDecisionAction.Approve)]
    [InlineData("Rejected", StageDecisionAction.Reject)]
    [InlineData("Superseded", StageDecisionAction.Approve)]
    public async Task AlreadyDecidedRequest_IsRefusedWithoutWrites(string decided, StageDecisionAction action)
    {
        await using var fixture = await Fixture.CreateAsync(
            Stage(StageCodes.FS, 1, StageStatus.Completed, Jan(1), Jan(10)),
            Stage(StageCodes.IPA, 2, StageStatus.NotStarted));
        var requestId = await fixture.SeedRequestAsync(StageCodes.IPA, StageStatus.InProgress, Jan(12), decided);
        await using var db = fixture.NewContext();
        var sideEffects = new SideEffects(db);

        var result = await fixture.Decisions(db, sideEffects)
            .DecideAsync(new StageDecisionInput(requestId, action, null), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.AlreadyDecided, result.Outcome);
        await AssertNothingPublishedAsync(fixture, decided);
        Assert.Equal(0, sideEffects.Notifications);
    }

    [Fact]
    public async Task MissingRequest_IsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync(Stage(StageCodes.FS, 1, StageStatus.NotStarted));
        await using var db = fixture.NewContext();

        var result = await fixture.Decisions(db, new SideEffects(db))
            .DecideAsync(new StageDecisionInput(999, StageDecisionAction.Approve, null), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.RequestNotFound, result.Outcome);
    }

    [Fact]
    public async Task MissingStage_IsNotFoundAndLeavesTheRequestPending()
    {
        await using var fixture = await Fixture.CreateAsync(Stage(StageCodes.FS, 1, StageStatus.Completed, Jan(1), Jan(10)));
        var requestId = await fixture.SeedRequestAsync(StageCodes.IPA, StageStatus.InProgress, Jan(12));
        await using var db = fixture.NewContext();

        var result = await fixture.Decisions(db, new SideEffects(db))
            .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Approve, null), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.StageNotFound, result.Outcome);
        await AssertNothingPublishedAsync(fixture, "Pending");
    }

    [Theory]
    [InlineData(StageDecisionAction.Approve)]
    [InlineData(StageDecisionAction.Reject)]
    public async Task ActorWithoutDecisionAuthority_IsRefusedWithoutWrites(StageDecisionAction action)
    {
        await using var fixture = await Fixture.CreateAsync(
            Stage(StageCodes.FS, 1, StageStatus.Completed, Jan(1), Jan(10)),
            Stage(StageCodes.IPA, 2, StageStatus.NotStarted));
        var requestId = await fixture.SeedRequestAsync(StageCodes.IPA, StageStatus.InProgress, Jan(12));
        await using var db = fixture.NewContext();

        var result = await fixture.Decisions(db, new SideEffects(db))
            .DecideAsync(new StageDecisionInput(requestId, action, null), "po", isAdmin: false, isHoD: false);

        Assert.Equal(StageDecisionOutcome.NotHeadOfDepartment, result.Outcome);
        await AssertNothingPublishedAsync(fixture, "Pending");
    }

    [Fact]
    public async Task ApprovalOfArppManagedIpa_IsRefusedAndTheRequestStaysPending()
    {
        await using var fixture = await Fixture.CreateAsync(
            Stage(StageCodes.FS, 1, StageStatus.Completed, Jan(1), Jan(10)),
            Stage(StageCodes.IPA, 2, StageStatus.InProgress, Jan(11)));
        var requestId = await fixture.SeedRequestAsync(StageCodes.IPA, StageStatus.Completed, Jan(20));
        await using var db = fixture.NewContext();

        var result = await fixture.Decisions(db, new SideEffects(db), arppManaged: true)
            .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Approve, null), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(ArppManagedIpaStageException.UserMessage, result.Error);
        await AssertNothingPublishedAsync(fixture, "Pending", StageStatus.InProgress);
    }

    [Fact]
    public async Task ApprovalWithAnIncompletePredecessor_IsRefusedWithoutWrites()
    {
        await using var fixture = await Fixture.CreateAsync(
            Stage(StageCodes.FS, 1, StageStatus.InProgress, Jan(1)),
            Stage(StageCodes.IPA, 2, StageStatus.NotStarted));
        var requestId = await fixture.SeedRequestAsync(StageCodes.IPA, StageStatus.InProgress, Jan(12));
        await using var db = fixture.NewContext();

        var result = await fixture.Decisions(db, new SideEffects(db))
            .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Approve, null), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(StageCodes.FS, result.Error);
        await AssertNothingPublishedAsync(fixture, "Pending");
    }

    // SECTION: Atomicity
    [Theory]
    [InlineData(StageDecisionAction.Approve)]
    [InlineData(StageDecisionAction.Reject)]
    public async Task FailedHistoryWrite_RollsBackTheClaimAndTheStage_AndRunsNoSideEffects(StageDecisionAction action)
    {
        await using var fixture = await Fixture.CreateAsync(
            Stage(StageCodes.FS, 1, StageStatus.Completed, Jan(1), Jan(10)),
            Stage(StageCodes.IPA, 2, StageStatus.NotStarted));
        var requestId = await fixture.SeedRequestAsync(StageCodes.IPA, StageStatus.InProgress, Jan(12));
        await using var db = fixture.NewContext(failStageHistory: true);
        var sideEffects = new SideEffects(db);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Decisions(db, sideEffects)
            .DecideAsync(new StageDecisionInput(requestId, action, null), "hod", false, true));

        await AssertNothingPublishedAsync(fixture, "Pending");
        Assert.Equal(0, sideEffects.Notifications);
        Assert.Empty(sideEffects.Audit.Entries);
    }

    [Fact]
    public async Task RolledBackDecision_CanBeDecidedAgain()
    {
        await using var fixture = await Fixture.CreateAsync(
            Stage(StageCodes.FS, 1, StageStatus.Completed, Jan(1), Jan(10)),
            Stage(StageCodes.IPA, 2, StageStatus.NotStarted));
        var requestId = await fixture.SeedRequestAsync(StageCodes.IPA, StageStatus.InProgress, Jan(12));
        await using (var failing = fixture.NewContext(failStageHistory: true))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Decisions(failing, new SideEffects(failing))
                .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Approve, null), "hod", false, true));
        }

        await using var db = fixture.NewContext();
        var result = await fixture.Decisions(db, new SideEffects(db))
            .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Approve, null), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        await using var verify = fixture.NewContext();
        Assert.Equal("Approved", (await verify.StageChangeRequests.SingleAsync()).DecisionStatus);
        Assert.Equal(new[] { "Approved", "Applied" }, await LogActionsAsync(verify));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PostCommitSideEffectFailure_DoesNotUndoOrFailTheCommittedDecision(bool failNotification, bool failAudit)
    {
        await using var fixture = await Fixture.CreateAsync(
            Stage(StageCodes.FS, 1, StageStatus.Completed, Jan(1), Jan(10)),
            Stage(StageCodes.IPA, 2, StageStatus.NotStarted));
        var requestId = await fixture.SeedRequestAsync(StageCodes.IPA, StageStatus.InProgress, Jan(12));
        await using var db = fixture.NewContext();
        var sideEffects = new SideEffects(db, failNotification, failAudit);

        var result = await fixture.Decisions(db, sideEffects)
            .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Approve, null), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        await using var verify = fixture.NewContext();
        Assert.Equal("Approved", (await verify.StageChangeRequests.SingleAsync()).DecisionStatus);
        Assert.Equal(StageStatus.InProgress, (await verify.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA)).Status);
        Assert.Equal(new[] { "Approved", "Applied" }, await LogActionsAsync(verify));
        // The other side effect still runs: one failure does not cancel the rest.
        Assert.Equal(1, sideEffects.NotificationAttempts);
        Assert.Equal(1, sideEffects.AuditAttempts);
    }

    [Fact]
    public async Task ApprovedCompletion_PublishesCompletionAndHistory_ThenAuditsAfterCommit()
    {
        await using var fixture = await Fixture.CreateAsync(
            Stage(StageCodes.FS, 1, StageStatus.InProgress, Jan(1)));
        var requestId = await fixture.SeedRequestAsync(StageCodes.FS, StageStatus.Completed, Jan(10));
        await using var db = fixture.NewContext();
        var sideEffects = new SideEffects(db);

        var result = await fixture.Decisions(db, sideEffects)
            .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Approve, null), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        await using var verify = fixture.NewContext();
        var fs = await verify.ProjectStages.SingleAsync();
        Assert.Equal(StageStatus.Completed, fs.Status);
        Assert.Equal(Jan(1), fs.ActualStart);
        Assert.Equal(Jan(10), fs.CompletedOn);
        Assert.Equal(new[] { "Approved", "Applied" }, await LogActionsAsync(verify));
        Assert.Equal(1, sideEffects.Notifications);
        Assert.True(sideEffects.AllRanOutsideTransaction);
    }

    // SECTION: Helpers
    private static DateOnly Jan(int day) => new(2026, 1, day);

    private static ProjectStage Stage(
        string code,
        int order,
        StageStatus status,
        DateOnly? actualStart = null,
        DateOnly? completedOn = null) => new()
    {
        ProjectId = ProjectId,
        StageCode = code,
        SortOrder = order,
        Status = status,
        ActualStart = actualStart,
        CompletedOn = completedOn
    };

    private static async Task<string[]> LogActionsAsync(ApplicationDbContext db) =>
        await db.StageChangeLogs.OrderBy(log => log.Id).Select(log => log.Action).ToArrayAsync();

    private static async Task AssertNothingPublishedAsync(
        Fixture fixture,
        string expectedDecisionStatus,
        StageStatus expectedIpaStatus = StageStatus.NotStarted)
    {
        await using var verify = fixture.NewContext();
        var request = await verify.StageChangeRequests.SingleAsync();
        Assert.Equal(expectedDecisionStatus, request.DecisionStatus);
        Assert.Empty(await verify.StageChangeLogs.ToListAsync());
        Assert.All(
            await verify.ProjectStages.Where(stage => stage.StageCode == StageCodes.IPA).ToListAsync(),
            stage => Assert.Equal(expectedIpaStatus, stage.Status));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection) => _connection = connection;

        public static async Task<Fixture> CreateAsync(params ProjectStage[] stages)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);

            await using var db = fixture.NewContext();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new ApplicationUser { Id = "seed", UserName = "seed" });
            db.Projects.Add(new Project
            {
                Id = ProjectId,
                Name = "Decision project",
                CreatedByUserId = "seed",
                WorkflowVersion = ProcurementWorkflow.VersionV1
            });
            db.ProjectStages.AddRange(stages);
            await db.SaveChangesAsync();
            return fixture;
        }

        public async Task<int> SeedRequestAsync(
            string stageCode,
            StageStatus requested,
            DateOnly requestedDate,
            string decisionStatus = "Pending")
        {
            await using var db = NewContext();
            var request = new StageChangeRequest
            {
                ProjectId = ProjectId,
                StageCode = stageCode,
                RequestedStatus = requested.ToString(),
                RequestedDate = requestedDate,
                RequestedByUserId = "po",
                RequestedOn = DateTimeOffset.UtcNow,
                DecisionStatus = decisionStatus
            };
            db.StageChangeRequests.Add(request);
            await db.SaveChangesAsync();
            return request.Id;
        }

        public ApplicationDbContext NewContext(bool failStageHistory = false)
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection);
            if (failStageHistory)
            {
                builder.AddInterceptors(new FailStageHistoryInterceptor());
            }

            return new ApplicationDbContext(builder.Options);
        }

        public StageDecisionService Decisions(ApplicationDbContext db, SideEffects sideEffects, bool arppManaged = false)
        {
            var clock = FakeClock.ForIstDate(Today);
            var policy = StageWorkflowTestFactory.CreatePolicy(db);
            var arpp = new StubArppAuthority(arppManaged);
            var progress = new StageProgressService(
                db, clock, sideEffects, new ProjectFactsReadService(db), sideEffects, policy,
                arppIpaStageAuthority: arpp);
            return new StageDecisionService(
                db, clock, progress, NullLogger<StageDecisionService>.Instance, policy,
                arppIpaStageAuthority: arpp);
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    /// <summary>Records secondary side effects and whether a transaction was still open when they ran.</summary>
    private sealed class SideEffects(ApplicationDbContext db, bool failNotification = false, bool failAudit = false)
        : IStageNotificationService, IAuditService
    {
        public RecordingAudit Audit { get; } = new();
        public int Notifications { get; private set; }
        public int NotificationAttempts { get; private set; }
        public int AuditAttempts { get; private set; }
        public bool AllRanOutsideTransaction { get; private set; } = true;

        public Task NotifyStageStatusChangedAsync(
            ProjectStage stage, Project project, StageStatus previousStatus, string actorUserId,
            CancellationToken cancellationToken = default)
        {
            NotificationAttempts++;
            AllRanOutsideTransaction &= db.Database.CurrentTransaction is null;
            if (failNotification)
            {
                throw new InvalidOperationException("Injected notification failure.");
            }

            Notifications++;
            return Task.CompletedTask;
        }

        public Task LogAsync(
            string action, string? message = null, string level = "Info", string? userId = null,
            string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null)
        {
            AuditAttempts++;
            AllRanOutsideTransaction &= db.Database.CurrentTransaction is null;
            if (failAudit)
            {
                throw new InvalidOperationException("Injected audit failure.");
            }

            return Audit.LogAsync(action, message, level, userId, userName, data, http);
        }
    }

    /// <summary>Fails the save that carries stage history, after the claim and stage rows were written.</summary>
    private sealed class FailStageHistoryInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<StageChangeLog>().Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new DbUpdateException("Injected stage-history failure.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class StubArppAuthority(bool managed) : IArppIpaStageAuthorityService
    {
        public Task<ArppIpaStageAuthority?> ResolveAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ArppIpaStageAuthority?>(null);

        public Task<IReadOnlyDictionary<int, ArppIpaStageAuthority>> ResolveManyAsync(
            IReadOnlyCollection<int> projectIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, ArppIpaStageAuthority>>(new Dictionary<int, ArppIpaStageAuthority>());

        public Task<bool> IsManagedAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(managed);

        public Task EnsureManualLifecycleMutationAllowedAsync(int projectId, string stageCode, CancellationToken cancellationToken = default) =>
            managed && string.Equals(stageCode, StageCodes.IPA, StringComparison.OrdinalIgnoreCase)
                ? throw new ArppManagedIpaStageException(projectId)
                : Task.CompletedTask;
    }
}
