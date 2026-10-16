using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests;

public partial class StageDirectApplyServiceTests
{
    public static IEnumerable<object?[]> DirectForceOptionalCases()
    {
        foreach (var version in new[] { ProcurementWorkflow.VersionV1, ProcurementWorkflow.VersionV2 })
        foreach (var target in new[] { StageCodes.EAS, StageCodes.SO })
        foreach (var desired in new[] { StageStatus.InProgress, StageStatus.Completed })
        foreach (var applicable in new[] { true, false })
        foreach (var pnc in new StageStatus?[] { StageStatus.NotStarted, StageStatus.InProgress, StageStatus.Blocked, null, StageStatus.Completed, StageStatus.Skipped })
            yield return new object?[] { version, target, desired, applicable, pnc };
    }

    [Theory]
    [MemberData(nameof(DirectForceOptionalCases))]
    public async Task DirectForce_OptionalAncestorRequiresAnAlreadyResolvedLifecycle(
        string version, string target, StageStatus desired, bool applicable, StageStatus? pncStatus)
    {
        await using var fixture = await OptionalForceFixture.CreateAsync(version, target, desired, applicable, pncStatus);
        var before = await DirectForceStateAsync(fixture.Db);
        var pncBefore = await fixture.Db.ProjectStages.AsNoTracking().SingleOrDefaultAsync(s => s.StageCode == StageCodes.PNC);
        var rejects = applicable && pncStatus is not StageStatus.Completed and not StageStatus.Skipped;
        if (rejects)
        {
            var error = await Assert.ThrowsAsync<StageDirectApplyValidationException>(() => fixture.Service.ApplyAsync(
                1, target, desired.ToString(), new DateOnly(2024, 7, 10), "Force must not resolve PNC", "hod-1", true, default));
            Assert.Contains(StageCodes.PNC, error.MissingPredecessors);
            // Fresh persisted reads prove missing-row materialisation, predecessor
            // flags/dates, pending-request supersession and history all roll back.
            fixture.Db.ChangeTracker.Clear();
            Assert.Equal(before, await DirectForceStateAsync(fixture.Db));
        }
        else
        {
            var result = await fixture.Service.ApplyAsync(
                1, target, desired.ToString(), new DateOnly(2024, 7, 10), "Resolved or non-applicable PNC", "hod-1", true, default);
            fixture.Db.ChangeTracker.Clear();
            Assert.Equal(desired, (await fixture.Db.ProjectStages.SingleAsync(s => s.StageCode == target)).Status);
            Assert.DoesNotContain(StageCodes.PNC, result.BackfilledStages);
            var pncAfter = await fixture.Db.ProjectStages.AsNoTracking().SingleAsync(s => s.StageCode == StageCodes.PNC);
            Assert.Equal(pncBefore?.Status ?? StageStatus.NotStarted, pncAfter.Status);
            Assert.Equal(pncBefore?.ActualStart, pncAfter.ActualStart);
            Assert.Equal(pncBefore?.CompletedOn, pncAfter.CompletedOn);
            Assert.False(pncAfter.IsAutoCompleted);
            Assert.False(pncAfter.RequiresBackfill);
            Assert.Null(pncAfter.AutoCompletedFromCode);
            Assert.Equal(applicable, (await fixture.Db.Projects.AsNoTracking().SingleAsync()).PncApplicable);
        }
    }

