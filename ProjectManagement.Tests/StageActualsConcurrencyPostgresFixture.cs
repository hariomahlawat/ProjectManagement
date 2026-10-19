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
    private static readonly DateOnly Today = new(2026, 3, 31);

    internal static async Task RunAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using (var seed = new ApplicationDbContext(options))
        {
            seed.Users.Add(new ApplicationUser { Id = Actor, UserName = Actor });
            await seed.SaveChangesAsync();
        }

        await ValidationRunsAgainstStateCommittedBehindTheGateAsync(options);
        await RowLockStopsAWriterOutsideTheGateFromBeingOverwrittenAsync(options);
        await HistoryFailureRollsBackTheCorrectionAsync(options);
    }

    /// <summary>
    /// A predecessor correction committed by a gate holder must be seen by a
    /// waiting successor correction, which was valid only against the old state.
    /// Validating before the gate (the pre-D-1b order) would have published it.
    /// </summary>
    private static async Task ValidationRunsAgainstStateCommittedBehindTheGateAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        var projectId = await SeedProjectAsync(options, "D-1b gate revalidation",
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
        var update = Service(contender).UpdateAsync(input, Actor, Actor);
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
        var projectId = await SeedProjectAsync(options, "D-1b row lock",
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(12)));

        await using var reader = new ApplicationDbContext(options);
        var input = await EditorInputAsync(reader, projectId, StageCodes.IPA, Jan(13));

        await using var writer = new ApplicationDbContext(options);
        await using var write = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ProjectStages\" SET \"ActualStart\" = {Jan(14)} WHERE \"ProjectId\" = {projectId} AND \"StageCode\" = {StageCodes.IPA}");

        await using var contender = new ApplicationDbContext(options);
        var update = Service(contender).UpdateAsync(input, Actor, Actor);
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
        var projectId = await SeedProjectAsync(options, "D-1b rollback",
            Completed(StageCodes.FS, Jan(1), Jan(10)),
            InProgress(StageCodes.IPA, Jan(12)));

        var failing = new DbContextOptionsBuilder<ApplicationDbContext>(options)
            .AddInterceptors(new FailStageHistoryInterceptor())
            .Options;
        await using var db = new ApplicationDbContext(failing);
        var input = await EditorInputAsync(db, projectId, StageCodes.IPA, Jan(11));

        await Assert.ThrowsAsync<DbUpdateException>(() => Service(db).UpdateAsync(input, Actor, Actor));

        Assert.Equal(Jan(12), await StartOfAsync(options, projectId, StageCodes.IPA));
        Assert.Equal(0, await HistoryCountAsync(options, projectId));
    }

    // SECTION: Helpers
    private static StageActualsUpdateService Service(ApplicationDbContext db) =>
        new(db, FakeClock.ForIstDate(Today), new RecordingAudit(), NullLogger<StageActualsUpdateService>.Instance);

    private static async Task<int> SeedProjectAsync(
        DbContextOptions<ApplicationDbContext> options,
        string name,
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
