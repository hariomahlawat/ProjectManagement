using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProjectManagement.Contracts.Stages;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Stages;

/// <summary>
/// Phase D-1c review: lifecycle submission supersedes a same-stage request only
/// while it is still pending. The cross-connection ARPP race is covered on
/// PostgreSQL by <see cref="StageDecisionConcurrencyPostgresFixture"/>.
/// </summary>
public sealed class StageRequestSupersessionTests
{
    private const string LeadPo = "lead-po";

    [Fact]
    public async Task Replacement_SupersedesThePendingRequestOnceWithOneHistoryRow()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.NewContext();

        var result = await Service(db).CreateAsync(Input(fixture.ProjectId), LeadPo);

        Assert.Equal(StageRequestOutcome.Success, result.Outcome);
        await using var verify = fixture.NewContext();
        var original = await verify.StageChangeRequests.SingleAsync(item => item.Id == fixture.RequestId);
        Assert.Equal("Superseded", original.DecisionStatus);
        Assert.Equal(LeadPo, original.DecidedByUserId);
        Assert.Equal("Pending", (await verify.StageChangeRequests.SingleAsync(item => item.Id == result.RequestId)).DecisionStatus);
        Assert.Equal(new[] { "Superseded", "Requested" },
            await verify.StageChangeLogs.OrderBy(log => log.Id).Select(log => log.Action).ToArrayAsync());
    }

    [Fact]
    public async Task RequestDecidedBeforeTheSupersession_IsNotOverwritten_AndTheSubmissionWritesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        // Another writer decides the request after submission read it as pending.
        await using var db = fixture.NewContext(new DecideBeforeFirstRequestWrite(fixture.RequestId));

        var result = await Service(db).CreateAsync(Input(fixture.ProjectId), LeadPo);

        Assert.Equal(StageRequestOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(result.Errors, error => error.Contains("was decided while this update was being submitted", StringComparison.Ordinal));
        await using var verify = fixture.NewContext();
        Assert.Single(await verify.StageChangeRequests.ToListAsync());
        Assert.Empty(await verify.StageChangeLogs.ToListAsync());
    }

    private static StageRequestService Service(ApplicationDbContext db)
    {
        var clock = FakeClock.ForIstDate(2026, 3, 31);
        var policy = StageWorkflowTestFactory.CreatePolicy(db);
        return new StageRequestService(db, clock, new StageValidationService(db, clock, policy), policy);
    }

    private static StageChangeRequestInput Input(int projectId) => new()
    {
        ProjectId = projectId,
        StageCode = StageCodes.IPA,
        RequestedStatus = StageStatus.Completed.ToString(),
        RequestedDate = new DateOnly(2026, 1, 26)
    };

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
            db.Users.AddRange(
                new ApplicationUser { Id = "seed", UserName = "seed" },
                new ApplicationUser { Id = LeadPo, UserName = LeadPo });
            var project = new Project
            {
                Name = "Supersession", CreatedByUserId = "seed", LeadPoUserId = LeadPo,
                WorkflowVersion = ProcurementWorkflow.VersionV1
            };
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
                RequestedDate = new DateOnly(2026, 1, 25), RequestedByUserId = LeadPo,
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

    /// <summary>Marks the request Approved just before the submission's first write to it.</summary>
    private sealed class DecideBeforeFirstRequestWrite(int requestId) : DbCommandInterceptor
    {
        private int _done;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await DecideIfFirstRequestWriteAsync(command, cancellationToken);
            return result;
        }

        // SaveChanges batches run as readers; a tracked supersession would arrive here.
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
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
                decide.CommandText = $"UPDATE \"StageChangeRequests\" SET \"DecisionStatus\" = 'Approved', \"DecidedByUserId\" = 'hod' WHERE \"Id\" = {requestId}";
                await decide.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
