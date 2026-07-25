using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectLifecycleServiceTests
{
    [Fact]
    public async Task MarkCompleted_WhenActive_SetsLifecycleAndLogs()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Alpha",
            CreatedAt = new DateTime(2024, 1, 10),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Active
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var audit = new TestAuditService();
        var service = new ProjectLifecycleService(db, audit, clock);

        var result = await service.MarkCompletedAsync(1, "actor", 2024);

        Assert.True(result.IsSuccess);
        var project = await db.Projects.SingleAsync(p => p.Id == 1);
        Assert.Equal(ProjectLifecycleStatus.Completed, project.LifecycleStatus);
        Assert.Equal(2024, project.CompletedYear);
        Assert.Null(project.CompletedOn);
        Assert.Null(project.CancelledOn);
        Assert.Null(project.CancelReason);

        var entry = Assert.Single(audit.Logs);
        Assert.Equal("Project.LifecycleCompletionUpdated", entry.Action);
        Assert.Equal("actor", entry.UserId);
        Assert.Equal("2024", entry.Data["CompletionYear"]);
        Assert.Equal("YearOnly", entry.Data["Precision"]);
    }

    [Fact]
    public async Task MarkCompleted_WithOutOfRangeYear_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 5,
            Name = "Beta",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Active
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var audit = new TestAuditService();
        var service = new ProjectLifecycleService(db, audit, clock);

        var result = await service.MarkCompletedAsync(5, "actor", 2125);

        Assert.Equal(ProjectLifecycleOperationStatus.ValidationFailed, result.Status);
        Assert.Equal("Completion year must be between 1900 and 2024.", result.ErrorMessage);
        var project = await db.Projects.SingleAsync(p => p.Id == 5);
        Assert.Equal(ProjectLifecycleStatus.Active, project.LifecycleStatus);
        Assert.Null(project.CompletedYear);
        Assert.Empty(audit.Logs);
    }

    [Fact]
    public async Task MarkCompleted_WhenAlreadyEndorsed_ReturnsInvalidStatus()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 7,
            Name = "Gamma",
            CreatedAt = new DateTime(2023, 5, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CompletedYear = 2023,
            CompletedOn = new DateOnly(2023, 9, 15)
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var audit = new TestAuditService();
        var service = new ProjectLifecycleService(db, audit, clock);

        var result = await service.MarkCompletedAsync(7, "actor", 2024);

        Assert.Equal(ProjectLifecycleOperationStatus.InvalidStatus, result.Status);
        Assert.Equal("Project must be active or awaiting endorsement to update completion details.", result.ErrorMessage);
        var project = await db.Projects.SingleAsync(p => p.Id == 7);
        Assert.Equal(2023, project.CompletedYear);
        Assert.Equal(new DateOnly(2023, 9, 15), project.CompletedOn);
        Assert.Empty(audit.Logs);
    }

    [Fact]
    public async Task EndorseCompletion_WhenValid_SetsDateYearAndLogs()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 10,
            Name = "Delta",
            CreatedAt = new DateTime(2023, 1, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CompletedYear = 2023
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var audit = new TestAuditService();
        var service = new ProjectLifecycleService(db, audit, clock);

        var result = await service.EndorseCompletionAsync(10, "actor", new DateOnly(2023, 12, 15));

        Assert.True(result.IsSuccess);
        var project = await db.Projects.SingleAsync(p => p.Id == 10);
        Assert.Equal(new DateOnly(2023, 12, 15), project.CompletedOn);
        Assert.Equal(2023, project.CompletedYear);

        var entry = Assert.Single(audit.Logs);
        Assert.Equal("Project.LifecycleCompletionUpdated", entry.Action);
        Assert.Equal("2023-12-15", entry.Data["CompletionDate"]);
        Assert.Equal("2023", entry.Data["CompletionYear"]);
        Assert.Equal("ExactDate", entry.Data["Precision"]);
    }

    [Fact]
    public async Task EndorseCompletion_WhenYearMissing_ReturnsInvalidStatus()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 11,
            Name = "Epsilon",
            CreatedAt = new DateTime(2023, 1, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var audit = new TestAuditService();
        var service = new ProjectLifecycleService(db, audit, clock);

        var result = await service.EndorseCompletionAsync(11, "actor", new DateOnly(2023, 8, 20));

        Assert.Equal(ProjectLifecycleOperationStatus.InvalidStatus, result.Status);
        Assert.Equal("Set a completion year before endorsing an exact date.", result.ErrorMessage);
        var project = await db.Projects.SingleAsync(p => p.Id == 11);
        Assert.Null(project.CompletedOn);
        Assert.Null(project.CompletedYear);
        Assert.Empty(audit.Logs);
    }

    [Fact]
    public async Task UpdateCompletion_WithExactDate_StoresExactDateAndDerivedYear()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 12,
            Name = "Exact completion",
            CreatedAt = new DateTime(2023, 1, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Active
        });
        await db.SaveChangesAsync();

        var service = new ProjectLifecycleService(
            db,
            new TestAuditService(),
            new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero)));

        var result = await service.UpdateCompletionAsync(
            12,
            "actor",
            ProjectCompletionValue.Exact(new DateOnly(2024, 7, 25)));

        Assert.True(result.IsSuccess);
        var project = await db.Projects.SingleAsync(item => item.Id == 12);
        Assert.Equal(ProjectLifecycleStatus.Completed, project.LifecycleStatus);
        Assert.Equal(new DateOnly(2024, 7, 25), project.CompletedOn);
        Assert.Equal(2024, project.CompletedYear);
        Assert.Null(project.CompletedMonth);
    }

    [Fact]
    public async Task UpdateCompletion_WithMonthAndYear_DoesNotInventCompletionDay()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 13,
            Name = "Month completion",
            CreatedAt = new DateTime(2023, 1, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Active
        });
        await db.SaveChangesAsync();

        var audit = new TestAuditService();
        var service = new ProjectLifecycleService(
            db,
            audit,
            new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero)));

        var result = await service.UpdateCompletionAsync(
            13,
            "actor",
            ProjectCompletionValue.MonthAndYear(2024, 7));

        Assert.True(result.IsSuccess);
        var project = await db.Projects.SingleAsync(item => item.Id == 13);
        Assert.Null(project.CompletedOn);
        Assert.Equal(2024, project.CompletedYear);
        Assert.Equal<short?>((short)7, project.CompletedMonth);

        var entry = Assert.Single(audit.Logs);
        Assert.Equal("MonthAndYear", entry.Data["Precision"]);
        Assert.Equal("7", entry.Data["CompletionMonth"]);
    }

    [Fact]
    public async Task UpdateCompletion_WithUnknownDate_ClearsEarlierCompletionInformation()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 14,
            Name = "Unknown completion",
            CreatedAt = new DateTime(2023, 1, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Completed,
            CompletedOn = new DateOnly(2024, 4, 2),
            CompletedYear = 2024
        });
        await db.SaveChangesAsync();

        var service = new ProjectLifecycleService(
            db,
            new TestAuditService(),
            new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero)));

        var result = await service.UpdateCompletionAsync(14, "actor", ProjectCompletionValue.NotKnown());

        Assert.True(result.IsSuccess);
        var project = await db.Projects.SingleAsync(item => item.Id == 14);
        Assert.Equal(ProjectLifecycleStatus.Completed, project.LifecycleStatus);
        Assert.Null(project.CompletedOn);
        Assert.Null(project.CompletedYear);
        Assert.Null(project.CompletedMonth);
    }

    [Fact]
    public async Task UpdateCompletion_WithFutureMonth_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 15,
            Name = "Future completion",
            CreatedAt = new DateTime(2023, 1, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Active
        });
        await db.SaveChangesAsync();

        var service = new ProjectLifecycleService(
            db,
            new TestAuditService(),
            new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero)));

        var result = await service.UpdateCompletionAsync(
            15,
            "actor",
            ProjectCompletionValue.MonthAndYear(2024, 11));

        Assert.Equal(ProjectLifecycleOperationStatus.ValidationFailed, result.Status);
        Assert.Equal("Completion month cannot be in the future.", result.ErrorMessage);
        var project = await db.Projects.SingleAsync(item => item.Id == 15);
        Assert.Equal(ProjectLifecycleStatus.Active, project.LifecycleStatus);
    }

    [Fact]
    public async Task CancelProject_WhenActive_SetsFieldsAndLogs()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 20,
            Name = "Zeta",
            CreatedAt = new DateTime(2023, 6, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Active,
            CompletedYear = 2023
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var audit = new TestAuditService();
        var service = new ProjectLifecycleService(db, audit, clock);

        var result = await service.CancelProjectAsync(20, "actor", new DateOnly(2024, 4, 2), " Budget withdrawn ");

        Assert.True(result.IsSuccess);
        var project = await db.Projects.SingleAsync(p => p.Id == 20);
        Assert.Equal(ProjectLifecycleStatus.Cancelled, project.LifecycleStatus);
        Assert.Equal(new DateOnly(2024, 4, 2), project.CancelledOn);
        Assert.Equal("Budget withdrawn", project.CancelReason);
        Assert.Null(project.CompletedYear);
        Assert.Null(project.CompletedOn);

        var entry = Assert.Single(audit.Logs);
        Assert.Equal("Project.LifecycleCancelled", entry.Action);
        Assert.Equal("Budget withdrawn", entry.Data["Reason"]);
        Assert.Equal("2024-04-02", entry.Data["CancelledOn"]);
    }

    [Fact]
    public async Task CancelProject_WithMissingReason_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 21,
            Name = "Eta",
            CreatedAt = new DateTime(2023, 6, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Active
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var audit = new TestAuditService();
        var service = new ProjectLifecycleService(db, audit, clock);

        var result = await service.CancelProjectAsync(21, "actor", new DateOnly(2024, 4, 2), " ");

        Assert.Equal(ProjectLifecycleOperationStatus.ValidationFailed, result.Status);
        Assert.Equal("Cancellation reason is required.", result.ErrorMessage);
        var project = await db.Projects.SingleAsync(p => p.Id == 21);
        Assert.Equal(ProjectLifecycleStatus.Active, project.LifecycleStatus);
        Assert.Null(project.CancelledOn);
        Assert.Empty(audit.Logs);
    }

    [Fact]
    public async Task CancelProject_WithFutureDate_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 22,
            Name = "Theta",
            CreatedAt = new DateTime(2023, 6, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Active
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var audit = new TestAuditService();
        var service = new ProjectLifecycleService(db, audit, clock);

        var result = await service.CancelProjectAsync(22, "actor", new DateOnly(2025, 1, 1), "Reason");

        Assert.Equal(ProjectLifecycleOperationStatus.ValidationFailed, result.Status);
        Assert.Equal("Cancellation date cannot be in the future.", result.ErrorMessage);
        var project = await db.Projects.SingleAsync(p => p.Id == 22);
        Assert.Null(project.CancelledOn);
        Assert.Empty(audit.Logs);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class TestAuditService : IAuditService
    {
        public List<AuditEntry> Logs { get; } = new();

        public Task LogAsync(string action, string? message = null, string level = "Info", string? userId = null, string? userName = null, IDictionary<string, string?>? data = null, HttpContext? http = null)
        {
            var snapshot = data is null
                ? new Dictionary<string, string?>()
                : data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            Logs.Add(new AuditEntry(action, userId, snapshot));
            return Task.CompletedTask;
        }
    }

    private sealed record AuditEntry(string Action, string? UserId, IDictionary<string, string?> Data);
}
