using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Stages;

/// <summary>
/// Phase D-1d: lifecycle writers report Actual chronology violations their
/// committed state introduced as <c>Stages.ActualChronologyWarning</c>, through
/// ILogger and after commit, and never change their outcome or published state.
/// The PostgreSQL savepoint behaviour is covered by
/// <see cref="LifecycleChronologyObservabilityPostgresFixture"/>.
/// </summary>
public sealed class LifecycleChronologyObservabilityTests
{
    private static readonly DateOnly Today = new(2026, 3, 31);

    // SECTION: A. Direct lifecycle application
    [Fact]
    public async Task DirectApply_CompletingAPredecessorAfterItsSuccessorStarted_SucceedsAndWarnsOnceAfterCommit()
    {
        await using var fixture = await Fixture.CreateAsync(PredecessorInProgressSuccessorStarted);
        await using var db = fixture.NewContext();
        var logger = new CapturingLogger<StageDirectApplyService>(db);

        var result = await DirectApply(db, logger).ApplyAsync(
            fixture.ProjectId, StageCodes.FS, "Completed", Jan(10), null, "hod", false, CancellationToken.None);

        Assert.Equal("Completed", result.UpdatedStatus);
        await using var verify = fixture.NewContext();
        var fs = await StageAsync(verify, StageCodes.FS);
        Assert.Equal(StageStatus.Completed, fs.Status);
        Assert.Equal(Jan(10), fs.CompletedOn);
        Assert.Equal(Jan(5), (await StageAsync(verify, StageCodes.IPA)).ActualStart);
        Assert.Equal(new[] { "DirectApply", "Applied" }, await LogActionsAsync(verify));

        var warning = Assert.Single(logger.Warnings);
        AssertWarning(warning, "DirectApply", fixture.ProjectId, StageCodes.FS, affected: StageCodes.IPA, source: StageCodes.FS);
        Assert.False(warning.TransactionOpen);
    }

    [Fact]
    public async Task DirectApply_WithValidResultingChronology_LogsNothing()
    {
        await using var fixture = await Fixture.CreateAsync(PredecessorInProgressSuccessorStarted);
        await using var db = fixture.NewContext();
        var logger = new CapturingLogger<StageDirectApplyService>(db);

        await DirectApply(db, logger).ApplyAsync(
            fixture.ProjectId, StageCodes.FS, "Completed", Jan(4), null, "hod", false, CancellationToken.None);

        Assert.Empty(logger.Warnings);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task DirectApply_LeavingAnExistingViolationUnchanged_DoesNotReportItAgain()
    {
        await using var fixture = await Fixture.CreateAsync(db => Stages(db,
            Completed(StageCodes.FS, 1, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, 2, Jan(5))));
        await using var db = fixture.NewContext();
        var logger = new CapturingLogger<StageDirectApplyService>(db);

        var result = await DirectApply(db, logger).ApplyAsync(
            fixture.ProjectId, StageCodes.IPA, "Blocked", null, "Waiting on vendor", "hod", false, CancellationToken.None);

        Assert.Equal("Blocked", result.UpdatedStatus);
        Assert.Empty(logger.Warnings);
    }

    // SECTION: B. Approved lifecycle request
    [Fact]
    public async Task ApprovedRequest_IntroducingAViolation_CommitsDecisionHistoryAndNotification_ThenWarns()
    {
        await using var fixture = await Fixture.CreateAsync(PredecessorInProgressSuccessorStarted);
        var requestId = await fixture.SeedRequestAsync(StageCodes.FS, StageStatus.Completed, Jan(10));
        await using var db = fixture.NewContext();
        var logger = new CapturingLogger<StageDecisionService>(db);
        var notifications = new CountingNotifications();

        var result = await Decisions(db, logger, notifications)
            .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Approve, null), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        await using var verify = fixture.NewContext();
        Assert.Equal("Approved", (await verify.StageChangeRequests.SingleAsync()).DecisionStatus);
        Assert.Equal(Jan(10), (await StageAsync(verify, StageCodes.FS)).CompletedOn);
        Assert.Equal(new[] { "Approved", "Applied" }, await LogActionsAsync(verify));
        Assert.Equal(1, notifications.Count);

        var warning = Assert.Single(logger.Warnings);
        AssertWarning(warning, "LifecycleApproval", fixture.ProjectId, StageCodes.FS, affected: StageCodes.IPA, source: StageCodes.FS);
        Assert.False(warning.TransactionOpen);
    }

