using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Phase D-1d on PostgreSQL. Lifecycle writers observe their final Actual
/// chronology inside their transaction; on PostgreSQL any failed statement aborts
/// the transaction, so the observation runs under a savepoint. These scenarios make
/// the observation's read fail inside PostgreSQL and require the lifecycle write,
/// its history and its outcome to commit unchanged.
/// </summary>
internal static class LifecycleChronologyObservabilityPostgresFixture
{
    private const string Seeder = "d1d-seed";
    private static readonly DateOnly Today = new(2026, 3, 31);

    internal static async Task RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using (var seed = new ApplicationDbContext(options))
        {
            seed.Users.Add(new ApplicationUser { Id = Seeder, UserName = Seeder });
            await seed.SaveChangesAsync();
        }

        await ApprovalWarnsAfterCommitAsync(options);
        await ApprovalCommitsWhenTheObservationFailsInsidePostgresAsync(options);
        await DirectApplyCommitsWhenTheObservationFailsInsidePostgresAsync(options);
        await HistoricalReconstructionCommitsWhenTheObservationFailsInsidePostgresAsync(options);
    }

    /// <summary>The savepoint path on PostgreSQL: the warning is produced and the approval commits.</summary>
    private static async Task ApprovalWarnsAfterCommitAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, requestId) = await SeedFsCompletionRequestAsync(options, "D-1d PG approval warning");

        await using var db = new ApplicationDbContext(options);
        var logger = new CapturingLogger<StageDecisionService>(db);
        var result = await Decisions(db, logger).DecideAsync(
            new StageDecisionInput(requestId, StageDecisionAction.Approve, null), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        await AssertFsCompletedWithHistoryAsync(options, projectId, "Approved", "Applied");
        var warning = Assert.Single(logger.Warnings);
        Assert.Equal(projectId, warning.Field("ProjectId"));
        Assert.Equal(new[] { StageCodes.IPA }, (string[])warning.Field("AffectedStageCodes")!);
        Assert.False(warning.TransactionOpen);
        Assert.Empty(logger.Errors);
    }

    /// <summary>
    /// The observation's ARPP read fails inside PostgreSQL. Without the savepoint
    /// the transaction would be aborted and the approval's commit would fail.
    /// </summary>
    private static async Task ApprovalCommitsWhenTheObservationFailsInsidePostgresAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, requestId) = await SeedFsCompletionRequestAsync(options, "D-1d PG approval observation failure");

        await using var db = Poisoned(options);
        var logger = new CapturingLogger<StageDecisionService>(db);
        var result = await Decisions(db, logger).DecideAsync(
            new StageDecisionInput(requestId, StageDecisionAction.Approve, null), "hod", false, true);

        Assert.Equal(StageDecisionOutcome.Success, result.Outcome);
        await using (var verify = new ApplicationDbContext(options))
        {
            Assert.Equal("Approved", (await verify.StageChangeRequests.AsNoTracking().SingleAsync(item => item.Id == requestId)).DecisionStatus);
        }

        await AssertFsCompletedWithHistoryAsync(options, projectId, "Approved", "Applied");
        AssertObservationFailureLogged(logger, projectId);
    }

    private static async Task DirectApplyCommitsWhenTheObservationFailsInsidePostgresAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var (projectId, _) = await SeedFsCompletionRequestAsync(options, "D-1d PG direct apply observation failure", withRequest: false);

        await using var db = Poisoned(options);
        var logger = new CapturingLogger<StageDirectApplyService>(db);
        var clock = FakeClock.ForIstDate(Today);
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var result = await new StageDirectApplyService(
                db, clock, new StageValidationService(db, clock, policy), new StageRulesService(db, policy), policy,
                logger: logger)
            .ApplyAsync(projectId, StageCodes.FS, "Completed", Jan(10), null, "hod", false, CancellationToken.None);

        Assert.Equal("Completed", result.UpdatedStatus);
        await AssertFsCompletedWithHistoryAsync(options, projectId, "DirectApply", "Applied");
        AssertObservationFailureLogged(logger, projectId);
    }

    private static async Task HistoricalReconstructionCommitsWhenTheObservationFailsInsidePostgresAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        int projectId;
        await using (var seed = new ApplicationDbContext(options))
        {
            var project = NewProject("D-1d PG historical observation failure");
            project.IsLegacy = true;
            project.LifecycleStatus = ProjectLifecycleStatus.Completed;
            project.CompletedOn = new DateOnly(2026, 3, 1);
            seed.Projects.Add(project);
            await seed.SaveChangesAsync();
            projectId = project.Id;
        }

        await using var db = Poisoned(options);
        var logger = new CapturingLogger<HistoricalStageRecordService>(db);
        var result = await new HistoricalStageRecordService(
                db, new WorkflowStageMetadataProvider(), FakeClock.ForIstDate(Today), new RecordingAudit(), logger)
            .SaveAsync(HistoricalInput(projectId), "hod", "HoD");

        Assert.Equal(2, result.UpdatedCount);
        await using (var verify = new ApplicationDbContext(options))
        {
            Assert.Equal(2, await verify.ProjectStages.CountAsync(stage => stage.ProjectId == projectId));
            Assert.Equal(2, await verify.StageChangeLogs.CountAsync(log => log.ProjectId == projectId && log.Action == "Backfill"));
        }

        AssertObservationFailureLogged(logger, projectId);
    }

    // SECTION: Helpers
    private static DateOnly Jan(int day) => new(2026, 1, day);

    private static Project NewProject(string name) => new()
    {
        Name = name,
        CreatedByUserId = Seeder,
        WorkflowVersion = ProcurementWorkflow.VersionV1,
        RowVersion = Array.Empty<byte>()
    };

    /// <summary>FS in progress since Jan 1, IPA already started Jan 5, and (optionally) a request to complete FS on Jan 10.</summary>
    private static async Task<(int ProjectId, int RequestId)> SeedFsCompletionRequestAsync(
        DbContextOptions<ApplicationDbContext> options,
        string name,
        bool withRequest = true)
    {
        await using var db = new ApplicationDbContext(options);
        var project = NewProject(name);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectStages.AddRange(
            new ProjectStage { ProjectId = project.Id, StageCode = StageCodes.FS, SortOrder = 1, Status = StageStatus.InProgress, ActualStart = Jan(1) },
            new ProjectStage { ProjectId = project.Id, StageCode = StageCodes.IPA, SortOrder = 2, Status = StageStatus.InProgress, ActualStart = Jan(5) });
        var request = new StageChangeRequest
        {
            ProjectId = project.Id, StageCode = StageCodes.FS, RequestedStatus = StageStatus.Completed.ToString(),
            RequestedDate = Jan(10), RequestedByUserId = "po", RequestedOn = DateTimeOffset.UtcNow, DecisionStatus = "Pending"
        };
        if (withRequest)
        {
            db.StageChangeRequests.Add(request);
        }

        await db.SaveChangesAsync();
        return (project.Id, withRequest ? request.Id : 0);
    }

    private static StageDecisionService Decisions(ApplicationDbContext db, CapturingLogger<StageDecisionService> logger)
    {
        var clock = FakeClock.ForIstDate(Today);
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        var progress = new StageProgressService(
            db, clock, new RecordingAudit(), new ProjectFactsReadService(db), new NoNotifications(), policy);
        return new StageDecisionService(db, clock, progress, logger, policy);
    }

    private static HistoricalStageRecordInput HistoricalInput(int projectId) => new()
    {
        ProjectId = projectId,
        EvidenceNote = "Closure report, 2026",
        Rows = new WorkflowStageMetadataProvider().GetStages(ProcurementWorkflow.VersionV1)
            .Select(definition => definition.Code switch
            {
                StageCodes.FS => new HistoricalStageRecordRowInput
                {
                    StageCode = definition.Code, Outcome = HistoricalStageOutcome.Completed, ActualStart = Jan(1), CompletedOn = Jan(10)
                },
                StageCodes.IPA => new HistoricalStageRecordRowInput
                {
                    StageCode = definition.Code, Outcome = HistoricalStageOutcome.Completed, ActualStart = Jan(5), CompletedOn = Jan(20)
                },
                _ => new HistoricalStageRecordRowInput { StageCode = definition.Code, Outcome = HistoricalStageOutcome.NotRecorded }
            })
            .ToList()
    };

    private static async Task AssertFsCompletedWithHistoryAsync(
        DbContextOptions<ApplicationDbContext> options,
        int projectId,
        params string[] history)
    {
        await using var db = new ApplicationDbContext(options);
        var fs = await db.ProjectStages.AsNoTracking()
            .SingleAsync(stage => stage.ProjectId == projectId && stage.StageCode == StageCodes.FS);
        Assert.Equal(StageStatus.Completed, fs.Status);
        Assert.Equal(Jan(10), fs.CompletedOn);
        Assert.Equal(history, await db.StageChangeLogs.AsNoTracking()
            .Where(log => log.ProjectId == projectId)
            .OrderBy(log => log.Id)
            .Select(log => log.Action)
            .ToArrayAsync());
    }

    private static void AssertObservationFailureLogged<T>(CapturingLogger<T> logger, int projectId)
    {
        var failure = Assert.Single(logger.Errors);
        Assert.Equal(projectId, failure.Field("ProjectId"));
        Assert.IsAssignableFrom<DbException>(failure.Exception);
        Assert.Empty(logger.Warnings);
    }

    private static ApplicationDbContext Poisoned(DbContextOptions<ApplicationDbContext> options) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(new FailObservationReadInPostgres()).Options);

    /// <summary>
    /// After the writer has saved its stage history, turns the next ARPP read (the
    /// observation's) into a statement that fails inside PostgreSQL (division by
    /// zero), which aborts the current transaction unless a savepoint isolates it.
    /// </summary>
    private sealed class FailObservationReadInPostgres : DbCommandInterceptor
    {
        private bool _historySaved;
        private bool _failed;

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
            else if (_historySaved && !_failed && command.CommandText.Contains("\"ArppPublishedEntries\"", StringComparison.Ordinal))
            {
                _failed = true;
                command.CommandText = "SELECT 1 / 0";
                command.Parameters.Clear();
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class NoNotifications : IStageNotificationService
    {
        public Task NotifyStageStatusChangedAsync(
            ProjectStage stage, Project project, StageStatus previousStatus, string actorUserId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
