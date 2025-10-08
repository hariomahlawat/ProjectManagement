using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class ProjectTotServiceTests
{
    [Fact]
    public async Task UpdateAsync_WhenProjectMissing_ReturnsNotFound()
    {
        await using var db = CreateContext();
        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            42,
            new ProjectTotUpdateRequest(ProjectTotStatus.NotStarted, null, null, null),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task UpdateAsync_WhenSetToNotRequired_ClearsDatesAndRemarks()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 1,
            Name = "Alpha",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });
        db.ProjectTots.Add(new ProjectTot
        {
            ProjectId = 1,
            Status = ProjectTotStatus.InProgress,
            StartedOn = new DateOnly(2024, 2, 1),
            CompletedOn = new DateOnly(2024, 3, 15),
            Remarks = "Some note"
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            1,
            new ProjectTotUpdateRequest(ProjectTotStatus.NotRequired, null, null, null),
            "actor");

        Assert.True(result.IsSuccess);
        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 1);
        Assert.Equal(ProjectTotStatus.NotRequired, tot.Status);
        Assert.Null(tot.StartedOn);
        Assert.Null(tot.CompletedOn);
        Assert.Null(tot.Remarks);
    }

    [Fact]
    public async Task UpdateAsync_WhenSetToNotRequired_PersistsStatus_WithSqlite()
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
            Id = 41,
            Name = "Lambda",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });
        db.ProjectTots.Add(new ProjectTot
        {
            ProjectId = 41,
            Status = ProjectTotStatus.NotStarted,
            StartedOn = new DateOnly(2024, 1, 10),
            Remarks = "Existing"
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            41,
            new ProjectTotUpdateRequest(ProjectTotStatus.NotRequired, null, null, null),
            "actor");

        Assert.True(result.IsSuccess);

        db.ChangeTracker.Clear();

        var persisted = await db.ProjectTots
            .AsNoTracking()
            .SingleAsync(t => t.ProjectId == 41);

        Assert.Equal(ProjectTotStatus.NotRequired, persisted.Status);
        Assert.Null(persisted.Remarks);
        Assert.Null(persisted.StartedOn);
        Assert.Null(persisted.CompletedOn);
    }

    [Fact]
    public async Task UpdateAsync_InProgressWithoutStart_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 5,
            Name = "Beta",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });
        db.ProjectTots.Add(new ProjectTot
        {
            ProjectId = 5,
            Status = ProjectTotStatus.NotStarted
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            5,
            new ProjectTotUpdateRequest(ProjectTotStatus.InProgress, null, null, null),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, result.Status);
        Assert.Equal("Start date is required when ToT is in progress.", result.ErrorMessage);

        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 5);
        Assert.Equal(ProjectTotStatus.NotStarted, tot.Status);
        Assert.Null(tot.StartedOn);
        Assert.Null(tot.CompletedOn);
    }

    [Fact]
    public async Task UpdateAsync_CompletedRequiresDates_TrimsRemarks()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 7,
            Name = "Gamma",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });
        db.ProjectTots.Add(new ProjectTot
        {
            ProjectId = 7,
            Status = ProjectTotStatus.InProgress,
            StartedOn = new DateOnly(2024, 2, 1)
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            7,
            new ProjectTotUpdateRequest(
                ProjectTotStatus.Completed,
                new DateOnly(2024, 2, 1),
                new DateOnly(2024, 5, 20),
                " Completed successfully "
            ),
            "actor");

        Assert.True(result.IsSuccess);
        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 7);
        Assert.Equal(ProjectTotStatus.Completed, tot.Status);
        Assert.Equal(new DateOnly(2024, 2, 1), tot.StartedOn);
        Assert.Equal(new DateOnly(2024, 5, 20), tot.CompletedOn);
        Assert.Equal("Completed successfully", tot.Remarks);
    }

    [Fact]
    public async Task UpdateAsync_CompletedWithFutureDate_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 11,
            Name = "Delta",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });
        db.ProjectTots.Add(new ProjectTot
        {
            ProjectId = 11,
            Status = ProjectTotStatus.NotStarted
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            11,
            new ProjectTotUpdateRequest(
                ProjectTotStatus.Completed,
                new DateOnly(2024, 9, 1),
                new DateOnly(2025, 1, 1),
                null),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, result.Status);
        Assert.Equal("Completion date cannot be in the future.", result.ErrorMessage);

        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 11);
        Assert.Equal(ProjectTotStatus.NotStarted, tot.Status);
        Assert.Null(tot.StartedOn);
        Assert.Null(tot.CompletedOn);
    }

    [Fact]
    public async Task UpdateAsync_CompletedWithEndBeforeStart_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 15,
            Name = "Epsilon",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });
        db.ProjectTots.Add(new ProjectTot
        {
            ProjectId = 15,
            Status = ProjectTotStatus.NotStarted
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            15,
            new ProjectTotUpdateRequest(
                ProjectTotStatus.Completed,
                new DateOnly(2024, 5, 10),
                new DateOnly(2024, 5, 5),
                null),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, result.Status);
        Assert.Equal("Completion date cannot be earlier than the start date.", result.ErrorMessage);

        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 15);
        Assert.Equal(ProjectTotStatus.NotStarted, tot.Status);
        Assert.Null(tot.StartedOn);
        Assert.Null(tot.CompletedOn);
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
}
