using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services.ProjectOfficeReports.Training;
using ProjectManagement.Tests.Fakes;
using TrainingEntity = ProjectManagement.Areas.ProjectOfficeReports.Domain.Training;

namespace ProjectManagement.Tests.ProjectOfficeReports.Training;

public sealed class TrainingWriteServiceTests
{
    [Fact]
    public async Task UpdateAsync_PreservesRosterCounters_WhenRosterExists()
    {
        var initialTimestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = FakeClock.AtUtc(initialTimestamp);
        await using var db = CreateDbContext();
        var sut = CreateService(db, clock);

        var trainingType = new TrainingType
        {
            Id = Guid.NewGuid(),
            Name = "Signals Refresher",
            IsActive = true,
            CreatedAtUtc = initialTimestamp,
            CreatedByUserId = "seed"
        };
        db.TrainingTypes.Add(trainingType);

        var training = new TrainingEntity
        {
            Id = Guid.NewGuid(),
            TrainingTypeId = trainingType.Id,
            StartDate = new DateOnly(2024, 1, 1),
            EndDate = new DateOnly(2024, 1, 10),
            TrainingMonth = 1,
            TrainingYear = 2024,
            LegacyOfficerCount = 7,
            LegacyJcoCount = 8,
            LegacyOrCount = 9,
            Notes = "Legacy snapshot",
            CreatedAtUtc = initialTimestamp,
            CreatedByUserId = "seed",
            LastModifiedAtUtc = initialTimestamp,
            LastModifiedByUserId = "seed",
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        var counters = new TrainingCounters
        {
            TrainingId = training.Id,
            Officers = 9,
            JuniorCommissionedOfficers = 9,
            OtherRanks = 9,
            Total = 27,
            Source = TrainingCounterSource.Legacy,
            UpdatedAtUtc = initialTimestamp,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        training.Counters = counters;

        db.Trainings.Add(training);
        db.TrainingCounters.Add(counters);

        db.TrainingTrainees.AddRange(
            new TrainingTrainee { TrainingId = training.Id, Rank = "Col", Name = "Alpha", UnitName = "Unit", Category = 0 },
            new TrainingTrainee { TrainingId = training.Id, Rank = "Subedar", Name = "Bravo", UnitName = "Unit", Category = 1 },
            new TrainingTrainee { TrainingId = training.Id, Rank = "Subedar", Name = "Charlie", UnitName = "Unit", Category = 1 },
            new TrainingTrainee { TrainingId = training.Id, Rank = "Sep", Name = "Delta", UnitName = "Unit", Category = 2 },
            new TrainingTrainee { TrainingId = training.Id, Rank = "Sep", Name = "Echo", UnitName = "Unit", Category = 2 },
            new TrainingTrainee { TrainingId = training.Id, Rank = "Sep", Name = "Foxtrot", UnitName = "Unit", Category = 2 });

        await db.SaveChangesAsync();

        clock.Set(initialTimestamp.AddHours(1));

        var command = new TrainingMutationCommand(
            trainingType.Id,
            training.StartDate,
            training.EndDate,
            training.TrainingMonth,
            training.TrainingYear,
            LegacyOfficers: 40,
            LegacyJcos: 50,
            LegacyOrs: 60,
            Notes: training.Notes,
            ProjectIds: Array.Empty<int>());

        var result = await sut.UpdateAsync(training.Id, command, training.RowVersion, "editor", CancellationToken.None);

        Assert.True(result.IsSuccess);

        var countersEntity = await db.TrainingCounters
            .AsNoTracking()
            .SingleAsync(x => x.TrainingId == training.Id);

        Assert.Equal(1, countersEntity.Officers);
        Assert.Equal(2, countersEntity.JuniorCommissionedOfficers);
        Assert.Equal(3, countersEntity.OtherRanks);
        Assert.Equal(6, countersEntity.Total);
        Assert.Equal(TrainingCounterSource.Roster, countersEntity.Source);
    }

    [Fact]
    public async Task UpsertRosterAsync_SetsCountersFromRoster_AndMarksSourceRoster()
    {
        var initialTimestamp = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = FakeClock.AtUtc(initialTimestamp);
        await using var db = CreateDbContext();
        var sut = CreateService(db, clock);

        var trainingType = new TrainingType
        {
            Id = Guid.NewGuid(),
            Name = "Logistics Workshop",
            IsActive = true,
            CreatedAtUtc = initialTimestamp,
            CreatedByUserId = "seed"
        };
        db.TrainingTypes.Add(trainingType);

        var training = new TrainingEntity
        {
            Id = Guid.NewGuid(),
            TrainingTypeId = trainingType.Id,
            TrainingMonth = 2,
            TrainingYear = 2024,
            LegacyOfficerCount = 2,
            LegacyJcoCount = 2,
            LegacyOrCount = 2,
            CreatedAtUtc = initialTimestamp,
            CreatedByUserId = "seed",
            LastModifiedAtUtc = initialTimestamp,
            LastModifiedByUserId = "seed",
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        var counters = new TrainingCounters
        {
            TrainingId = training.Id,
            Officers = 2,
            JuniorCommissionedOfficers = 2,
            OtherRanks = 2,
            Total = 6,
            Source = TrainingCounterSource.Legacy,
            UpdatedAtUtc = initialTimestamp,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        training.Counters = counters;

        db.Trainings.Add(training);
        db.TrainingCounters.Add(counters);

        await db.SaveChangesAsync();

        clock.Set(initialTimestamp.AddMinutes(30));

        var rosterRows = new[]
        {
            new TrainingRosterRow { Rank = "Maj", Name = "Alpha", UnitName = "Unit", Category = 0 },
            new TrainingRosterRow { Rank = "Sep", Name = "Bravo", UnitName = "Unit", Category = 2 }
        };

        var result = await sut.UpsertRosterAsync(training.Id, rosterRows, training.RowVersion, "editor", CancellationToken.None);

        Assert.True(result.IsSuccess);

        var countersEntity = await db.TrainingCounters
            .AsNoTracking()
            .SingleAsync(x => x.TrainingId == training.Id);

        Assert.Equal(1, countersEntity.Officers);
        Assert.Equal(0, countersEntity.JuniorCommissionedOfficers);
        Assert.Equal(1, countersEntity.OtherRanks);
        Assert.Equal(2, countersEntity.Total);
        Assert.Equal(TrainingCounterSource.Roster, countersEntity.Source);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableSensitiveDataLogging()
            .Options;

        return new ApplicationDbContext(options);
    }

    private static TrainingWriteService CreateService(ApplicationDbContext db, FakeClock clock)
        => new(db, clock, new NoOpTrainingNotificationService(), NullLogger<TrainingWriteService>.Instance);

    private sealed class NoOpTrainingNotificationService : ITrainingNotificationService
    {
        public Task NotifyDeleteApprovedAsync(TrainingDeleteNotificationContext context, string approverUserId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task NotifyDeleteRejectedAsync(TrainingDeleteNotificationContext context, string approverUserId, string decisionNotes, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task NotifyDeleteRequestedAsync(TrainingDeleteNotificationContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
