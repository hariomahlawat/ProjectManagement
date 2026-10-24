using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Contracts.Stages;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Approvals;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Stages;

// SECTION: Project Officer multi-stage requests without procurement amounts
// The production case (project 207): Benchmarking in progress with its PDC set, no Benchmark
// cost recorded. A Project Officer sends one or several stage updates, in one submission or
// one after another, and the HoD approves them in workflow order. No procurement amount is
// recorded at any point; only the PO's completion date is required.
public sealed class ProjectOfficerMultiStageRequestTests
{
    private const int ProjectId = 207;
    private const string ProjectOfficer = "po-1";
    private const string Hod = "hod-1";
    private static readonly DateOnly Today = new(2026, 10, 24);
    private static readonly DateOnly FirstDay = new(2026, 9, 1);

    private static DateOnly BmStart => FirstDay.AddDays(StageIndex(StageCodes.BM));

    [Fact]
    public async Task OneSubmission_CompletesBenchmarkingCobAndPnc_ThenHoDApprovesEachInOrder()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 1)),
            Complete(StageCodes.COB, new DateOnly(2026, 10, 5), start: new DateOnly(2026, 10, 2)),
            Complete(StageCodes.PNC, new DateOnly(2026, 10, 10), start: new DateOnly(2026, 10, 6)));

        Assert.Equal(BatchStageRequestOutcome.Success, result.Outcome);
        Assert.Equal(new[] { StageCodes.BM, StageCodes.COB, StageCodes.PNC }, result.Items.Select(item => item.StageCode));
        Assert.Equal(3, await fixture.Db.StageChangeRequests.CountAsync(request => request.DecisionStatus == "Pending"));

        foreach (var code in new[] { StageCodes.BM, StageCodes.COB, StageCodes.PNC })
        {
            var decision = await fixture.ApproveAsync(code);
            Assert.True(decision.Outcome == StageDecisionOutcome.Success, $"{code}: {decision.Error}");
        }

        await fixture.AssertCompletedAsync(StageCodes.BM, BmStart, new DateOnly(2026, 10, 1));
        await fixture.AssertCompletedAsync(StageCodes.COB, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 5));
        await fixture.AssertCompletedAsync(StageCodes.PNC, new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 10));
        await fixture.AssertNoProcurementAmountsAsync();
    }

    [Fact]
    public async Task OneSubmission_CompletesEveryProcurementStageThroughSupplyOrder_WithoutAnyAmount()
    {
        await using var fixture = await Fixture.CreateAsync();
        var codes = new[] { StageCodes.BM, StageCodes.COB, StageCodes.PNC, StageCodes.EAS, StageCodes.SO };

        var items = codes.Select((code, index) => Complete(
            code,
            new DateOnly(2026, 10, 1).AddDays(index * 3),
            start: code == StageCodes.BM ? null : new DateOnly(2026, 10, 1).AddDays(index * 3 - 2))).ToArray();
        var result = await fixture.SubmitAsync(items);

        Assert.Equal(BatchStageRequestOutcome.Success, result.Outcome);
        foreach (var code in codes)
        {
            var decision = await fixture.ApproveAsync(code);
            Assert.True(decision.Outcome == StageDecisionOutcome.Success, $"{code}: {decision.Error}");
            var stage = await fixture.StageAsync(code);
            Assert.Equal(StageStatus.Completed, stage.Status);
            Assert.False(stage.RequiresBackfill);
        }

        await fixture.AssertNoProcurementAmountsAsync();
    }

    [Fact]
    public async Task OneSubmission_MixesACompletionAndAStart()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 1)),
            Start(StageCodes.COB, new DateOnly(2026, 10, 2)));

        Assert.Equal(BatchStageRequestOutcome.Success, result.Outcome);
        Assert.Equal(StageDecisionOutcome.Success, (await fixture.ApproveAsync(StageCodes.BM)).Outcome);
        Assert.Equal(StageDecisionOutcome.Success, (await fixture.ApproveAsync(StageCodes.COB)).Outcome);

        var cob = await fixture.StageAsync(StageCodes.COB);
        Assert.Equal(StageStatus.InProgress, cob.Status);
        Assert.Equal(new DateOnly(2026, 10, 2), cob.ActualStart);
    }

    [Fact]
    public async Task SeparateSubmissions_LaterStageBuildsOnAnEarlierPendingRequest()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.SubmitAsync(Complete(StageCodes.BM, new DateOnly(2026, 10, 1)));
        var second = await fixture.SubmitAsync(Start(StageCodes.COB, new DateOnly(2026, 10, 2)));
        var third = await fixture.SubmitAsync(Complete(StageCodes.PNC, new DateOnly(2026, 10, 12), start: new DateOnly(2026, 10, 8)));

        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, third.Outcome);
        Assert.Contains("Commercial Bid Opening", string.Join(" ", third.Items.SelectMany(item => item.Result.Errors)));

        Assert.Equal(BatchStageRequestOutcome.Success, first.Outcome);
        Assert.Equal(BatchStageRequestOutcome.Success, second.Outcome);
        Assert.Equal(2, await fixture.Db.StageChangeRequests.CountAsync(request => request.DecisionStatus == "Pending"));
    }

    [Fact]
    public async Task SeparateSubmissions_ARevisionSupersedesTheEarlierRequestForTheSameStage()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.SubmitAsync(Complete(StageCodes.BM, new DateOnly(2026, 10, 1)));
        await fixture.SubmitAsync(Start(StageCodes.COB, new DateOnly(2026, 10, 2)));
        var revised = await fixture.SubmitAsync(Complete(StageCodes.COB, new DateOnly(2026, 10, 6), start: new DateOnly(2026, 10, 2)));

        Assert.Equal(BatchStageRequestOutcome.Success, revised.Outcome);
        var cobRequests = await fixture.Db.StageChangeRequests
            .Where(request => request.StageCode == StageCodes.COB)
            .OrderBy(request => request.Id)
            .ToListAsync();
        Assert.Equal(new[] { "Superseded", "Pending" }, cobRequests.Select(request => request.DecisionStatus));
        Assert.Equal(nameof(StageStatus.Completed), cobRequests[1].RequestedStatus);
        Assert.Equal(
            "Pending",
            (await fixture.Db.StageChangeRequests.SingleAsync(request => request.StageCode == StageCodes.BM)).DecisionStatus);
    }

    [Fact]
    public async Task SeparateSubmissions_RevisingBenchmarkingDateReplacesTheEarlierProposal()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.SubmitAsync(Complete(StageCodes.BM, new DateOnly(2026, 10, 1)));
        var revised = await fixture.SubmitAsync(Complete(StageCodes.BM, new DateOnly(2026, 10, 3)));

        Assert.Equal(BatchStageRequestOutcome.Success, revised.Outcome);
        var pending = await fixture.Db.StageChangeRequests.SingleAsync(request => request.DecisionStatus == "Pending");
        Assert.Equal(new DateOnly(2026, 10, 3), pending.RequestedDate);
        Assert.Equal(StageDecisionOutcome.Success, (await fixture.ApproveAsync(StageCodes.BM)).Outcome);
        await fixture.AssertCompletedAsync(StageCodes.BM, BmStart, new DateOnly(2026, 10, 3));
    }

    [Fact]
    public async Task HoDCannotApproveALaterStageBeforeItsPredecessor()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 1)),
            Complete(StageCodes.COB, new DateOnly(2026, 10, 5), start: new DateOnly(2026, 10, 2)));

        var cobAssessment = await fixture.AssessAsync(StageCodes.COB);
        Assert.Equal(ApprovalReadiness.Waiting, cobAssessment.Readiness);
        Assert.Equal(ApprovalReadiness.Ready, (await fixture.AssessAsync(StageCodes.BM)).Readiness);

        var early = await fixture.ApproveAsync(StageCodes.COB);
        Assert.NotEqual(StageDecisionOutcome.Success, early.Outcome);
        Assert.Equal(StageStatus.NotStarted, (await fixture.StageAsync(StageCodes.COB)).Status);

        Assert.Equal(StageDecisionOutcome.Success, (await fixture.ApproveAsync(StageCodes.BM)).Outcome);
        Assert.Equal(StageDecisionOutcome.Success, (await fixture.ApproveAsync(StageCodes.COB)).Outcome);
    }

    [Fact]
    public async Task ApprovalChecks_NeverAskForAProcurementAmount()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(Complete(StageCodes.BM, new DateOnly(2026, 10, 1)));

        var assessment = await fixture.AssessAsync(StageCodes.BM);

        Assert.Equal(ApprovalReadiness.Ready, assessment.Readiness);
        Assert.DoesNotContain(assessment.Checks, check => check.Label.Contains("information", StringComparison.OrdinalIgnoreCase));
        Assert.All(assessment.Checks, check => Assert.NotEqual(ApprovalCheckState.Blocked, check.State));
    }

    [Fact]
    public async Task Batch_WithAMissingCompletionDate_IsRejectedAndSavesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 1)),
            Complete(StageCodes.COB, completedOn: null, start: new DateOnly(2026, 10, 2)));

        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, result.Outcome);
        var failed = Assert.Single(result.Items, item => item.Result.Outcome == StageRequestOutcome.ValidationFailed);
        Assert.Equal(StageCodes.COB, failed.StageCode);
        Assert.Contains("Completion date is required.", failed.Result.Errors);
        Assert.False(await fixture.Db.StageChangeRequests.AnyAsync());
    }

    [Fact]
    public async Task Batch_WithACompletionBeforeThePredecessorCompletes_IsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 5)),
            Complete(StageCodes.COB, new DateOnly(2026, 10, 4), start: new DateOnly(2026, 10, 2)));

        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(
            result.Items.Single(item => item.StageCode == StageCodes.COB).Result.Errors,
            error => error.Contains("on or after 05 Oct 2026", StringComparison.Ordinal));
        Assert.False(await fixture.Db.StageChangeRequests.AnyAsync());
    }

    [Fact]
    public async Task Batch_SkippingOverAnOpenPredecessor_IsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SubmitAsync(Complete(StageCodes.PNC, new DateOnly(2026, 10, 10), start: new DateOnly(2026, 10, 6)));

        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, result.Outcome);
        var errors = string.Join(" ", result.Items.SelectMany(item => item.Result.Errors));
        Assert.Contains("Benchmarking", errors);
        Assert.Contains("Commercial Bid Opening", errors);
    }

    [Fact]
    public async Task Batch_WithAFutureCompletionDate_IsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SubmitAsync(Complete(StageCodes.BM, Today.AddDays(1)));

        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("Date cannot be in the future.", result.Items.SelectMany(item => item.Result.Errors));
    }

    [Fact]
    public async Task Batch_NamingTheSameStageTwice_IsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 1)),
            Complete(StageCodes.BM, new DateOnly(2026, 10, 2)));

        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(result.Errors, error => error.Contains("Include each stage once", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Batch_BlockOrSkipNeedsANote()
    {
        await using var fixture = await Fixture.CreateAsync();

        var withoutNote = await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 1)),
            new StageChangeRequestItemInput { StageCode = StageCodes.EAS, RequestedStatus = nameof(StageStatus.Blocked) });
        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, withoutNote.Outcome);

        var withNote = await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 1)),
            new StageChangeRequestItemInput
            {
                StageCode = StageCodes.EAS,
                RequestedStatus = nameof(StageStatus.Blocked),
                Note = "Awaiting EAS board date"
            });
        Assert.Equal(BatchStageRequestOutcome.Success, withNote.Outcome);
    }

    [Fact]
    public async Task Batch_FromAnotherProjectOfficer_IsRefused()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SubmitAsync("po-2", Complete(StageCodes.BM, new DateOnly(2026, 10, 1)));

        Assert.Equal(BatchStageRequestOutcome.NotProjectOfficer, result.Outcome);
        Assert.False(await fixture.Db.StageChangeRequests.AnyAsync());
    }

    [Fact]
    public async Task HoDCompletesDirectlyWithoutADateOrAmount_AndTheStageAwaitsItsDate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var workflow = new ProjectStageWorkflowPolicy(fixture.Db, new WorkflowStageMetadataProvider());
        var service = new StageDirectApplyService(
            fixture.Db,
            fixture.Clock,
            new StageValidationService(fixture.Db, fixture.Clock, workflow),
            new StageRulesService(fixture.Db, workflow),
            workflow);

        var result = await service.ApplyAsync(
            projectId: ProjectId,
            stageCode: StageCodes.BM,
            status: nameof(StageStatus.Completed),
            date: null,
            note: "Completion date to follow",
            hodUserId: Hod,
            forceBackfillPredecessors: false,
            CancellationToken.None);

        Assert.Equal(nameof(StageStatus.Completed), result.UpdatedStatus);
        var bm = await fixture.StageAsync(StageCodes.BM);
        Assert.Equal(StageStatus.Completed, bm.Status);
        Assert.Null(bm.CompletedOn);
        Assert.True(bm.RequiresBackfill);
        await fixture.AssertNoProcurementAmountsAsync();
    }

    [Fact]
    public async Task HoDRejectsOneStageOfABatch_TheOthersStand_AndThePoResubmitsIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 1)),
            Start(StageCodes.COB, new DateOnly(2026, 10, 2)));

        Assert.Equal(StageDecisionOutcome.Success, (await fixture.ApproveAsync(StageCodes.BM)).Outcome);
        Assert.Equal(
            StageDecisionOutcome.Success,
            (await fixture.DecideAsync(StageCodes.COB, StageDecisionAction.Reject, "Start date is wrong")).Outcome);

        Assert.Equal(StageStatus.Completed, (await fixture.StageAsync(StageCodes.BM)).Status);
        Assert.Equal(StageStatus.NotStarted, (await fixture.StageAsync(StageCodes.COB)).Status);
        Assert.Equal(
            "Rejected",
            (await fixture.Db.StageChangeRequests.SingleAsync(request => request.StageCode == StageCodes.COB)).DecisionStatus);

        var resubmitted = await fixture.SubmitAsync(Start(StageCodes.COB, new DateOnly(2026, 10, 3)));
        Assert.Equal(BatchStageRequestOutcome.Success, resubmitted.Outcome);
        Assert.Equal(StageDecisionOutcome.Success, (await fixture.ApproveAsync(StageCodes.COB)).Outcome);
        Assert.Equal(new DateOnly(2026, 10, 3), (await fixture.StageAsync(StageCodes.COB)).ActualStart);
    }

    [Fact]
    public async Task ARequestDecidedOnce_CannotBeDecidedAgain()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SubmitAsync(Complete(StageCodes.BM, new DateOnly(2026, 10, 1)));
        var requestId = (await fixture.Db.StageChangeRequests.SingleAsync()).Id;

        Assert.Equal(StageDecisionOutcome.Success, (await fixture.ApproveAsync(StageCodes.BM)).Outcome);
        var again = await fixture.DecideByIdAsync(requestId, StageDecisionAction.Approve);

        Assert.Equal(StageDecisionOutcome.AlreadyDecided, again.Outcome);
    }

    [Fact]
    public async Task Batch_CompletesANotStartedStageDirectly_WithoutAStartDate()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 1)),
            Complete(StageCodes.COB, new DateOnly(2026, 10, 4)));

        Assert.Equal(BatchStageRequestOutcome.Success, result.Outcome);
        Assert.Equal(StageDecisionOutcome.Success, (await fixture.ApproveAsync(StageCodes.BM)).Outcome);
        Assert.Equal(StageDecisionOutcome.Success, (await fixture.ApproveAsync(StageCodes.COB)).Outcome);
        var cob = await fixture.StageAsync(StageCodes.COB);
        Assert.Equal(StageStatus.Completed, cob.Status);
        Assert.Equal(new DateOnly(2026, 10, 4), cob.CompletedOn);
    }

    [Fact]
    public async Task Batch_WithAStartDateAfterTheCompletionDate_IsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 1)),
            Complete(StageCodes.COB, new DateOnly(2026, 10, 3), start: new DateOnly(2026, 10, 5)));

        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(
            result.Items.Single(item => item.StageCode == StageCodes.COB).Result.Errors,
            error => error.Contains("before its proposed start date", StringComparison.Ordinal)
                || error.Contains("before the selected start date", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Batch_StartingBeforeThePredecessorCompletes_IsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 5)),
            Start(StageCodes.COB, new DateOnly(2026, 10, 3)));

        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(
            result.Items.Single(item => item.StageCode == StageCodes.COB).Result.Errors,
            error => error.Contains("on or after 05 Oct 2026", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReopeningACompletedStage_NeedsANote_AndKeepsItsRecordedStart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tec = await fixture.StageAsync(StageCodes.TEC);

        var withoutNote = await fixture.SubmitAsync(Start(StageCodes.TEC, tec.ActualStart!.Value));
        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, withoutNote.Outcome);

        var withNote = await fixture.SubmitAsync(new StageChangeRequestItemInput
        {
            StageCode = StageCodes.TEC,
            RequestedStatus = nameof(StageStatus.InProgress),
            RequestedDate = tec.ActualStart,
            Note = "Re-evaluation ordered"
        });
        Assert.Equal(BatchStageRequestOutcome.Success, withNote.Outcome);

        var movedStart = await fixture.SubmitAsync(new StageChangeRequestItemInput
        {
            StageCode = StageCodes.TEC,
            RequestedStatus = nameof(StageStatus.InProgress),
            RequestedDate = tec.ActualStart!.Value.AddDays(1),
            Note = "Re-evaluation ordered"
        });
        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, movedStart.Outcome);
    }

    [Fact]
    public async Task SkippingAStage_WorksForANotStartedStage_ButNotForOneThatHasStarted()
    {
        await using var fixture = await Fixture.CreateAsync();

        var skipEas = await fixture.SubmitAsync(new StageChangeRequestItemInput
        {
            StageCode = StageCodes.EAS,
            RequestedStatus = nameof(StageStatus.Skipped),
            Note = "EAS not required for this case"
        });
        Assert.Equal(BatchStageRequestOutcome.Success, skipEas.Outcome);

        var skipBm = await fixture.SubmitAsync(new StageChangeRequestItemInput
        {
            StageCode = StageCodes.BM,
            RequestedStatus = nameof(StageStatus.Skipped),
            Note = "Not needed"
        });
        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, skipBm.Outcome);
        Assert.Contains(
            skipBm.Items.SelectMany(item => item.Result.Errors),
            error => error.Contains("cannot be skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAmountCanStillBeRecordedBeforeOrAfterCompletion_AndChangesNothingInTheLifecycle()
    {
        await using var fixture = await Fixture.CreateAsync();
        var facts = new ProjectFactsService(fixture.Db, fixture.Clock, new RecordingAudit());

        await facts.UpsertBenchmarkCostAsync(ProjectId, 89_00_000m, ProjectOfficer);
        await fixture.SubmitAsync(
            Complete(StageCodes.BM, new DateOnly(2026, 10, 1)),
            Complete(StageCodes.COB, new DateOnly(2026, 10, 5), start: new DateOnly(2026, 10, 2)));
        Assert.Equal(StageDecisionOutcome.Success, (await fixture.ApproveAsync(StageCodes.BM)).Outcome);
        Assert.Equal(StageDecisionOutcome.Success, (await fixture.ApproveAsync(StageCodes.COB)).Outcome);
        await facts.UpsertL1CostAsync(ProjectId, 92_00_000m, ProjectOfficer);

        Assert.Equal(89_00_000m, (await fixture.Db.ProjectBenchmarkFacts.SingleAsync()).BenchmarkCost);
        Assert.Equal(92_00_000m, (await fixture.Db.ProjectCommercialFacts.SingleAsync()).L1Cost);
        await fixture.AssertCompletedAsync(StageCodes.BM, BmStart, new DateOnly(2026, 10, 1));
        await fixture.AssertCompletedAsync(StageCodes.COB, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 5));
    }

    // SECTION: Helpers
    private static StageChangeRequestItemInput Complete(string code, DateOnly? completedOn, DateOnly? start = null)
        => new()
        {
            StageCode = code,
            RequestedStatus = nameof(StageStatus.Completed),
            RequestedDate = completedOn,
            RequestedStartDate = start
        };

    private static StageChangeRequestItemInput Start(string code, DateOnly startedOn)
        => new()
        {
            StageCode = code,
            RequestedStatus = nameof(StageStatus.InProgress),
            RequestedDate = startedOn
        };

    private static int StageIndex(string code)
        => Array.IndexOf(ProcurementWorkflow.StageCodesFor(ProcurementWorkflow.VersionV2), code);

    private sealed class NullStageNotificationService : IStageNotificationService
    {
        public Task NotifyStageStatusChangedAsync(ProjectStage stage, Project project, StageStatus previousStatus, string actorUserId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, ApplicationDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public ApplicationDbContext Db { get; }

        public FakeClock Clock { get; } = FakeClock.ForIstDate(Today);

        // Workflow v2: FS to TEC completed one per day from 1 Sep 2026, Benchmarking in
        // progress since the day TEC completed with its PDC set, later stages not started.
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            foreach (var id in new[] { "seed", ProjectOfficer, "po-2", Hod })
            {
                db.Users.Add(new ApplicationUser { Id = id, UserName = id });
            }

            db.Projects.Add(new Project
            {
                Id = ProjectId,
                Name = "VR based Physiological Response",
                CreatedByUserId = "seed",
                LeadPoUserId = ProjectOfficer,
                WorkflowVersion = ProcurementWorkflow.VersionV2
            });

            var codes = ProcurementWorkflow.StageCodesFor(ProcurementWorkflow.VersionV2);
            var bmIndex = StageIndex(StageCodes.BM);
            for (var index = 0; index < codes.Length; index++)
            {
                var status = index < bmIndex ? StageStatus.Completed
                    : index == bmIndex ? StageStatus.InProgress
                    : StageStatus.NotStarted;
                db.ProjectStages.Add(new ProjectStage
                {
                    ProjectId = ProjectId,
                    StageCode = codes[index],
                    SortOrder = index + 1,
                    Status = status,
                    ActualStart = status == StageStatus.NotStarted ? null : FirstDay.AddDays(index),
                    CompletedOn = status == StageStatus.Completed ? FirstDay.AddDays(index + 1) : null,
                    PlannedDue = index == bmIndex ? new DateOnly(2026, 10, 17) : null
                });
            }

            await db.SaveChangesAsync();
            return new Fixture(connection, db);
        }

        public Task<BatchStageRequestResult> SubmitAsync(params StageChangeRequestItemInput[] items)
            => SubmitAsync(ProjectOfficer, items);

        public Task<BatchStageRequestResult> SubmitAsync(string userId, params StageChangeRequestItemInput[] items)
        {
            var workflow = new ProjectStageWorkflowPolicy(Db, new WorkflowStageMetadataProvider());
            var service = new StageRequestService(
                Db,
                Clock,
                new StageValidationService(Db, Clock, workflow),
                workflow,
                new ProjectFactsReadService(Db));
            return service.CreateBatchAsync(
                new BatchStageChangeRequestInput { ProjectId = ProjectId, Stages = items },
                userId);
        }

        public async Task<StageApprovalAssessment> AssessAsync(string code)
        {
            var request = await PendingAsync(code);
            var assessment = await SequenceService().AssessRequestAsync(request.Id);
            Assert.NotNull(assessment);
            return assessment!;
        }

        public Task<StageDecisionResult> ApproveAsync(string code)
            => DecideAsync(code, StageDecisionAction.Approve, null);

        public async Task<StageDecisionResult> DecideAsync(string code, StageDecisionAction action, string? note)
        {
            var request = await PendingAsync(code);
            var workflow = new ProjectStageWorkflowPolicy(Db, new WorkflowStageMetadataProvider());
            var progress = new StageProgressService(
                Db,
                Clock,
                new RecordingAudit(),
                new ProjectFactsReadService(Db),
                new NullStageNotificationService(),
                workflow);
            var decisions = new StageDecisionService(
                Db,
                Clock,
                progress,
                NullLogger<StageDecisionService>.Instance,
                workflow,
                sequenceService: SequenceService());
            var result = await decisions.DecideAsync(
                new StageDecisionInput(request.Id, action, note),
                Hod,
                isAdmin: false,
                isHoD: true);
            Db.ChangeTracker.Clear();
            return result;
        }

        public async Task<ProjectStage> StageAsync(string code)
            => await Db.ProjectStages.AsNoTracking().SingleAsync(stage => stage.ProjectId == ProjectId && stage.StageCode == code);

        public async Task AssertCompletedAsync(string code, DateOnly start, DateOnly completedOn)
        {
            var stage = await StageAsync(code);
            Assert.Equal(StageStatus.Completed, stage.Status);
            Assert.Equal(start, stage.ActualStart);
            Assert.Equal(completedOn, stage.CompletedOn);
            Assert.False(stage.RequiresBackfill);
        }

        public async Task AssertNoProcurementAmountsAsync()
        {
            Assert.False(await Db.ProjectAonFacts.AnyAsync());
            Assert.False(await Db.ProjectBenchmarkFacts.AnyAsync());
            Assert.False(await Db.ProjectCommercialFacts.AnyAsync());
            Assert.False(await Db.ProjectPncFacts.AnyAsync());
            Assert.False(await Db.ProjectSupplyOrderFacts.AnyAsync());
        }

        public async Task<StageDecisionResult> DecideByIdAsync(int requestId, StageDecisionAction action)
        {
            var workflow = new ProjectStageWorkflowPolicy(Db, new WorkflowStageMetadataProvider());
            var progress = new StageProgressService(
                Db, Clock, new RecordingAudit(), new ProjectFactsReadService(Db), new NullStageNotificationService(), workflow);
            var decisions = new StageDecisionService(
                Db, Clock, progress, NullLogger<StageDecisionService>.Instance, workflow, sequenceService: SequenceService());
            var result = await decisions.DecideAsync(new StageDecisionInput(requestId, action, null), Hod, isAdmin: false, isHoD: true);
            Db.ChangeTracker.Clear();
            return result;
        }

        private StageApprovalSequenceService SequenceService()
            => new(Db, new ProjectStageWorkflowPolicy(Db, new WorkflowStageMetadataProvider()), new ProjectFactsReadService(Db));

        private Task<StageChangeRequest> PendingAsync(string code)
            => Db.StageChangeRequests.AsNoTracking()
                .SingleAsync(request => request.StageCode == code && request.DecisionStatus == "Pending");

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
