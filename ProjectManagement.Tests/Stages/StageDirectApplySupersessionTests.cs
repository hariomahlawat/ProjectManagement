using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Stages;

/// <summary>
/// HoD direct apply supersedes the stage's pending request only while it is still
/// pending. The cross-connection ARPP race is covered on PostgreSQL by
/// <see cref="StageDecisionConcurrencyPostgresFixture"/>.
/// </summary>
public sealed class StageDirectApplySupersessionTests
{
    [Fact]
    public async Task DirectApply_SupersedesThePendingRequestOnceWithOneHistoryRow()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.NewContext();

        var result = await Service(db).ApplyAsync(
            fixture.ProjectId, StageCodes.IPA, "Completed", new DateOnly(2026, 1, 26), null, "hod", false, CancellationToken.None);

        Assert.Equal("Completed", result.UpdatedStatus);
        await using var verify = fixture.NewContext();
        var request = await verify.StageChangeRequests.SingleAsync();
        Assert.Equal("Superseded", request.DecisionStatus);
        Assert.Equal("hod", request.DecidedByUserId);
        Assert.Equal(new[] { "Superseded", "DirectApply", "Applied" },
            await verify.StageChangeLogs.OrderBy(log => log.Id).Select(log => log.Action).ToArrayAsync());
    }

    [Fact]
    public async Task RequestDecidedBeforeTheSupersession_IsNotOverwritten_AndTheDirectApplyWritesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.NewContext(new DecideBeforeFirstRequestWrite(fixture.RequestId));

        var refusal = await Assert.ThrowsAsync<StageDirectApplyValidationException>(() => Service(db).ApplyAsync(
            fixture.ProjectId, StageCodes.IPA, "Completed", new DateOnly(2026, 1, 26), null, "hod", false, CancellationToken.None));

        Assert.Contains(refusal.Details, detail => detail.Contains("was decided while this change was being applied", StringComparison.Ordinal));
        await using var verify = fixture.NewContext();
        Assert.Equal("Pending", (await verify.StageChangeRequests.SingleAsync()).DecisionStatus); // the injected decision rolled back with it
        Assert.Equal(StageStatus.InProgress, (await verify.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA)).Status);
        Assert.Empty(await verify.StageChangeLogs.ToListAsync());
    }

    private static StageDirectApplyService Service(ApplicationDbContext db)
    {
        var clock = FakeClock.ForIstDate(2026, 3, 31);
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        return new StageDirectApplyService(db, clock, new StageValidationService(db, clock, policy), new StageRulesService(db, policy), policy);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection) => _connection = connection;

        public int ProjectId { get; private set; }
        public int RequestId { get; private set; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);

            await using var db = fixture.NewContext();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new ApplicationUser { Id = "seed", UserName = "seed" });
            var project = new Project { Name = "Direct apply supersession", CreatedByUserId = "seed", WorkflowVersion = ProcurementWorkflow.VersionV1 };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            db.ProjectStages.AddRange(
                new ProjectStage
                {
                    ProjectId = project.Id, StageCode = StageCodes.FS, SortOrder = 1, Status = StageStatus.Completed,
                    ActualStart = new DateOnly(2026, 1, 1), CompletedOn = new DateOnly(2026, 1, 10)
                },
                new ProjectStage
                {
                    ProjectId = project.Id, StageCode = StageCodes.IPA, SortOrder = 2, Status = StageStatus.InProgress,
                    ActualStart = new DateOnly(2026, 1, 12)
                });
            var request = new StageChangeRequest
            {
                ProjectId = project.Id, StageCode = StageCodes.IPA, RequestedStatus = StageStatus.Completed.ToString(),
                RequestedDate = new DateOnly(2026, 1, 25), RequestedByUserId = "po",
                RequestedOn = DateTimeOffset.UtcNow.AddDays(-1), DecisionStatus = "Pending"
            };
            db.StageChangeRequests.Add(request);
            await db.SaveChangesAsync();
            fixture.ProjectId = project.Id;
            fixture.RequestId = request.Id;
            return fixture;
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

    /// <summary>Marks the request Approved just before the direct apply's first write to it.</summary>
    private sealed class DecideBeforeFirstRequestWrite(int requestId) : DbCommandInterceptor
    {
        private int _done;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await DecideIfFirstRequestWriteAsync(command, cancellationToken);
            return result;
        }

        // SaveChanges batches run as readers; a tracked supersession would arrive here.
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await DecideIfFirstRequestWriteAsync(command, cancellationToken);
            return result;
        }

        private async Task DecideIfFirstRequestWriteAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (command.CommandText.Contains("UPDATE \"StageChangeRequests\"", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _done, 1) == 0)
            {
                await using var decide = command.Connection!.CreateCommand();
                decide.Transaction = command.Transaction;
                decide.CommandText = $"UPDATE \"StageChangeRequests\" SET \"DecisionStatus\" = 'Approved', \"DecidedByUserId\" = 'other-hod' WHERE \"Id\" = {requestId}";
                await decide.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
