using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;
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
            Name = "Actuals Test Project",
            CreatedByUserId = "user-1",
            WorkflowVersion = PlanConstants.DefaultStageTemplateVersion
        });

        db.ProjectStages.Add(new ProjectStage
        {
            ProjectId = 1,
            StageCode = StageCodes.IPA,
            SortOrder = 1,
            Status = StageStatus.InProgress,
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
            userId: "user-1",
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
            userId: "user-1",
            userName: "Tester");

        Assert.Equal(1, result.UpdatedCount);
        var stage = await db.ProjectStages.SingleAsync();
        Assert.Null(stage.ActualStart);
        Assert.Equal(new DateOnly(2024, 1, 25), stage.CompletedOn);
    }

    [Fact]
    public async Task UpdateAsync_AllowsCompletionWithoutStart_ForInProgressStage()
    {
        var (connection, db, service) = await CreateServiceWithSingleStageAsync(
            StageStatus.InProgress,
            actualStart: null,
            completedOn: null);
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
            userId: "user-1",
            userName: "Tester");

        Assert.Equal(1, result.UpdatedCount);
        var stage = await db.ProjectStages.SingleAsync();
        Assert.Null(stage.ActualStart);
        Assert.Equal(new DateOnly(2024, 1, 25), stage.CompletedOn);
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
            RequestedAt = new DateTimeOffset(2024, 1, 30, 0, 0, 0, TimeSpan.Zero),
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
                        CompletedOn = new DateOnly(2024, 1, 20)
                    },
                    new()
                    {
                        StageCode = StageCodes.AON
                    }
                }
            },
            userId: "user-1",
            userName: "Tester");

        Assert.Equal(1, result.UpdatedCount);
        var updated = await db.ProjectStages.SingleAsync(s => s.StageCode == StageCodes.IPA);
        Assert.Equal(new DateOnly(2024, 1, 20), updated.CompletedOn);
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
