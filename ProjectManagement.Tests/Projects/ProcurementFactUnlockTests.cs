using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Contracts.Stages;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Pages.Projects.Procurement;
using ProjectManagement.Services.Approvals;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.Projects;

// SECTION: Stage completion is independent of procurement figures
// Completion used to wait for the stage's figure (AoN, Benchmark, L1, PNC cost or supply-order
// date) while the figure stayed locked until completion, so the Project Officer could never
// complete those stages. Completion no longer looks at the figure, and the figure can be
// recorded as soon as the stage has started.
public sealed class ProcurementFactUnlockTests
{
    private const int ProjectId = 41;
    private const string ProjectOfficer = "po-1";
    private static readonly DateOnly Today = new(2026, 6, 30);
    private static readonly DateOnly PncStarted = new(2026, 6, 10);

    // The case reported from production (project 207): Benchmarking in progress, PDC set,
    // Benchmark cost "Not yet", and completion refused for the missing Benchmarking information.
    [Fact]
    public async Task ProjectOfficer_RequestsBenchmarkingCompletionWithoutABenchmarkCost_AndTheHoDCanApprove()
    {
        await using var connection = await OpenAsync();
        await using var db = await CreateDbAsync(
            connection,
            pnc: StageStatus.NotStarted,
            cob: StageStatus.NotStarted,
            bm: StageStatus.InProgress);
        var bmStart = (await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.BM)).ActualStart;

        var requested = await RequestCompletionAsync(db, StageCodes.BM, bmStart);