    [Fact]
    public async Task ApprovedRequest_RolledBackByAnEnclosingTransaction_LogsNoWarning()
    {
        await using var fixture = await Fixture.CreateAsync(PredecessorInProgressSuccessorStarted);
        var requestId = await fixture.SeedRequestAsync(StageCodes.FS, StageStatus.Completed, Jan(10));
        await using var db = fixture.NewContext();
        var logger = new CapturingLogger<StageDecisionService>(db);

        await using (var outer = await RelationalTransactionScope.CreateAsync(db.Database))
        {
            var result = await Decisions(db, logger, new CountingNotifications())
                .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Approve, null), "hod", false, true);
            Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
            // Disposed without commit: the approval never becomes durable.
        }

        Assert.Empty(logger.Warnings);
        await using var verify = fixture.NewContext();
        Assert.Equal("Pending", (await verify.StageChangeRequests.SingleAsync()).DecisionStatus);
    }

    [Fact]
    public async Task Rejection_ChangesNoStageFactsAndLogsNothing()
    {
        await using var fixture = await Fixture.CreateAsync(PredecessorInProgressSuccessorStarted);
        var requestId = await fixture.SeedRequestAsync(StageCodes.FS, StageStatus.Completed, Jan(10));
        await using var db = fixture.NewContext();
        var logger = new CapturingLogger<StageDecisionService>(db);

        var result = await Decisions(db, logger, new CountingNotifications())
            .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Reject, "Not yet"), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        Assert.Empty(logger.Warnings);
    }

    // SECTION: C. Historical stage reconstruction
    [Fact]
    public async Task HistoricalReconstruction_IntroducingAViolation_CommitsAndWarns()
    {
        await using var fixture = await Fixture.CreateAsync(_ => { }, LegacyCompletedProject);
        await using var db = fixture.NewContext();
        var logger = new CapturingLogger<HistoricalStageRecordService>(db);

        var result = await Historical(db, logger).SaveAsync(
            HistoricalInput(fixture.ProjectId, ipaStart: Jan(5)), "hod", "HoD");

        Assert.Equal(2, result.UpdatedCount);
        await using var verify = fixture.NewContext();
        Assert.Equal(Jan(5), (await StageAsync(verify, StageCodes.IPA)).ActualStart);
        Assert.Equal(new[] { "Backfill", "Backfill" }, await LogActionsAsync(verify));

        var warning = Assert.Single(logger.Warnings);
        AssertWarning(warning, "HistoricalReconstruction", fixture.ProjectId, "FS,IPA", affected: StageCodes.IPA, source: StageCodes.FS);
        Assert.False(warning.TransactionOpen);
    }

    [Fact]
    public async Task HistoricalReconstruction_WithValidChronology_LogsNothing()
    {
        await using var fixture = await Fixture.CreateAsync(_ => { }, LegacyCompletedProject);
        await using var db = fixture.NewContext();
        var logger = new CapturingLogger<HistoricalStageRecordService>(db);

        await Historical(db, logger).SaveAsync(HistoricalInput(fixture.ProjectId, ipaStart: Jan(12)), "hod", "HoD");

        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public async Task HistoricalReconstruction_EligibilityIsUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync(_ => { }, project => project.IsLegacy = false);
        await using var db = fixture.NewContext();

        await Assert.ThrowsAsync<HistoricalStageRecordNotAllowedException>(() =>
            Historical(db, new CapturingLogger<HistoricalStageRecordService>(db))
                .SaveAsync(HistoricalInput(fixture.ProjectId, ipaStart: Jan(5)), "hod", "HoD"));
    }

    // SECTION: D. Failure isolation
    [Fact]
    public async Task DirectApply_ObservationFailure_CommitsTheChangeAndLogsAnError()
    {
        await using var fixture = await Fixture.CreateAsync(PredecessorInProgressSuccessorStarted);
        await using var db = fixture.NewContext(new FailObservationRead());
        var logger = new CapturingLogger<StageDirectApplyService>(db);

        var result = await DirectApply(db, logger).ApplyAsync(
            fixture.ProjectId, StageCodes.FS, "Completed", Jan(10), null, "hod", false, CancellationToken.None);

        Assert.Equal("Completed", result.UpdatedStatus);
        await using var verify = fixture.NewContext();
        Assert.Equal(Jan(10), (await StageAsync(verify, StageCodes.FS)).CompletedOn);
        Assert.Equal(new[] { "DirectApply", "Applied" }, await LogActionsAsync(verify));
        Assert.Empty(logger.Warnings);
        AssertFailure(Assert.Single(logger.Errors), "DirectApply", fixture.ProjectId);
    }

    [Fact]
    public async Task ApprovedRequest_ObservationFailure_CommitsTheDecisionAndStillNotifies()
    {
        await using var fixture = await Fixture.CreateAsync(PredecessorInProgressSuccessorStarted);
        var requestId = await fixture.SeedRequestAsync(StageCodes.FS, StageStatus.Completed, Jan(10));
        await using var db = fixture.NewContext(new FailObservationRead());
        var logger = new CapturingLogger<StageDecisionService>(db);
        var notifications = new CountingNotifications();

        var result = await Decisions(db, logger, notifications)
            .DecideAsync(new StageDecisionInput(requestId, StageDecisionAction.Approve, null), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        await using var verify = fixture.NewContext();
        Assert.Equal("Approved", (await verify.StageChangeRequests.SingleAsync()).DecisionStatus);
        Assert.Equal(new[] { "Approved", "Applied" }, await LogActionsAsync(verify));
        Assert.Equal(1, notifications.Count);
        AssertFailure(Assert.Single(logger.Errors), "LifecycleApproval", fixture.ProjectId);
    }

    [Fact]
    public async Task HistoricalReconstruction_ObservationFailure_CommitsAndLogsAnError()
    {
        await using var fixture = await Fixture.CreateAsync(_ => { }, LegacyCompletedProject);
        await using var db = fixture.NewContext(new FailObservationRead());
        var logger = new CapturingLogger<HistoricalStageRecordService>(db);

        var result = await Historical(db, logger).SaveAsync(
            HistoricalInput(fixture.ProjectId, ipaStart: Jan(5)), "hod", "HoD");

        Assert.Equal(2, result.UpdatedCount);
        await using var verify = fixture.NewContext();
        Assert.Equal(new[] { "Backfill", "Backfill" }, await LogActionsAsync(verify));
        AssertFailure(Assert.Single(logger.Errors), "HistoricalReconstruction", fixture.ProjectId);
    }

    // SECTION: Helpers
    private static DateOnly Jan(int day) => new(2026, 1, day);

    private static void PredecessorInProgressSuccessorStarted(ApplicationDbContext db) => Stages(db,
        InProgress(StageCodes.FS, 1, Jan(1)),
        InProgress(StageCodes.IPA, 2, Jan(5)));

    private static void LegacyCompletedProject(Project project)
    {
        project.IsLegacy = true;
        project.LifecycleStatus = ProjectLifecycleStatus.Completed;
        project.CompletedOn = new DateOnly(2026, 3, 1);
    }

    private static void Stages(ApplicationDbContext db, params ProjectStage[] stages) => db.ProjectStages.AddRange(stages);

    private static ProjectStage InProgress(string code, int order, DateOnly start) =>
        new() { StageCode = code, SortOrder = order, Status = StageStatus.InProgress, ActualStart = start };

    private static ProjectStage Completed(string code, int order, DateOnly start, DateOnly completed) =>
        new() { StageCode = code, SortOrder = order, Status = StageStatus.Completed, ActualStart = start, CompletedOn = completed };

    private static HistoricalStageRecordInput HistoricalInput(int projectId, DateOnly ipaStart) => new()
    {
        ProjectId = projectId,
        EvidenceNote = "Closure report, 2026",
        Rows = new WorkflowStageMetadataProvider().GetStages(ProcurementWorkflow.VersionV1)
            .Select(definition => definition.Code switch
            {
                StageCodes.FS => new HistoricalStageRecordRowInput
                {
                    StageCode = definition.Code, Outcome = HistoricalStageOutcome.Completed,
                    ActualStart = Jan(1), CompletedOn = Jan(10)
                },
                StageCodes.IPA => new HistoricalStageRecordRowInput
                {
                    StageCode = definition.Code, Outcome = HistoricalStageOutcome.Completed,
                    ActualStart = ipaStart, CompletedOn = Jan(20)
                },
                _ => new HistoricalStageRecordRowInput { StageCode = definition.Code, Outcome = HistoricalStageOutcome.NotRecorded }
            })
            .ToList()
    };

    private static StageDirectApplyService DirectApply(ApplicationDbContext db, ILogger<StageDirectApplyService> logger)
    {
        var clock = FakeClock.ForIstDate(Today);
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        return new StageDirectApplyService(
            db, clock, new StageValidationService(db, clock, policy), new StageRulesService(db, policy), policy,
            logger: logger);
    }

    private static StageDecisionService Decisions(
        ApplicationDbContext db,
        ILogger<StageDecisionService> logger,
        IStageNotificationService notifications)
    {
        var clock = FakeClock.ForIstDate(Today);
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var progress = new StageProgressService(
            db, clock, new RecordingAudit(), new ProjectFactsReadService(db), notifications, policy);
        return new StageDecisionService(db, clock, progress, logger, policy);
    }

    private static HistoricalStageRecordService Historical(ApplicationDbContext db, ILogger<HistoricalStageRecordService> logger) =>
        new(db, new WorkflowStageMetadataProvider(), FakeClock.ForIstDate(Today), new RecordingAudit(), logger);

    private static async Task<ProjectStage> StageAsync(ApplicationDbContext db, string code) =>
        await db.ProjectStages.AsNoTracking().SingleAsync(stage => stage.StageCode == code);

    private static async Task<string[]> LogActionsAsync(ApplicationDbContext db) =>
        await db.StageChangeLogs.OrderBy(log => log.Id).Select(log => log.Action).ToArrayAsync();

    private static void AssertWarning(LogEntry entry, string operation, int projectId, string stageCode, string affected, string source)
    {
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("Stages.ActualChronologyWarning", entry.EventId.Name);
        Assert.Equal(operation, entry.Field("Operation"));
        Assert.Equal(projectId, entry.Field("ProjectId"));
        Assert.Equal(stageCode, entry.Field("StageCode"));
        Assert.Equal(1, entry.Field("ViolationCount"));
        Assert.Equal(new[] { affected }, (string[])entry.Field("AffectedStageCodes")!);
        Assert.Equal(new[] { source }, (string?[])entry.Field("SourceStageCodes")!);
        Assert.Single((string[])entry.Field("Violations")!);
    }

    private static void AssertFailure(LogEntry entry, string operation, int projectId)
    {
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Stages.ActualChronologyWarningFailed", entry.EventId.Name);
        Assert.Equal(operation, entry.Field("Operation"));
        Assert.Equal(projectId, entry.Field("ProjectId"));
        Assert.NotNull(entry.Exception);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection) => _connection = connection;

        public int ProjectId { get; private set; }

        public static async Task<Fixture> CreateAsync(Action<ApplicationDbContext> seed, Action<Project>? configure = null)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);

            await using var db = fixture.NewContext();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new ApplicationUser { Id = "seed", UserName = "seed" });
            var project = new Project { Name = "D-1d observability", CreatedByUserId = "seed", WorkflowVersion = ProcurementWorkflow.VersionV1 };
            configure?.Invoke(project);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            fixture.ProjectId = project.Id;

            seed(db);
            foreach (var stage in db.ChangeTracker.Entries<ProjectStage>().Select(entry => entry.Entity))
            {
                stage.ProjectId = project.Id;
            }

            await db.SaveChangesAsync();
            return fixture;
        }

        public async Task<int> SeedRequestAsync(string stageCode, StageStatus requested, DateOnly requestedDate)
        {
            await using var db = NewContext();
            var request = new StageChangeRequest
            {
                ProjectId = ProjectId, StageCode = stageCode, RequestedStatus = requested.ToString(),
                RequestedDate = requestedDate, RequestedByUserId = "po", RequestedOn = DateTimeOffset.UtcNow,
                DecisionStatus = "Pending"
            };
            db.StageChangeRequests.Add(request);
            await db.SaveChangesAsync();
            return request.Id;
        }

        public ApplicationDbContext NewContext(IInterceptor? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection);
            if (interceptor is not null)
            {
                builder.AddInterceptors(interceptor);
            }

            return new ApplicationDbContext(builder.Options);
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    /// <summary>
    /// Fails the first ARPP read after the writer has saved its stage history: the
    /// observer's read, never one of the writer's own.
    /// </summary>
    private sealed class FailObservationRead : DbCommandInterceptor
    {
        private bool _historySaved;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"StageChangeLogs\"", StringComparison.Ordinal))
            {
                _historySaved = true;
            }
            else if (_historySaved && command.CommandText.Contains("\"ArppPublishedEntries\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Injected observability read failure.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class CountingNotifications : IStageNotificationService
    {
        public int Count { get; private set; }

        public Task NotifyStageStatusChangedAsync(
            ProjectStage stage, Project project, StageStatus previousStatus, string actorUserId,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}