    [Theory]
    [InlineData(ProcurementWorkflow.VersionV1)]
    [InlineData(ProcurementWorkflow.VersionV2)]
    public async Task DirectForce_PresentNonOptionalAncestorStillSupportsAuthorisedBackfill(string version)
    {
        await using var fixture = await OptionalForceFixture.CreateAsync(version, StageCodes.EAS, StageStatus.Completed, true, StageStatus.Completed);
        var predecessor = await fixture.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.TEC);
        predecessor.Status = StageStatus.InProgress;
        predecessor.CompletedOn = null;
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.Service.ApplyAsync(
            1, StageCodes.EAS, nameof(StageStatus.Completed), new DateOnly(2024, 7, 10), "Deliberate non-optional backfill", "hod-1", true, default);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(new[] { StageCodes.TEC }, result.BackfilledStages);
        var saved = await fixture.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.TEC);
        Assert.Equal(StageStatus.Completed, saved.Status);
        Assert.Null(saved.CompletedOn);
        Assert.True(saved.IsAutoCompleted);
        Assert.True(saved.RequiresBackfill);
        Assert.Equal(StageCodes.EAS, saved.AutoCompletedFromCode);
        Assert.Equal(StageStatus.Completed, (await fixture.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.EAS)).Status);
        Assert.Equal(StageStatus.Completed, (await fixture.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC)).Status);
    }

    [Theory]
    [InlineData(ProcurementWorkflow.VersionV1, StageStatus.InProgress)]
    [InlineData(ProcurementWorkflow.VersionV1, StageStatus.Completed)]
    [InlineData(ProcurementWorkflow.VersionV1, StageStatus.Skipped)]
    [InlineData(ProcurementWorkflow.VersionV2, StageStatus.InProgress)]
    [InlineData(ProcurementWorkflow.VersionV2, StageStatus.Completed)]
    [InlineData(ProcurementWorkflow.VersionV2, StageStatus.Skipped)]
    public async Task DirectForce_ExplicitValidPncLifecycleEditsRemainAllowed(string version, StageStatus desired)
    {
        await using var fixture = await OptionalForceFixture.CreateAsync(version, StageCodes.PNC, desired, true, StageStatus.NotStarted);
        var result = await fixture.Service.ApplyAsync(
            1, StageCodes.PNC, desired.ToString(), desired == StageStatus.Skipped ? null : new DateOnly(2024, 7, 10),
            "Explicit PNC lifecycle decision", "hod-1", true, default);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(desired, (await fixture.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.PNC)).Status);
        Assert.Empty(result.BackfilledStages);
    }

    [Theory]
    [InlineData(ProcurementWorkflow.VersionV1, StageStatus.InProgress)]
    [InlineData(ProcurementWorkflow.VersionV1, StageStatus.Completed)]
    [InlineData(ProcurementWorkflow.VersionV2, StageStatus.InProgress)]
    [InlineData(ProcurementWorkflow.VersionV2, StageStatus.Completed)]
    public async Task DirectForce_ConfiguredOptionalIpaUsesPublishedArppEffectiveCompletion(string version, StageStatus desired)
    {
        await using var fixture = await OptionalForceFixture.CreateAsync(version, StageCodes.EAS, desired, true, StageStatus.Completed);
        fixture.Db.StageTemplates.AddRange(
            new StageTemplate { Version = version, Code = StageCodes.IPA, Name = "IPA", Sequence = ProcurementWorkflow.OrderOf(version, StageCodes.IPA), Optional = true },
            new StageTemplate { Version = version, Code = StageCodes.PNC, Name = "PNC", Sequence = ProcurementWorkflow.OrderOf(version, StageCodes.PNC), Optional = true });
        var storedIpa = await fixture.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        storedIpa.Status = StageStatus.NotStarted;
        storedIpa.ActualStart = null;
        storedIpa.CompletedOn = null;
        AddPublishedPosition(fixture.Db, 1, new DateOnly(2024, 7, 2));
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Service.ApplyAsync(
            1, StageCodes.EAS, desired.ToString(), new DateOnly(2024, 7, 10), "Published IPA is already resolved", "hod-1", true, default);

        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(desired, (await fixture.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.EAS)).Status);
        Assert.Empty(result.BackfilledStages);
        var persistedIpa = await fixture.Db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(StageStatus.NotStarted, persistedIpa.Status);
        Assert.Null(persistedIpa.ActualStart);
        Assert.Null(persistedIpa.CompletedOn);
        Assert.False(persistedIpa.IsAutoCompleted);
        Assert.False(persistedIpa.RequiresBackfill);
        Assert.DoesNotContain(await fixture.Db.StageChangeLogs.ToListAsync(), log => log.StageCode == StageCodes.IPA);
    }

    private static async Task<string> DirectForceStateAsync(ApplicationDbContext db) => JsonSerializer.Serialize(new
    {
        Projects = await db.Projects.AsNoTracking().Select(p => new { p.Id, p.PncApplicable, p.RowVersion }).ToListAsync(),
        Stages = await db.ProjectStages.AsNoTracking().OrderBy(s => s.Id).Select(s => new
        {
            s.Id, s.StageCode, s.SortOrder, s.Status, s.PlannedStart, s.PlannedDue, s.ForecastStart, s.ForecastDue,
            s.ActualStart, s.CompletedOn, s.IsAutoCompleted, s.AutoCompletedFromCode, s.RequiresBackfill
        }).ToListAsync(),
        Requests = await db.StageChangeRequests.AsNoTracking().OrderBy(r => r.Id).ToListAsync(),
        History = await db.StageChangeLogs.AsNoTracking().OrderBy(l => l.Id).ToListAsync()
    });

    private sealed class OptionalForceFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public ApplicationDbContext Db { get; }
        public StageDirectApplyService Service { get; }
        private OptionalForceFixture(SqliteConnection connection, ApplicationDbContext db, StageDirectApplyService service) =>
            (_connection, Db, Service) = (connection, db, service);

        public static async Task<OptionalForceFixture> CreateAsync(string version, string target, StageStatus desired, bool applicable, StageStatus? pncStatus)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.Projects.Add(new Project { Id = 1, Name = "Direct force optional dependency", WorkflowVersion = version, PncApplicable = applicable });
            foreach (var definition in ProcurementWorkflow.StageDefinitionsFor(version))
            {
                if (definition.Code == StageCodes.PNC && pncStatus is null) continue;
                var status = definition.Code == StageCodes.PNC ? pncStatus!.Value : StageStatus.Completed;
                if (definition.Code == target)
                    status = desired == StageStatus.Completed ? StageStatus.InProgress : StageStatus.NotStarted;
                // Explicit PNC lifecycle tests begin with an unresolved PNC.
                if (target == StageCodes.PNC && definition.Code == StageCodes.PNC) status = StageStatus.NotStarted;
                db.ProjectStages.Add(new ProjectStage
                {
                    ProjectId = 1, StageCode = definition.Code, SortOrder = ProcurementWorkflow.OrderOf(version, definition.Code), Status = status,
                    ActualStart = definition.Code == target && desired == StageStatus.Completed && target != StageCodes.PNC
                        ? new DateOnly(2024, 7, 5)
                        : status is StageStatus.InProgress or StageStatus.Blocked or StageStatus.Completed ? new DateOnly(2024, 7, 1) : null,
                    CompletedOn = status == StageStatus.Completed ? new DateOnly(2024, 7, 2) : null
                });
            }
            // The completion transition retains a real existing start; starts use
            // a NotStarted target and the submitted date.
            db.StageChangeRequests.Add(new StageChangeRequest
            {
                ProjectId = 1, StageCode = target, RequestedStatus = desired.ToString(), RequestedByUserId = "po-1",
                RequestedOn = new DateTimeOffset(2024, 7, 3, 0, 0, 0, TimeSpan.Zero), DecisionStatus = "Pending", Note = "Recognisable pending request"
            });
            db.StageChangeLogs.Add(new StageChangeLog
            {
                ProjectId = 1, StageCode = target, Action = "Applied", UserId = "po-1",
                At = new DateTimeOffset(2024, 7, 3, 0, 0, 0, TimeSpan.Zero), Note = "Preserve this history"
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var clock = FakeClock.ForIstDate(2024, 7, 15);
            var policy = StageWorkflowTestFactory.CreatePolicy(db);
            var validator = new StageValidationService(db, clock, policy);
            return new OptionalForceFixture(connection, db, new StageDirectApplyService(db, clock, validator, new StageRulesService(db, policy), policy));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