        Assert.Equal(BatchStageRequestOutcome.Success, requested.Outcome);
        Assert.False(await db.ProjectBenchmarkFacts.AnyAsync());
        var pending = await db.StageChangeRequests.SingleAsync(request => request.DecisionStatus == "Pending");
        var assessment = await new StageApprovalSequenceService(
                db,
                new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider()),
                new ProjectFactsReadService(db))
            .AssessRequestAsync(pending.Id);
        Assert.NotNull(assessment);
        Assert.Equal(ApprovalReadiness.Ready, assessment!.Readiness);
    }

    // The amount is optional, but a Project Officer must still give the completion date.
    [Fact]
    public async Task ProjectOfficer_StillNeedsACompletionDate()
    {
        await using var connection = await OpenAsync();
        await using var db = await CreateDbAsync(connection, pnc: StageStatus.InProgress);

        var result = await RequestCompletionAsync(db, StageCodes.PNC, PncStarted, withCompletionDate: false);

        Assert.Equal(BatchStageRequestOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("Completion date is required.", result.Items.SelectMany(item => item.Result.Errors));
    }

    [Fact]
    public async Task ProjectOfficer_CanRecordThePncCostWhilePncIsInProgress()
    {
        await using var connection = await OpenAsync();
        await using var db = await CreateDbAsync(connection, pnc: StageStatus.InProgress);

        var page = CreateEditPage(db, ProjectOfficer, pncCost: 4_80_00_000m);
        var saved = await page.OnPostAsync(ProjectId, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(saved);
        Assert.Equal("Procurement details updated.", page.TempData["Flash"]);
        Assert.Equal(4_80_00_000m, (await db.ProjectPncFacts.SingleAsync(fact => fact.ProjectId == ProjectId)).PncCost);
        Assert.Equal(BatchStageRequestOutcome.Success, (await RequestPncCompletionAsync(db)).Outcome);
    }

    [Fact]
    public async Task NotStartedStageWhosePredecessorsAreDone_OpensForADirectCompletion()
    {
        await using var connection = await OpenAsync();
        await using var db = await CreateDbAsync(connection, pnc: StageStatus.NotStarted);

        var saved = await CreateEditPage(db, ProjectOfficer, pncCost: 1_00_000m).OnPostAsync(ProjectId, CancellationToken.None);
        Assert.IsType<RedirectToPageResult>(saved);

        var requested = await RequestPncCompletionAsync(db);
        Assert.Equal(BatchStageRequestOutcome.Success, requested.Outcome);
    }

    [Fact]
    public async Task StageWithAnOpenPredecessor_StaysLocked()
    {
        await using var connection = await OpenAsync();
        await using var db = await CreateDbAsync(connection, pnc: StageStatus.NotStarted, cob: StageStatus.InProgress);

        var page = CreateEditPage(db, ProjectOfficer, pncCost: 1_00_000m);
        var result = await page.OnPostAsync(ProjectId, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("PNC cost cannot be recorded until PNC has started.", page.TempData["Error"]);
        Assert.False(await db.ProjectPncFacts.AnyAsync());
    }

    [Theory]
    [InlineData(StageStatus.InProgress, true)]
    [InlineData(StageStatus.Blocked, true)]
    [InlineData(StageStatus.Completed, true)]
    [InlineData(StageStatus.Skipped, false)]
    public async Task GetRecordableStages_FollowsTheStageStatus(StageStatus pnc, bool expected)
    {
        await using var connection = await OpenAsync();
        await using var db = await CreateDbAsync(connection, pnc: pnc);

        var canRecord = await ProcurementFactAvailabilityService.Create(db).GetRecordableStagesAsync(ProjectId);

        Assert.Equal(expected, canRecord(StageCodes.PNC));
        Assert.False(canRecord(StageCodes.SO));
    }

    [Fact]
    public async Task GetRecordableStages_PendingPredecessorCompletionCountsAsDone()
    {
        await using var connection = await OpenAsync();
        await using var db = await CreateDbAsync(connection, pnc: StageStatus.NotStarted, cob: StageStatus.InProgress);
        db.StageChangeRequests.Add(new StageChangeRequest
        {
            ProjectId = ProjectId,
            StageCode = StageCodes.COB,
            RequestedStatus = nameof(StageStatus.Completed),
            RequestedDate = PncStarted,
            RequestedByUserId = ProjectOfficer,
            RequestedOn = new DateTimeOffset(2026, 6, 20, 6, 0, 0, TimeSpan.Zero),
            DecisionStatus = "Pending"
        });
        await db.SaveChangesAsync();

        var canRecord = await ProcurementFactAvailabilityService.Create(db).GetRecordableStagesAsync(ProjectId);

        Assert.True(canRecord(StageCodes.PNC));
    }

    private static Task<BatchStageRequestResult> RequestPncCompletionAsync(ApplicationDbContext db)
        => RequestCompletionAsync(db, StageCodes.PNC, PncStarted);

    private static async Task<BatchStageRequestResult> RequestCompletionAsync(
        ApplicationDbContext db,
        string stageCode,
        DateOnly? startDate,
        bool withCompletionDate = true)
    {
        var clock = FakeClock.ForIstDate(Today);
        var workflow = new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider());
        var service = new StageRequestService(
            db,
            clock,
            new StageValidationService(db, clock, workflow),
            workflow,
            new ProjectFactsReadService(db));

        return await service.CreateBatchAsync(
            new BatchStageChangeRequestInput
            {
                ProjectId = ProjectId,
                Stages = new[]
                {
                    new StageChangeRequestItemInput
                    {
                        StageCode = stageCode,
                        RequestedStatus = nameof(StageStatus.Completed),
                        RequestedDate = withCompletionDate ? Today : null,
                        RequestedStartDate = startDate
                    }
                }
            },
            ProjectOfficer);
    }

    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    // Workflow v2 up to COB completed (one stage per day to 9 Jun 2026), then PNC and later stages.
    private static async Task<ApplicationDbContext> CreateDbAsync(
        SqliteConnection connection,
        StageStatus pnc,
        StageStatus cob = StageStatus.Completed,
        StageStatus bm = StageStatus.Completed)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        foreach (var id in new[] { "seed", ProjectOfficer })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id });
        }

        db.Projects.Add(new Project
        {
            Id = ProjectId,
            Name = "Procurement unlock project",
            CreatedByUserId = "seed",
            LeadPoUserId = ProjectOfficer,
            WorkflowVersion = ProcurementWorkflow.VersionV2
        });

        var codes = ProcurementWorkflow.StageCodesFor(ProcurementWorkflow.VersionV2);
        var pncIndex = Array.IndexOf(codes, StageCodes.PNC);
        var firstDay = PncStarted.AddDays(-pncIndex - 1);
        for (var index = 0; index < codes.Length; index++)
        {
            var code = codes[index];
            var status = code == StageCodes.PNC ? pnc
                : code == StageCodes.COB ? cob
                : code == StageCodes.BM ? bm
                : index < pncIndex ? StageStatus.Completed
                : StageStatus.NotStarted;
            db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = ProjectId,
                StageCode = code,
                SortOrder = index + 1,
                Status = status,
                ActualStart = status is StageStatus.NotStarted or StageStatus.Skipped
                    ? null
                    : code == StageCodes.PNC ? PncStarted : firstDay.AddDays(index),
                CompletedOn = status == StageStatus.Completed
                    ? code == StageCodes.PNC ? PncStarted.AddDays(5) : firstDay.AddDays(index + 1)
                    : null
            });
        }

        await db.SaveChangesAsync();
        return db;
    }

    private static EditModel CreateEditPage(ApplicationDbContext db, string userId, decimal? pncCost, decimal? benchmarkCost = null)
    {
        var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(db),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var facts = new ProjectFactsService(db, FakeClock.ForIstDate(Today), new RecordingAudit());
        var page = new EditModel(facts, userManager, db, NullLogger<EditModel>.Instance)
        {
            Input = new ProcurementEditInput { ProjectId = ProjectId, PncCost = pncCost, BenchmarkCost = benchmarkCost }
        };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, "Project Officer")
            }, "Test"))
        };
        page.PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
        page.TempData = new TempDataDictionary(httpContext, new NullTempDataProvider());
        return page;
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
