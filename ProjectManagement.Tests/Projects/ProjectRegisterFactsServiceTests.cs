using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Projects;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Projects;

// The register's facts come from OngoingProjectsReadService, so these run the real service on a
// small data set read on 22 Oct 2026 (IST), in the default workflow's stage order.
public sealed class ProjectRegisterFactsServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);
    private static readonly DateOnly LongAgo = new(2026, 1, 1);

    [Fact]
    public async Task InProgressPastItsPdc_IsLate()
    {
        await using var db = NewDb();
        AddProject(db, 1);
        CompleteBefore(db, 1, StageCodes.BID, LongAgo);
        AddStage(db, 1, StageCodes.BID, StageStatus.InProgress, start: new DateOnly(2026, 8, 1), due: new DateOnly(2026, 9, 1));
        await db.SaveChangesAsync();

        var facts = (await CreateService(db).GetActiveFactsAsync(CancellationToken.None))[1];

        Assert.Equal(StageCodes.BID, facts.CurrentStageCode);
        Assert.Equal(ProjectRegisterStageState.InProgress, facts.CurrentStageState);
        Assert.Equal(new DateOnly(2026, 8, 1), facts.StageAgeFrom);
        Assert.Equal(82, facts.DaysInStage);
        Assert.Equal(31, facts.PlannedDays);
        Assert.Equal(51, facts.DaysLate);
        Assert.Null(facts.DaysLeft);
        Assert.Equal(ProjectRegisterPlanStatus.Late, facts.PlanStatus);
        Assert.True(facts.IsLate);
        Assert.False(facts.IsBlocked);
    }

    [Fact]
    public async Task InProgressBeforeItsPdc_IsOnPlanWithDaysLeft()
    {
        await using var db = NewDb();
        AddProject(db, 2);
        CompleteBefore(db, 2, StageCodes.BID, LongAgo);
        AddStage(db, 2, StageCodes.BID, StageStatus.InProgress, start: new DateOnly(2026, 10, 1), due: new DateOnly(2026, 11, 1));
        await db.SaveChangesAsync();

        var facts = (await CreateService(db).GetActiveFactsAsync(CancellationToken.None))[2];

        Assert.Equal(21, facts.DaysInStage);
        Assert.Equal(31, facts.PlannedDays);
        Assert.Null(facts.DaysLate);
        Assert.Equal(10, facts.DaysLeft);
        Assert.Equal(ProjectRegisterPlanStatus.OnPlan, facts.PlanStatus);
        Assert.False(facts.IsLate);
    }

    [Fact]
    public async Task NextStageNotStarted_WaitsFromThePredecessorsCompletion()
    {
        await using var db = NewDb();
        AddProject(db, 3);
        CompleteBefore(db, 3, StageCodes.SO, LongAgo);
        AddStage(db, 3, StageCodes.SO, StageStatus.Completed, completed: new DateOnly(2026, 9, 1));
        AddStage(db, 3, StageCodes.DEVP, StageStatus.NotStarted, due: new DateOnly(2026, 9, 15));
        await db.SaveChangesAsync();

        var facts = (await CreateService(db).GetActiveFactsAsync(CancellationToken.None))[3];

        Assert.Equal(StageCodes.DEVP, facts.CurrentStageCode);
        Assert.Equal(ProjectRegisterStageState.NotStarted, facts.CurrentStageState);
        Assert.Equal(new DateOnly(2026, 9, 1), facts.StageAgeFrom);
        Assert.Equal(51, facts.DaysInStage);
        Assert.Equal(37, facts.DaysLate);
        Assert.Equal(ProjectRegisterStageMarkState.Waiting, facts.Stages[facts.CurrentStageIndex].State);
        Assert.Equal(ProjectRegisterStageMarkState.Done, facts.Stages[facts.CurrentStageIndex - 1].State);
    }

    [Fact]
    public async Task BlockedStage_IsCurrentAndBlockedInTheTracker()
    {
        await using var db = NewDb();
        AddProject(db, 4);
        CompleteBefore(db, 4, StageCodes.SOW, LongAgo);
        AddStage(db, 4, StageCodes.SOW, StageStatus.Blocked, start: new DateOnly(2026, 9, 12));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var facts = (await service.GetActiveFactsAsync(CancellationToken.None))[4];
        var ongoingRow = (await new OngoingProjectsReadService(db, new WorkflowStageMetadataProvider(), FakeClock.ForIstDate(Today))
            .GetAsync(Array.Empty<int>(), null, null, null, Array.Empty<string>(), null, CancellationToken.None))
            .Single(row => row.ProjectId == 4);

        Assert.Equal(StageCodes.SOW, facts.CurrentStageCode);
        Assert.True(facts.IsBlocked);
        // The register takes the shared stage age as it is (PresentStageHelper decides it) and
        // counts back from it, so the two never disagree.
        Assert.Equal(ongoingRow.DaysInCurrentStage, facts.DaysInStage);
        Assert.Equal(Today.AddDays(-facts.DaysInStage!.Value), facts.StageAgeFrom);
        var sow = facts.Stages.ToList().FindIndex(stage => stage.Code == StageCodes.SOW);
        Assert.Equal(sow, facts.CurrentStageIndex);
        Assert.All(facts.Stages.Take(sow), stage => Assert.Equal(ProjectRegisterStageMarkState.Done, stage.State));
        Assert.Equal(ProjectRegisterStageMarkState.Blocked, facts.Stages[sow].State);
        Assert.All(facts.Stages.Skip(sow + 1), stage => Assert.Equal(ProjectRegisterStageMarkState.Upcoming, stage.State));
    }

    [Fact]
    public async Task NoPdcAndNoRecordedStart_CountsFromTheLatestCompletion()
    {
        await using var db = NewDb();
        AddProject(db, 5);
        AddStage(db, 5, StageCodes.FS, StageStatus.Completed, completed: new DateOnly(2026, 10, 12));
        AddStage(db, 5, StageCodes.IPA, StageStatus.InProgress);
        await db.SaveChangesAsync();

        var facts = (await CreateService(db).GetActiveFactsAsync(CancellationToken.None))[5];

        Assert.Equal(StageCodes.IPA, facts.CurrentStageCode);
        Assert.Equal(new DateOnly(2026, 10, 12), facts.StageAgeFrom);
        Assert.Equal(10, facts.DaysInStage);
        Assert.Null(facts.StagePdc);
        Assert.Null(facts.PlannedDays);
        Assert.Equal(ProjectRegisterPlanStatus.NoPdc, facts.PlanStatus);
        Assert.False(facts.IsLate);
    }

    [Fact]
    public async Task EveryStageDoneButProjectOpen_KeepsTerminalMarksAndIsFinished()
    {
        await using var db = NewDb();
        AddProject(db, 12);
        var codes = ProcurementWorkflow.StageCodesFor(null);
        foreach (var code in codes.Take(codes.Length - 1))
        {
            AddStage(db, 12, code, StageStatus.Completed, completed: new DateOnly(2026, 10, 1));
        }

        AddStage(db, 12, codes[^1], StageStatus.Skipped);
        await db.SaveChangesAsync();

        var facts = (await CreateService(db).GetActiveFactsAsync(CancellationToken.None))[12];

        Assert.Equal(codes.Length - 1, facts.CurrentStageIndex);
        Assert.Equal(ProjectRegisterStageState.Finished, facts.CurrentStageState);
        Assert.Equal(ProjectRegisterStageMarkState.Skipped, facts.Stages[^1].State);
        Assert.All(facts.Stages.Take(codes.Length - 1), stage => Assert.Equal(ProjectRegisterStageMarkState.Done, stage.State));
        Assert.DoesNotContain(facts.Stages, stage => stage.State == ProjectRegisterStageMarkState.Waiting);
    }

    [Fact]
    public async Task LastUpdate_IsTheLatestRemarkOrContentEditElseCreation()
    {
        await using var db = NewDb();
        AddProject(db, 6);
        AddProject(db, 7, contentUpdatedUtc: new DateTimeOffset(2026, 10, 20, 6, 0, 0, TimeSpan.Zero));
        AddProject(db, 8);
        db.Remarks.AddRange(
            Remark(6, createdUtc: new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc), editedUtc: new DateTime(2026, 10, 17, 6, 0, 0, DateTimeKind.Utc)),
            Remark(8, createdUtc: new DateTime(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc), deleted: true));
        await db.SaveChangesAsync();

        var facts = await CreateService(db).GetActiveFactsAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 17), facts[6].LastUpdatedOn);
        Assert.Equal(5, facts[6].DaysSinceUpdate);
        Assert.False(facts[6].IsStale);
        Assert.Equal(new DateOnly(2026, 10, 20), facts[7].LastUpdatedOn);
        // A deleted remark is not an update: project 8 was last touched when it was created.
        Assert.Equal(LongAgo, facts[8].LastUpdatedOn);
        Assert.True(facts[8].IsStale);
    }

    [Fact]
    public async Task OnlyActiveUnarchivedProjectsHaveFacts()
    {
        await using var db = NewDb();
        AddProject(db, 9);
        AddProject(db, 10, archived: true);
        AddProject(db, 11, lifecycle: ProjectLifecycleStatus.Completed);
        await db.SaveChangesAsync();

        var facts = await CreateService(db).GetActiveFactsAsync(CancellationToken.None);

        Assert.Equal(new[] { 9 }, facts.Keys);
    }

    private static ProjectRegisterFactsService CreateService(ApplicationDbContext db)
    {
        var clock = FakeClock.ForIstDate(Today);
        return new ProjectRegisterFactsService(
            db,
            new OngoingProjectsReadService(db, new WorkflowStageMetadataProvider(), clock),
            clock);
    }

    private static void AddProject(
        ApplicationDbContext db,
        int id,
        bool archived = false,
        ProjectLifecycleStatus lifecycle = ProjectLifecycleStatus.Active,
        DateTimeOffset? contentUpdatedUtc = null)
        => db.Projects.Add(new Project
        {
            Id = id,
            Name = $"Project {id}",
            CreatedByUserId = "seed",
            CreatedAt = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc),
            ContentUpdatedAtUtc = contentUpdatedUtc,
            LifecycleStatus = lifecycle,
            IsArchived = archived
        });

    // Every workflow stage before stageCode, completed on the given date.
    private static void CompleteBefore(ApplicationDbContext db, int projectId, string stageCode, DateOnly completedOn)
    {
        foreach (var code in ProcurementWorkflow.StageCodesFor(null).TakeWhile(code => code != stageCode))
        {
            AddStage(db, projectId, code, StageStatus.Completed, completed: completedOn);
        }
    }

    private static void AddStage(
        ApplicationDbContext db,
        int projectId,
        string code,
        StageStatus status,
        DateOnly? start = null,
        DateOnly? completed = null,
        DateOnly? due = null)
        => db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = projectId,
            StageCode = code,
            SortOrder = Array.IndexOf(ProcurementWorkflow.StageCodesFor(null), code),
            Status = status,
            ActualStart = start,
            CompletedOn = completed,
            PlannedDue = due
        });

    private static Remark Remark(int projectId, DateTime createdUtc, DateTime? editedUtc = null, bool deleted = false)
        => new()
        {
            ProjectId = projectId,
            AuthorUserId = "po",
            Type = RemarkType.Internal,
            Body = "Update",
            CreatedAtUtc = createdUtc,
            LastEditedAtUtc = editedUtc,
            IsDeleted = deleted
        };

    private static ApplicationDbContext NewDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
