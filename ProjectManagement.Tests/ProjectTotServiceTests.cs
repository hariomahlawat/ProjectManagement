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
            CreateRequest(ProjectTotStatus.NotStarted),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task UpdateAsync_WhenSetToNotRequired_ClearsDates()
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
            MetDetails = "Previous MET milestone",
            MetCompletedOn = new DateOnly(2024, 3, 10),
            FirstProductionModelManufactured = true,
            FirstProductionModelManufacturedOn = new DateOnly(2024, 3, 12)
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            1,
            CreateRequest(ProjectTotStatus.NotRequired),
            "actor");

        Assert.True(result.IsSuccess);
        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 1);
        Assert.Equal(ProjectTotStatus.NotRequired, tot.Status);
        Assert.Null(tot.StartedOn);
        Assert.Null(tot.CompletedOn);
        Assert.Null(tot.MetDetails);
        Assert.Null(tot.MetCompletedOn);
        Assert.Null(tot.FirstProductionModelManufactured);
        Assert.Null(tot.FirstProductionModelManufacturedOn);
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
            StartedOn = new DateOnly(2024, 1, 10)
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            41,
            CreateRequest(ProjectTotStatus.NotRequired),
            "actor");

        Assert.True(result.IsSuccess);

        db.ChangeTracker.Clear();

        var persisted = await db.ProjectTots
            .AsNoTracking()
            .SingleAsync(t => t.ProjectId == 41);

        Assert.Equal(ProjectTotStatus.NotRequired, persisted.Status);
        Assert.Null(persisted.StartedOn);
        Assert.Null(persisted.CompletedOn);
        Assert.Null(persisted.MetDetails);
        Assert.Null(persisted.MetCompletedOn);
        Assert.Null(persisted.FirstProductionModelManufactured);
        Assert.Null(persisted.FirstProductionModelManufacturedOn);
    }

    [Fact]
    public async Task SubmitRequestAsync_PersistsSubmittedByUserId_WithSqlite()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var previousSubmitter = new ApplicationUser
        {
            Id = "old-user",
            UserName = "old-user",
            NormalizedUserName = "OLD-USER"
        };
        var newSubmitter = new ApplicationUser
        {
            Id = "new-user",
            UserName = "new-user",
            NormalizedUserName = "NEW-USER"
        };

        db.Users.Add(previousSubmitter);
        db.Users.Add(newSubmitter);

        var project = new Project
        {
            Id = 50,
            Name = "Sigma",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator",
            LifecycleStatus = ProjectLifecycleStatus.Completed
        };

        var tot = new ProjectTot
        {
            ProjectId = 50,
            Status = ProjectTotStatus.NotStarted
        };

        var existingRequest = new ProjectTotRequest
        {
            ProjectId = 50,
            ProposedStatus = ProjectTotStatus.NotStarted,
            SubmittedByUserId = previousSubmitter.Id,
            SubmittedByUser = previousSubmitter,
            SubmittedOnUtc = new DateTime(2024, 1, 2),
            DecisionState = ProjectTotRequestDecisionState.Rejected,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        project.Tot = tot;
        project.TotRequest = existingRequest;

        db.Projects.Add(project);
        db.ProjectTots.Add(tot);
        db.ProjectTotRequests.Add(existingRequest);

        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var submitResult = await service.SubmitRequestAsync(
            50,
            CreateRequest(
                ProjectTotStatus.InProgress,
                startedOn: new DateOnly(2024, 2, 1)),
            newSubmitter.Id);

        Assert.True(submitResult.IsSuccess);

        db.ChangeTracker.Clear();

        var persisted = await db.ProjectTotRequests
            .AsNoTracking()
            .SingleAsync(r => r.ProjectId == 50);

        Assert.Equal(newSubmitter.Id, persisted.SubmittedByUserId);
        Assert.Equal(ProjectTotRequestDecisionState.Pending, persisted.DecisionState);
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
            CreateRequest(ProjectTotStatus.InProgress),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, result.Status);
        Assert.Equal("Start date (year or month and year) is required when ToT is in progress.", result.ErrorMessage);

        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 5);
        Assert.Equal(ProjectTotStatus.NotStarted, tot.Status);
        Assert.Null(tot.StartedOn);
        Assert.Null(tot.CompletedOn);
    }

    [Fact]
    public async Task UpdateAsync_CompletedRequiresDates_TrimsMetDetails()
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
            CreateRequest(
                ProjectTotStatus.Completed,
                startedOn: new DateOnly(2024, 2, 1),
                completedOn: new DateOnly(2024, 5, 20),
                metDetails: "  MET delivered  ",
                metCompletedOn: new DateOnly(2024, 3, 5),
                firstProductionModelManufactured: true,
                firstProductionModelManufacturedOn: new DateOnly(2024, 4, 1)),
            "actor");

        Assert.True(result.IsSuccess);
        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 7);
        Assert.Equal(ProjectTotStatus.Completed, tot.Status);
        Assert.Equal(new DateOnly(2024, 2, 1), tot.StartedOn);
        Assert.Equal(new DateOnly(2024, 5, 20), tot.CompletedOn);
        Assert.Equal("MET delivered", tot.MetDetails);
        Assert.Equal(new DateOnly(2024, 3, 5), tot.MetCompletedOn);
        Assert.True(tot.FirstProductionModelManufactured);
        Assert.Equal(new DateOnly(2024, 4, 1), tot.FirstProductionModelManufacturedOn);
    }

    [Fact]
    public async Task UpdateAsync_CompletedAllowsCompletionWithoutStart()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 8,
            Name = "Project Nova",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });

        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            8,
            CreateRequest(
                ProjectTotStatus.Completed,
                completedOn: new DateOnly(2023, 12, 31)),
            "actor");

        Assert.True(result.IsSuccess);
        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 8);
        Assert.Equal(ProjectTotStatus.Completed, tot.Status);
        Assert.Null(tot.StartedOn);
        Assert.Equal(new DateOnly(2023, 12, 31), tot.CompletedOn);
    }

    [Fact]
    public async Task UpdateAsync_CompletedRequiresCompletionDate()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 8,
            Name = "Project Vega",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });

        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            8,
            CreateRequest(ProjectTotStatus.Completed),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, result.Status);
        Assert.Equal("Completion date (year or month and year) is required when ToT is completed.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_InProgressAcceptsYearOnlyStart()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 9,
            Name = "Project Orion",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });

        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            9,
            CreateRequest(
                ProjectTotStatus.InProgress,
                startedOn: new DateOnly(2022, 1, 1)),
            "actor");

        Assert.True(result.IsSuccess);
        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 9);
        Assert.Equal(new DateOnly(2022, 1, 1), tot.StartedOn);
        Assert.Null(tot.CompletedOn);
    }

    [Fact]
    public async Task UpdateAsync_WhenMetCompletedBeforeStart_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 9,
            Name = "Project Helios",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });

        db.ProjectTots.Add(new ProjectTot
        {
            ProjectId = 9,
            Status = ProjectTotStatus.NotStarted
        });

        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            9,
            CreateRequest(
                ProjectTotStatus.InProgress,
                startedOn: new DateOnly(2024, 5, 10),
                metCompletedOn: new DateOnly(2024, 5, 5)),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, result.Status);
        Assert.Equal("MET completion date cannot be earlier than the ToT start date.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_WhenFirstProductionBeyondCompletion_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 10,
            Name = "Project Lumen",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });

        db.ProjectTots.Add(new ProjectTot
        {
            ProjectId = 10,
            Status = ProjectTotStatus.NotStarted
        });

        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            10,
            CreateRequest(
                ProjectTotStatus.Completed,
                startedOn: new DateOnly(2024, 6, 1),
                completedOn: new DateOnly(2024, 6, 30),
                firstProductionModelManufactured: true,
                firstProductionModelManufacturedOn: new DateOnly(2024, 7, 2)),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, result.Status);
        Assert.Equal("First production model date cannot be later than the ToT completion date.", result.ErrorMessage);

        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 10);
        Assert.Equal(ProjectTotStatus.NotStarted, tot.Status);
        Assert.Null(tot.StartedOn);
        Assert.Null(tot.CompletedOn);
    }

    [Fact]
    public async Task UpdateAsync_WhenFoPmMarkedTrueWithoutDate_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 21,
            Name = "Theta",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });
        db.ProjectTots.Add(new ProjectTot
        {
            ProjectId = 21,
            Status = ProjectTotStatus.NotStarted
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            21,
            CreateRequest(
                ProjectTotStatus.InProgress,
                startedOn: new DateOnly(2024, 8, 1),
                firstProductionModelManufactured: true),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, result.Status);
        Assert.Equal("First production model manufacture date is required when marked as manufactured.", result.ErrorMessage);

        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 21);
        Assert.Equal(ProjectTotStatus.NotStarted, tot.Status);
        Assert.Null(tot.FirstProductionModelManufactured);
        Assert.Null(tot.FirstProductionModelManufacturedOn);
    }

    [Fact]
    public async Task UpdateAsync_WhenFoPmDateProvidedWithoutFlag_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 22,
            Name = "Iota",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });
        db.ProjectTots.Add(new ProjectTot
        {
            ProjectId = 22,
            Status = ProjectTotStatus.NotStarted
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            22,
            CreateRequest(
                ProjectTotStatus.InProgress,
                startedOn: new DateOnly(2024, 8, 1),
                firstProductionModelManufacturedOn: new DateOnly(2024, 9, 1)),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, result.Status);
        Assert.Equal("First production model manufacture date must be empty unless marked as manufactured.", result.ErrorMessage);

        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 22);
        Assert.Equal(ProjectTotStatus.NotStarted, tot.Status);
        Assert.Null(tot.FirstProductionModelManufactured);
        Assert.Null(tot.FirstProductionModelManufacturedOn);
    }

    [Fact]
    public async Task UpdateAsync_WhenMetCompletionInFuture_ReturnsValidationError()
    {
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = 23,
            Name = "Kappa",
            CreatedAt = new DateTime(2024, 1, 1),
            CreatedByUserId = "creator"
        });
        db.ProjectTots.Add(new ProjectTot
        {
            ProjectId = 23,
            Status = ProjectTotStatus.NotStarted
        });
        await db.SaveChangesAsync();

        var clock = new FixedClock(new DateTimeOffset(2024, 10, 8, 6, 0, 0, TimeSpan.Zero));
        var service = new ProjectTotService(db, clock);

        var result = await service.UpdateAsync(
            23,
            CreateRequest(
                ProjectTotStatus.InProgress,
                startedOn: new DateOnly(2024, 8, 1),
                metCompletedOn: new DateOnly(2024, 10, 9)),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, result.Status);
        Assert.Equal("MET completion date cannot be in the future.", result.ErrorMessage);

        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 23);
        Assert.Equal(ProjectTotStatus.NotStarted, tot.Status);
        Assert.Null(tot.MetCompletedOn);
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
            CreateRequest(
                ProjectTotStatus.Completed,
                startedOn: new DateOnly(2024, 9, 1),
                completedOn: new DateOnly(2025, 1, 1)),
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
            CreateRequest(
                ProjectTotStatus.Completed,
                startedOn: new DateOnly(2024, 5, 10),
                completedOn: new DateOnly(2024, 5, 5)),
            "actor");

        Assert.Equal(ProjectTotUpdateStatus.ValidationFailed, result.Status);
        Assert.Equal("Completion date cannot be earlier than the start date.", result.ErrorMessage);

        var tot = await db.ProjectTots.SingleAsync(t => t.ProjectId == 15);
        Assert.Equal(ProjectTotStatus.NotStarted, tot.Status);
        Assert.Null(tot.StartedOn);
        Assert.Null(tot.CompletedOn);
    }

    private static ProjectTotUpdateRequest CreateRequest(
        ProjectTotStatus status,
        DateOnly? startedOn = null,
        DateOnly? completedOn = null,
        string? metDetails = null,
        DateOnly? metCompletedOn = null,
        bool? firstProductionModelManufactured = null,
        DateOnly? firstProductionModelManufacturedOn = null) =>
        new(
            status,
            startedOn,
            completedOn,
            metDetails,
            metCompletedOn,
            firstProductionModelManufactured,
            firstProductionModelManufacturedOn);

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
