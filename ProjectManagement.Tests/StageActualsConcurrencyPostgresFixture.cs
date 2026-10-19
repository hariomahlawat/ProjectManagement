using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Plans;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Phase D-1b relational guarantees for direct Actual correction, run inside the
/// disposable migrated PostgreSQL database used by the production migration
/// gate. SQLite cannot verify advisory-lock, row-lock or rollback behaviour.
/// </summary>
internal static class StageActualsConcurrencyPostgresFixture
{
    private const string Actor = "d1b-actuals-actor";
    private const string FormerLead = "d1b-former-lead-po";
    private const string NewLead = "d1b-new-lead-po";
    private static readonly DateOnly Today = new(2026, 3, 31);

    internal static async Task RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using (var seed = new ApplicationDbContext(options))
        {
            seed.Users.AddRange(
                new ApplicationUser { Id = Actor, UserName = Actor },
                new ApplicationUser { Id = FormerLead, UserName = FormerLead },
                new ApplicationUser { Id = NewLead, UserName = NewLead });
            await seed.SaveChangesAsync();
        }

        await ValidationRunsAgainstStateCommittedBehindTheGateAsync(options);
        await RowLockStopsAWriterOutsideTheGateFromBeingOverwrittenAsync(options);
        await HistoryFailureRollsBackTheCorrectionAsync(options);
        await ReassignmentCommittedDuringSubmissionRevokesAuthorityAsync(options);
        await DeletionCommittedDuringSubmissionRejectsBackfillAsync(options);
    }

    /// <summary>
    /// Lead-PO reassignment (<c>AssignRoles</c>) updates the project row without
    /// taking the project gate. A correction by the outgoing lead that is already
    /// past its page-level check must wait on the project row and then be refused
    /// against the newly committed assignment.
    /// </summary>
    private static async Task ReassignmentCommittedDuringSubmissionRevokesAuthorityAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-1b reassignment race",
            project => project.LeadPoUserId = FormerLead,
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(12)));

        await using var reader = new ApplicationDbContext(options);
        var input = await EditorInputAsync(reader, projectId, StageCodes.IPA, Jan(11));

        await using var assigner = new ApplicationDbContext(options);
        await using var assignment = await assigner.Database.BeginTransactionAsync();
        await assigner.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Projects\" SET \"LeadPoUserId\" = {NewLead} WHERE \"Id\" = {projectId}");

        await using var contender = new ApplicationDbContext(options);
        var update = Service(contender).UpdateAsync(input, TestActors.ProjectOfficer(FormerLead), FormerLead);
        await WaitUntilBlockedOnALockAsync(options, update);
        await assignment.CommitAsync();

        await Assert.ThrowsAsync<ActualsWriteForbiddenException>(() => update.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(Jan(12), await StartOfAsync(options, projectId, StageCodes.IPA));
        Assert.Equal(0, await HistoryCountAsync(options, projectId));
    }

    /// <summary>
    /// Moving a project to Trash (<c>ProjectModerationService.MoveToTrashAsync</c>)
    /// also updates the project row without the gate. An HoD backfill in flight
    /// must wait and then be refused for the deleted project.
    /// </summary>
    private static async Task DeletionCommittedDuringSubmissionRejectsBackfillAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-1b deletion race",
            _ => { },
            new ProjectStage { StageCode = StageCodes.BID, Status = StageStatus.Completed, ActualStart = Jan(5), RequiresBackfill = true });

        await using var moderator = new ApplicationDbContext(options);
        await using var deletion = await moderator.Database.BeginTransactionAsync();
        await moderator.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Projects\" SET \"IsDeleted\" = TRUE WHERE \"Id\" = {projectId}");

        await using var contender = new ApplicationDbContext(options);
        var backfill = new StageBackfillService(contender, FakeClock.ForIstDate(Today)).ApplyAsync(
            projectId,
            new[] { new StageBackfillUpdate(StageCodes.BID, null, Jan(20)) },
            TestActors.Hod(Actor));
        await WaitUntilBlockedOnALockAsync(options, backfill);
        await deletion.CommitAsync();

        await Assert.ThrowsAsync<ActualsProjectUnavailableException>(() => backfill.WaitAsync(TimeSpan.FromSeconds(30)));
        await using var verify = new ApplicationDbContext(options);
        var stage = await verify.ProjectStages.AsNoTracking().SingleAsync(item => item.ProjectId == projectId);
        Assert.Null(stage.CompletedOn);
        Assert.True(stage.RequiresBackfill);
        Assert.Equal(0, await HistoryCountAsync(options, projectId));
    }

    /// <summary>
    /// Deterministic synchronization point: returns once PostgreSQL reports the
    /// submission waiting on a lock, rather than relying on a sleep. Fails if the
    /// submission finishes without ever waiting (the pre-fix behaviour).
    /// </summary>
    private static async Task WaitUntilBlockedOnALockAsync(DbContextOptions<ApplicationDbContext> options, Task submission)
    {
        await using var probe = new ApplicationDbContext(options);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            Assert.False(submission.IsCompleted, "The submission completed without waiting for the project-row lock.");

            var waiting = await probe.Database
                .SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity " +
                    "WHERE datname = current_database() AND wait_event_type = 'Lock'")
                .SingleAsync();
            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("The submission never waited on a lock.");
    }

    /// <summary>
    /// A predecessor correction committed by a gate holder must be seen by a
    /// waiting successor correction, which was valid only against the old state.
    /// Validating before the gate (the pre-D-1b order) would have published it.
    /// </summary>
    private static async Task ValidationRunsAgainstStateCommittedBehindTheGateAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-1b gate revalidation", _ => { },
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(12)));

        await using var reader = new ApplicationDbContext(options);
        var input = await EditorInputAsync(reader, projectId, StageCodes.IPA, Jan(11));

        await using var holder = new ApplicationDbContext(options);
        await using var hold = await holder.Database.BeginTransactionAsync();
        await PlanRevisionConsistency.LockProjectAsync(holder, projectId, default);
        var fs = await holder.ProjectStages.SingleAsync(stage => stage.ProjectId == projectId && stage.StageCode == StageCodes.FS);
        fs.CompletedOn = Jan(13);
        await holder.SaveChangesAsync();

        await using var contender = new ApplicationDbContext(options);
        var update = Service(contender).UpdateAsync(input, TestActors.Hod(Actor), Actor);
        await Task.Delay(200);
        Assert.False(update.IsCompleted);
        await hold.CommitAsync();

        await Assert.ThrowsAsync<StageActualsValidationException>(() => update.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(Jan(12), await StartOfAsync(options, projectId, StageCodes.IPA));
        Assert.Equal(0, await HistoryCountAsync(options, projectId));
    }

    /// <summary>
    /// A writer that does not take the project gate (as ARPP synchronization does
    /// not) changes a stage row. The correction's FOR UPDATE load waits for it and
    /// then sees the new row, so the stale editor is refused instead of silently
    /// overwriting the committed change.
    /// </summary>
    private static async Task RowLockStopsAWriterOutsideTheGateFromBeingOverwrittenAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-1b row lock", _ => { },
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(12)));

        await using var reader = new ApplicationDbContext(options);
        var input = await EditorInputAsync(reader, projectId, StageCodes.IPA, Jan(13));

        await using var writer = new ApplicationDbContext(options);
        await using var write = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ProjectStages\" SET \"ActualStart\" = {Jan(14)} WHERE \"ProjectId\" = {projectId} AND \"StageCode\" = {StageCodes.IPA}");

        await using var contender = new ApplicationDbContext(options);
        var update = Service(contender).UpdateAsync(input, TestActors.Hod(Actor), Actor);
        await Task.Delay(200);
        Assert.False(update.IsCompleted);
        await write.CommitAsync();

        var stale = await Assert.ThrowsAsync<StageActualsStaleException>(() => update.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Contains(StageCodes.IPA, stale.StageCodes);
        Assert.Equal(Jan(14), await StartOfAsync(options, projectId, StageCodes.IPA));
        Assert.Equal(0, await HistoryCountAsync(options, projectId));
    }

    /// <summary>
    /// The stage update and its mandatory history commit together: a failure while
    /// saving history leaves no published date on PostgreSQL.
    /// </summary>
    private static async Task HistoryFailureRollsBackTheCorrectionAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-1b rollback", _ => { },
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(12)));

        var failing = new DbContextOptionsBuilder<ApplicationDbContext>(options)
            .AddInterceptors(new FailStageHistoryInterceptor())
            .Options;
        await using var db = new ApplicationDbContext(failing);
        var input = await EditorInputAsync(db, projectId, StageCodes.IPA, Jan(11));

        await Assert.ThrowsAsync<DbUpdateException>(() => Service(db).UpdateAsync(input, TestActors.Hod(Actor), Actor));

        Assert.Equal(Jan(12), await StartOfAsync(options, projectId, StageCodes.IPA));
        Assert.Equal(0, await HistoryCountAsync(options, projectId));
    }

    // SECTION: Helpers
    private static StageActualsUpdateService Service(ApplicationDbContext db) =>
        new(db, FakeClock.ForIstDate(Today), new RecordingAudit(), NullLogger<StageActualsUpdateService>.Instance);

    private static async Task<int> SeedProjectAsync(
        DbContextOptions<ApplicationDbContext> options,
        string name,
        Action<Project> configure,
        params ProjectStage[] stages)
    {
        await using var db = new ApplicationDbContext(options);
        var project = new Project
        {
            Name = name,
            CreatedByUserId = Actor,
            WorkflowVersion = ProcurementWorkflow.VersionV1,
            RowVersion = Array.Empty<byte>()
        };
        configure(project);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        foreach (var stage in stages)
        {
            stage.ProjectId = project.Id;
        }

        db.ProjectStages.AddRange(stages);
        await db.SaveChangesAsync();
        return project.Id;
    }

    // The post the editor would send for one row, with the row's token as rendered now.
    private static async Task<ActualsEditInput> EditorInputAsync(
        ApplicationDbContext db,
        int projectId,
        string stageCode,
        DateOnly actualStart)
    {
        var stage = await db.ProjectStages.AsNoTracking()
            .SingleAsync(item => item.ProjectId == projectId && item.StageCode == stageCode);
        return new ActualsEditInput
        {
            ProjectId = projectId,
            BaselineVersion = ActualStageToken.BaselineVersion,
            Rows = new List<ActualsEditRowInput>
            {
                new() { StageCode = stageCode, ActualStart = actualStart, StageToken = ActualStageToken.Compute(stage) }
            }
        };
    }

    private static async Task<DateOnly?> StartOfAsync(
        DbContextOptions<ApplicationDbContext> options,
        int projectId,
        string stageCode)
    {
        await using var db = new ApplicationDbContext(options);
        return (await db.ProjectStages.AsNoTracking()
            .SingleAsync(stage => stage.ProjectId == projectId && stage.StageCode == stageCode)).ActualStart;
    }

    private static async Task<int> HistoryCountAsync(DbContextOptions<ApplicationDbContext> options, int projectId)
    {
        await using var db = new ApplicationDbContext(options);
        return await db.StageChangeLogs.CountAsync(log => log.ProjectId == projectId);
    }

    private static ProjectStage InProgress(string code, DateOnly start) =>
        new() { StageCode = code, Status = StageStatus.InProgress, ActualStart = start };

    private static ProjectStage Completed(string code, DateOnly start, DateOnly completed) =>
        new() { StageCode = code, Status = StageStatus.Completed, ActualStart = start, CompletedOn = completed };

    private static DateOnly Jan(int day) => new(2026, 1, day);

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
}
