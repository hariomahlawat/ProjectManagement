using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Arpp;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Stages;
using ProjectManagement.Tests.Fakes;
using ProjectManagement.ViewModels;
using Xunit;

namespace ProjectManagement.Tests;

// SECTION: Stage actuals update tests
public sealed class StageActualsUpdateServiceTests
{
    [Fact]
    public async Task UpdateAsync_SavesActualsAndLogsWithoutConstraintError()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Actuals Test Project",
            CreatedByUserId = "user-1",
            WorkflowVersion = PlanConstants.DefaultStageTemplateVersion
        });

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 1,
            Status = StageStatus.Completed,
            ActualStart = new DateOnly(2024, 1, 5),
            CompletedOn = new DateOnly(2024, 1, 20),
            RequiresBackfill = false
        });

        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new StageActualsUpdateService(db, clock, new FakeAudit(), NullLogger<StageActualsUpdateService>.Instance);

        var result = await service.UpdateAsync(
            new ActualsEditInput
            {
                ProjectId = 1,
                Rows = new List<ActualsEditRowInput>
                {
                    new()
                    {
                        StageCode = StageCodes.IPA,
                        ActualStart = new DateOnly(2024, 1, 10),
                        CompletedOn = new DateOnly(2024, 1, 25)
                    }
                }
            },
            actor: TestActors.Hod("user-1"),
            userName: "Tester");

        Assert.Equal(1, result.UpdatedCount);

        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2024, 1, 10), stage.ActualStart);
        Assert.Equal(new DateOnly(2024, 1, 25), stage.CompletedOn);

        var log = await db.StageChangeLogs.SingleAsync();
        Assert.Equal("ActualsUpdated", log.Action);
        Assert.Equal(new DateOnly(2024, 1, 10), log.ToActualStart);
        Assert.Equal(new DateOnly(2024, 1, 25), log.ToCompletedOn);
    }

    [Fact]
    public async Task UpdateAsync_AllowsBenchmarkingActualStartDuringTechnicalEvaluation_WhenBidWasComplete()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Parallel Actuals Project",
            CreatedByUserId = "user-1",
            WorkflowVersion = ProcurementWorkflow.VersionV2
        });
        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.BID,
                SortOrder = 5,
                Status = StageStatus.Completed,
                ActualStart = new DateOnly(2026, 8, 1),
                CompletedOn = new DateOnly(2026, 8, 6)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.TEC,
                SortOrder = 6,
                Status = StageStatus.Completed,
                ActualStart = new DateOnly(2026, 8, 7),
                CompletedOn = new DateOnly(2026, 8, 21)
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.BM,
                SortOrder = 7,
                Status = StageStatus.Completed,
                ActualStart = new DateOnly(2026, 8, 21),
                CompletedOn = new DateOnly(2026, 8, 25)
            });
        db.StageDependencyTemplates.Add(new StageDependencyTemplate
        {
            Version = ProcurementWorkflow.VersionV2,
            FromStageCode = StageCodes.BM,
            DependsOnStageCode = StageCodes.BID
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2026, 8, 28, 6, 30, 0, TimeSpan.Zero));
        var service = new StageActualsUpdateService(
            db,
            clock,
            new FakeAudit(),
            NullLogger<StageActualsUpdateService>.Instance,
            workflowPolicy: StageWorkflowTestFactory.CreatePolicy(db));

        var result = await service.UpdateAsync(
            new ActualsEditInput
            {
                ProjectId = 1,
                Rows = new List<ActualsEditRowInput>
                {
                    new()
                    {
                        StageCode = StageCodes.BM,
                        ActualStart = new DateOnly(2026, 8, 10),
                        CompletedOn = new DateOnly(2026, 8, 25)
                    }
                }
            },
            actor: TestActors.Hod("user-1"),
            userName: "Tester");

        Assert.Equal(1, result.UpdatedCount);
        var benchmarking = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.BM);
        Assert.Equal(new DateOnly(2026, 8, 10), benchmarking.ActualStart);
        Assert.Equal(new DateOnly(2026, 8, 25), benchmarking.CompletedOn);
    }

    [Fact]
    public async Task UpdateAsync_AllowsCompletionWithoutStart_ForCompletedStage()
    {
        var (connection, db, service) = await CreateServiceWithSingleStageAsync(
            StageStatus.Completed,
            actualStart: null,
            completedOn: new DateOnly(2024, 1, 20));
        await using var _ = connection;
        await using var __ = db;

        var result = await service.UpdateAsync(
            new ActualsEditInput
            {
                ProjectId = 1,
                Rows = new List<ActualsEditRowInput>
                {
                    new()
                    {
                        StageCode = StageCodes.IPA,
                        CompletedOn = new DateOnly(2024, 1, 25)
                    }
                }
            },
            actor: TestActors.Hod("user-1"),
            userName: "Tester");

        Assert.Equal(1, result.UpdatedCount);
        var stage = await db.ProjectStages.SingleAsync();
        Assert.Null(stage.ActualStart);
        Assert.Equal(new DateOnly(2024, 1, 25), stage.CompletedOn);
        Assert.False(stage.RequiresBackfill);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateAsync_RejectsCompletionForInProgressStage(bool hasStart)
    {
        var start = hasStart ? new DateOnly(2024, 1, 10) : (DateOnly?)null;
        var (connection, db, service) = await CreateServiceWithSingleStageAsync(StageStatus.InProgress, start, null);
        await using var _ = connection;
        await using var __ = db;
        var input = new ActualsEditInput
        {
            ProjectId = 1,
            Rows = new List<ActualsEditRowInput> { new() { StageCode = StageCodes.IPA, CompletedOn = new DateOnly(2024, 1, 25) } }
        };
        await Assert.ThrowsAsync<StageActualsValidationException>(() => service.UpdateAsync(input, TestActors.Hod("user-1"), "Tester"));
        var stage = await db.ProjectStages.AsNoTracking().SingleAsync();
        Assert.Equal(StageStatus.InProgress, stage.Status);
        Assert.Equal(start, stage.ActualStart);
        Assert.Null(stage.CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
    }

    [Theory]
    [InlineData(StageStatus.Completed, true)]
    [InlineData(StageStatus.InProgress, false)]
    public async Task UpdateAsync_ExplicitStartClear_ObeysStatus(StageStatus status, bool allowed)
    {
        var completion = status == StageStatus.Completed ? new DateOnly(2024, 1, 25) : (DateOnly?)null;
        var (connection, db, service) = await CreateServiceWithSingleStageAsync(status, new DateOnly(2024, 1, 10), completion);
        await using var _ = connection;
        await using var __ = db;
        var row = System.Text.Json.JsonSerializer.Deserialize<ActualsEditRowInput>(
            "{\"StageCode\":\"IPA\",\"ClearActualStart\":true}")!;
        var input = new ActualsEditInput { ProjectId = 1, Rows = new List<ActualsEditRowInput> { row } };
        if (allowed)
        {
            Assert.Equal(1, (await service.UpdateAsync(input, TestActors.Hod("user-1"), "Tester")).UpdatedCount);
            Assert.Null((await db.ProjectStages.AsNoTracking().SingleAsync()).ActualStart);
            var log = await db.StageChangeLogs.SingleAsync();
            Assert.Equal(new DateOnly(2024, 1, 10), log.FromActualStart);
            Assert.Null(log.ToActualStart);
        }
        else
        {
            await Assert.ThrowsAsync<StageActualsValidationException>(() => service.UpdateAsync(input, TestActors.Hod("user-1"), "Tester"));
            Assert.Equal(new DateOnly(2024, 1, 10), (await db.ProjectStages.AsNoTracking().SingleAsync()).ActualStart);
            Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
        }
        Assert.Equal(completion, (await db.ProjectStages.AsNoTracking().SingleAsync()).CompletedOn);
    }

    [Fact]
    public async Task UpdateAsync_LegacyBlankFields_PreserveCompletedDates()
    {
        var (connection, db, service) = await CreateServiceWithSingleStageAsync(
            StageStatus.Completed, new DateOnly(2024, 1, 10), new DateOnly(2024, 1, 25));
        await using var _ = connection;
        await using var __ = db;
        var result = await service.UpdateAsync(
            new ActualsEditInput { ProjectId = 1, Rows = new List<ActualsEditRowInput> { new() { StageCode = StageCodes.IPA } } }, TestActors.Hod("user-1"), "Tester");
        Assert.Equal(0, result.UpdatedCount);
        var stage = await db.ProjectStages.AsNoTracking().SingleAsync();
        Assert.Equal(new DateOnly(2024, 1, 10), stage.ActualStart);
        Assert.Equal(new DateOnly(2024, 1, 25), stage.CompletedOn);
    }

    [Theory]
    [InlineData("2024-02-02", "2024-01-25")]
    [InlineData("2024-01-10", "2024-02-02")]
    [InlineData("2024-01-26", "2024-01-25")]
    public async Task UpdateAsync_RejectsFutureOrInvertedDates_WithoutClamping(string startText, string completionText)
    {
        var (connection, db, service) = await CreateServiceWithSingleStageAsync(
            StageStatus.Completed, new DateOnly(2024, 1, 10), new DateOnly(2024, 1, 25));
        await using var _ = connection;
        await using var __ = db;
        var input = new ActualsEditInput
        {
            ProjectId = 1, Rows = new List<ActualsEditRowInput>
            {
                new() { StageCode = StageCodes.IPA, ActualStart = DateOnly.Parse(startText), CompletedOn = DateOnly.Parse(completionText) }
            }
        };
        await Assert.ThrowsAsync<StageActualsValidationException>(() => service.UpdateAsync(input, TestActors.Hod("user-1"), "Tester"));
        var stage = await db.ProjectStages.AsNoTracking().SingleAsync();
        Assert.Equal(new DateOnly(2024, 1, 10), stage.ActualStart);
        Assert.Equal(new DateOnly(2024, 1, 25), stage.CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
    }

    [Fact]
    public async Task UpdateAsync_UnchangedInvalidExistingRow_IsNotRepaired()
    {
        var completion = new DateOnly(2024, 1, 25);
        var (connection, db, service) = await CreateServiceWithSingleStageAsync(StageStatus.InProgress, null, completion);
        await using var _ = connection;
        await using var __ = db;
        var input = new ActualsEditInput { ProjectId = 1, Rows = new List<ActualsEditRowInput> { new() { StageCode = StageCodes.IPA } } };
        Assert.Equal(0, (await service.UpdateAsync(input, TestActors.Hod("user-1"), "Tester")).UpdatedCount);
        var stage = await db.ProjectStages.AsNoTracking().SingleAsync();
        Assert.Equal(StageStatus.InProgress, stage.Status);
        Assert.Null(stage.ActualStart);
        Assert.Equal(completion, stage.CompletedOn);
        Assert.Empty(await db.StageChangeLogs.ToArrayAsync());
    }

    [Fact]
    public async Task UpdateAsync_IgnoresUnchangedLockedStage_WhenAnotherChangedStageIsValid()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Actuals Test Project",
            CreatedByUserId = "user-1",
            WorkflowVersion = PlanConstants.DefaultStageTemplateVersion
        });

        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                SortOrder = 1,
                Status = StageStatus.InProgress,
                ActualStart = new DateOnly(2024, 1, 5),
                CompletedOn = null
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.AON,
                SortOrder = 2,
                Status = StageStatus.Completed,
                ActualStart = null,
                CompletedOn = new DateOnly(2024, 1, 15)
            });

        db.StageChangeRequests.Add(new StageChangeRequest
        {
            ProjectId = 1,
            StageCode = StageCodes.AON,
            RequestedByUserId = "user-2",
            RequestedOn = new DateTimeOffset(2024, 1, 30, 0, 0, 0, TimeSpan.Zero),
            DecisionStatus = "Pending"
        });

        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new StageActualsUpdateService(db, clock, new FakeAudit(), NullLogger<StageActualsUpdateService>.Instance);

        var result = await service.UpdateAsync(
            new ActualsEditInput
            {
                ProjectId = 1,
                Rows = new List<ActualsEditRowInput>
                {
                    new()
                    {
                        StageCode = StageCodes.IPA,
                        ActualStart = new DateOnly(2024, 1, 10)
                    },
                    new()
                    {
                        StageCode = StageCodes.AON
                    }
                }
            },
            actor: TestActors.Hod("user-1"),
            userName: "Tester");

        Assert.Equal(1, result.UpdatedCount);
        var updated = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(new DateOnly(2024, 1, 10), updated.ActualStart);
        Assert.Null(updated.CompletedOn);
    }

    [Fact]
    public async Task UpdateAsync_AllowsSuccessorToStartOnPredecessorCompletionDate()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Same-day chronology",
            CreatedByUserId = "user-1",
            WorkflowVersion = ProcurementWorkflow.VersionV1
        });

        var predecessorCompletion = new DateOnly(2024, 1, 10);
        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.FS,
                SortOrder = 0,
                Status = StageStatus.Completed,
                ActualStart = new DateOnly(2024, 1, 1),
                CompletedOn = predecessorCompletion
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                SortOrder = 1,
                Status = StageStatus.InProgress,
                ActualStart = predecessorCompletion.AddDays(1)
            });

        await db.SaveChangesAsync();

        var service = new StageActualsUpdateService(
            db,
            new FixedClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            new FakeAudit(),
            NullLogger<StageActualsUpdateService>.Instance,
            workflowPolicy: StageWorkflowTestFactory.CreatePolicy(db));

        var result = await service.UpdateAsync(
            new ActualsEditInput
            {
                ProjectId = 1,
                Rows = new List<ActualsEditRowInput>
                {
                    new()
                    {
                        StageCode = StageCodes.IPA,
                        ActualStart = predecessorCompletion
                    }
                }
            },
            actor: TestActors.Hod("user-1"),
            userName: "Tester");

        Assert.Equal(1, result.UpdatedCount);
        var successor = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        Assert.Equal(predecessorCompletion, successor.ActualStart);
    }

    [Fact]
    public async Task UpdateAsync_RejectsSuccessorStartBeforePredecessorCompletionDate()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Chronology guard",
            CreatedByUserId = "user-1",
            WorkflowVersion = ProcurementWorkflow.VersionV1
        });

        var predecessorCompletion = new DateOnly(2024, 1, 10);
        db.ProjectStages.AddRange(
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.FS,
                SortOrder = 0,
                Status = StageStatus.Completed,
                ActualStart = new DateOnly(2024, 1, 1),
                CompletedOn = predecessorCompletion
            },
            new ProjectStage
            {
                ProjectId = 1,
                StageCode = StageCodes.IPA,
                SortOrder = 1,
                Status = StageStatus.InProgress,
                ActualStart = predecessorCompletion.AddDays(1)
            });

        await db.SaveChangesAsync();

        var service = new StageActualsUpdateService(
            db,
            new FixedClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            new FakeAudit(),
            NullLogger<StageActualsUpdateService>.Instance,
            workflowPolicy: StageWorkflowTestFactory.CreatePolicy(db));

        var exception = await Assert.ThrowsAsync<StageActualsValidationException>(() => service.UpdateAsync(
            new ActualsEditInput
            {
                ProjectId = 1,
                Rows = new List<ActualsEditRowInput>
                {
                    new()
                    {
                        StageCode = StageCodes.IPA,
                        ActualStart = predecessorCompletion.AddDays(-1)
                    }
                }
            },
            actor: TestActors.Hod("user-1"),
            userName: "Tester"));

        Assert.Contains(exception.Errors, error =>
            error.Contains("10 Jan 2024", StringComparison.OrdinalIgnoreCase)
            && error.Contains("Same-day commencement is permitted", StringComparison.OrdinalIgnoreCase));

        var successor = await db.ProjectStages.SingleAsync(stage => stage.StageCode == StageCodes.IPA);
        Assert.Equal(predecessorCompletion.AddDays(1), successor.ActualStart);
    }

    [Fact]
    public async Task UpdateAsync_ArppManagedIpa_AllowsClearingActualStart_AndPreservesCompletion()
    {
        var (connection, db, _) = await CreateServiceWithSingleStageAsync(
            StageStatus.Completed,
            actualStart: new DateOnly(2026, 3, 1),
            completedOn: new DateOnly(2026, 2, 26));
        await using var __ = connection;
        await using var ___ = db;
        AddPublishedPosition(db, projectId: 1, issueDate: new DateOnly(2026, 2, 26));
        await db.SaveChangesAsync();

        var service = new StageActualsUpdateService(
            db,
            new FixedClock(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            new FakeAudit(),
            NullLogger<StageActualsUpdateService>.Instance,
            new ArppIpaStageAuthorityService(db));

        var result = await service.UpdateAsync(
            new ActualsEditInput
            {
                ProjectId = 1,
                Rows = new List<ActualsEditRowInput>
                {
                    new()
                    {
                        StageCode = StageCodes.IPA,
                        ActualStart = null,
                        ClearActualStart = true,
                        CompletedOn = null
                    }
                }
            },
            actor: TestActors.Hod("user-1"),
            userName: "Tester");

        Assert.Equal(1, result.UpdatedCount);
        var stage = await db.ProjectStages.SingleAsync();
        Assert.Null(stage.ActualStart);
        Assert.Equal(new DateOnly(2026, 2, 26), stage.CompletedOn);
        Assert.False(stage.RequiresBackfill);
    }

    [Fact]
    public async Task UpdateAsync_ArppManagedIpa_OmittedStartPreservesRecordedFact()
    {
        var start = new DateOnly(2026, 2, 1);
        var completion = new DateOnly(2026, 2, 26);
        var (connection, db, _) = await CreateServiceWithSingleStageAsync(StageStatus.Completed, start, completion);
        await using var disposableConnection = connection;
        await using var disposableDb = db;
        AddPublishedPosition(db, 1, completion);
        await db.SaveChangesAsync();
        var service = new StageActualsUpdateService(db,
            new FixedClock(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            new FakeAudit(), NullLogger<StageActualsUpdateService>.Instance);
        var result = await service.UpdateAsync(new ActualsEditInput
        {
            ProjectId = 1,
            Rows = [new ActualsEditRowInput { StageCode = StageCodes.IPA, CompletedOn = completion }]
        }, TestActors.Hod("user-1"), "Tester");
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(start, (await db.ProjectStages.SingleAsync()).ActualStart);
        Assert.Empty(await db.StageChangeLogs.ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_ArppManagedIpa_RejectsCompletionDateChange()
    {
        var (connection, db, _) = await CreateServiceWithSingleStageAsync(
            StageStatus.Completed,
            actualStart: null,
            completedOn: new DateOnly(2026, 2, 26));
        await using var __ = connection;
        await using var ___ = db;
        AddPublishedPosition(db, projectId: 1, issueDate: new DateOnly(2026, 2, 26));
        await db.SaveChangesAsync();

        var service = new StageActualsUpdateService(
            db,
            new FixedClock(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            new FakeAudit(),
            NullLogger<StageActualsUpdateService>.Instance,
            new ArppIpaStageAuthorityService(db));

        var exception = await Assert.ThrowsAsync<StageActualsValidationException>(() => service.UpdateAsync(
            new ActualsEditInput
            {
                ProjectId = 1,
                Rows = new List<ActualsEditRowInput>
                {
                    new()
                    {
                        StageCode = StageCodes.IPA,
                        CompletedOn = new DateOnly(2026, 3, 1)
                    }
                }
            },
            actor: TestActors.Hod("user-1"),
            userName: "Tester"));

        Assert.Contains(exception.Errors, error => error.Contains("controlled by the published ARPP", StringComparison.OrdinalIgnoreCase));
        var stage = await db.ProjectStages.SingleAsync();
        Assert.Equal(new DateOnly(2026, 2, 26), stage.CompletedOn);
    }

    // SECTION: Test helpers
    private static async Task<(SqliteConnection Connection, ApplicationDbContext Db, StageActualsUpdateService Service)> CreateServiceWithSingleStageAsync(
        StageStatus status,
        DateOnly? actualStart,
        DateOnly? completedOn)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Actuals Test Project",
            CreatedByUserId = "user-1",
            WorkflowVersion = PlanConstants.DefaultStageTemplateVersion
        });

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 1,
            Status = status,
            ActualStart = actualStart,
            CompletedOn = completedOn
        });

        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new StageActualsUpdateService(db, clock, new FakeAudit(), NullLogger<StageActualsUpdateService>.Instance);
        return (connection, db, service);
    }

    private static void AddPublishedPosition(ApplicationDbContext db, int projectId, DateOnly issueDate)
    {
        var issue = new ArppIssue
        {
            Id = 100,
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Original,
            IssueSequence = 0,
            Name = "ARPP/I&R&D/ARTRAC",
            IssueDate = issueDate,
            IsVerified = true,
            VerifiedAtUtc = DateTimeOffset.UtcNow,
            VerifiedByUserId = "verifier",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "seed",
            UpdatedByUserId = "seed"
        };

        issue.PublishedSnapshot = new ArppPublishedIssue
        {
            ArppIssueId = issue.Id,
            RevisionNumber = 1,
            FinancialYearStart = 2026,
            Kind = ArppIssueKind.Original,
            IssueSequence = 0,
            Name = issue.Name,
            IssueDate = issueDate,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            PublishedByUserId = "verifier",
            AttachmentStorageKey = "arpp/100.pdf",
            AttachmentOriginalFileName = "ARPP-100.pdf",
            AttachmentContentType = "application/pdf",
            AttachmentSizeBytes = 100,
            AttachmentSha256 = new string('a', 64),
            Entries =
            {
                new ArppPublishedEntry
                {
                    ArppIssueId = issue.Id,
                    SourceEntryId = 1,
                    SortOrder = 1,
                    SerialNumber = "33",
                    ProjectReference = "Project",
                    ProjectId = projectId,
                    Category = ArppCategory.New,
                    IpaCost = 40_000_000m,
                    Cfa = "Comdt SDD",
                    Fund = "IR&D",
                    DfpdsSchedule = "9.3"
                }
            }
        };

        db.ArppIssues.Add(issue);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeAudit : IAuditService
    {
        public Task LogAsync(
            string action,
            string? message = null,
            string level = "Info",
            string? userId = null,
            string? userName = null,
            IDictionary<string, string?>? data = null,
            Microsoft.AspNetCore.Http.HttpContext? http = null)
            => Task.CompletedTask;
    }
}
