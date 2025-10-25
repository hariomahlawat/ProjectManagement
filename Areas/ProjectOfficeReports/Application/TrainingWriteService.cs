using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class TrainingWriteService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public TrainingWriteService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<TrainingMutationResult> CreateAsync(TrainingMutationCommand command, string userId, CancellationToken cancellationToken)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return TrainingMutationResult.Failure(TrainingMutationFailureCode.MissingUserId, "The current user context is not available.");
        }

        var type = await _db.TrainingTypes
            .FirstOrDefaultAsync(x => x.Id == command.TrainingTypeId, cancellationToken);

        if (type is null)
        {
            return TrainingMutationResult.Failure(TrainingMutationFailureCode.TrainingTypeNotFound, "The selected training type could not be found.");
        }

        if (!type.IsActive)
        {
            return TrainingMutationResult.Failure(TrainingMutationFailureCode.TrainingTypeInactive, "The selected training type is inactive.");
        }

        var validation = await ValidateProjectsAsync(command.ProjectIds, cancellationToken);
        if (!validation.Success)
        {
            return TrainingMutationResult.Failure(TrainingMutationFailureCode.InvalidProjects, validation.ErrorMessage);
        }

        var now = _clock.UtcNow;
        var trainingId = Guid.NewGuid();
        var rowVersion = Guid.NewGuid().ToByteArray();

        var training = new Training
        {
            Id = trainingId,
            TrainingTypeId = command.TrainingTypeId,
            StartDate = command.StartDate,
            EndDate = command.EndDate,
            TrainingMonth = command.TrainingMonth,
            TrainingYear = command.TrainingYear,
            LegacyOfficerCount = command.LegacyOfficers,
            LegacyJcoCount = command.LegacyJcos,
            LegacyOrCount = command.LegacyOrs,
            Notes = NormalizeNotes(command.Notes),
            CreatedAtUtc = now,
            CreatedByUserId = userId,
            LastModifiedAtUtc = now,
            LastModifiedByUserId = userId,
            RowVersion = rowVersion
        };

        foreach (var projectId in validation.ProjectIds)
        {
            training.ProjectLinks.Add(new TrainingProject
            {
                TrainingId = trainingId,
                ProjectId = projectId,
                AllocationShare = 0,
                RowVersion = Guid.NewGuid().ToByteArray()
            });
        }

        var counters = CreateCounters(trainingId, command.LegacyOfficers, command.LegacyJcos, command.LegacyOrs, now);
        training.Counters = counters;

        _db.Trainings.Add(training);
        _db.TrainingCounters.Add(counters);

        await _db.SaveChangesAsync(cancellationToken);

        return TrainingMutationResult.Success(trainingId, rowVersion);
    }

    public async Task<TrainingMutationResult> UpdateAsync(Guid id, TrainingMutationCommand command, byte[]? expectedRowVersion, string userId, CancellationToken cancellationToken)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return TrainingMutationResult.Failure(TrainingMutationFailureCode.MissingUserId, "The current user context is not available.");
        }

        var training = await _db.Trainings
            .Include(x => x.ProjectLinks)
            .Include(x => x.Counters)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (training is null)
        {
            return TrainingMutationResult.Failure(TrainingMutationFailureCode.TrainingNotFound, "The training could not be found.");
        }

        if (expectedRowVersion is not null && !training.RowVersion.SequenceEqual(expectedRowVersion))
        {
            return TrainingMutationResult.Failure(TrainingMutationFailureCode.ConcurrencyConflict, "The training was updated by another user.");
        }

        var type = await _db.TrainingTypes
            .FirstOrDefaultAsync(x => x.Id == command.TrainingTypeId, cancellationToken);

        if (type is null)
        {
            return TrainingMutationResult.Failure(TrainingMutationFailureCode.TrainingTypeNotFound, "The selected training type could not be found.");
        }

        if (!type.IsActive)
        {
            return TrainingMutationResult.Failure(TrainingMutationFailureCode.TrainingTypeInactive, "The selected training type is inactive.");
        }

        var validation = await ValidateProjectsAsync(command.ProjectIds, cancellationToken);
        if (!validation.Success)
        {
            return TrainingMutationResult.Failure(TrainingMutationFailureCode.InvalidProjects, validation.ErrorMessage);
        }

        var now = _clock.UtcNow;

        training.TrainingTypeId = command.TrainingTypeId;
        training.StartDate = command.StartDate;
        training.EndDate = command.EndDate;
        training.TrainingMonth = command.TrainingMonth;
        training.TrainingYear = command.TrainingYear;
        training.LegacyOfficerCount = command.LegacyOfficers;
        training.LegacyJcoCount = command.LegacyJcos;
        training.LegacyOrCount = command.LegacyOrs;
        training.Notes = NormalizeNotes(command.Notes);
        training.LastModifiedAtUtc = now;
        training.LastModifiedByUserId = userId;
        training.RowVersion = Guid.NewGuid().ToByteArray();

        UpdateProjectLinks(training, validation.ProjectIds);
        UpdateCounters(training, command.LegacyOfficers, command.LegacyJcos, command.LegacyOrs, now);

        await _db.SaveChangesAsync(cancellationToken);

        return TrainingMutationResult.Success(training.Id, training.RowVersion);
    }

    private static TrainingCounters CreateCounters(Guid trainingId, int officers, int jcos, int ors, DateTimeOffset timestamp)
    {
        return new TrainingCounters
        {
            TrainingId = trainingId,
            Officers = officers,
            JuniorCommissionedOfficers = jcos,
            OtherRanks = ors,
            Total = officers + jcos + ors,
            Source = TrainingCounterSource.Legacy,
            UpdatedAtUtc = timestamp,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
    }

    private static void UpdateCounters(Training training, int officers, int jcos, int ors, DateTimeOffset timestamp)
    {
        var counters = training.Counters;
        if (counters is null)
        {
            counters = CreateCounters(training.Id, officers, jcos, ors, timestamp);
            training.Counters = counters;
        }
        else
        {
            counters.Officers = officers;
            counters.JuniorCommissionedOfficers = jcos;
            counters.OtherRanks = ors;
            counters.Total = officers + jcos + ors;
            counters.Source = TrainingCounterSource.Legacy;
            counters.UpdatedAtUtc = timestamp;
            counters.RowVersion = Guid.NewGuid().ToByteArray();
        }
    }

    private static string? NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        return notes.Trim();
    }

    private async Task<ProjectValidationResult> ValidateProjectsAsync(IEnumerable<int> projectIds, CancellationToken cancellationToken)
    {
        var ids = projectIds?.Distinct().ToList() ?? new List<int>();
        if (ids.Count == 0)
        {
            return ProjectValidationResult.Successful(ids);
        }

        var existing = await _db.Projects
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id) && !x.IsDeleted && !x.IsArchived)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (existing.Count != ids.Count)
        {
            return ProjectValidationResult.Failure("One or more selected projects are not available.");
        }

        return ProjectValidationResult.Successful(existing);
    }

    private void UpdateProjectLinks(Training training, IReadOnlyCollection<int> desiredProjectIds)
    {
        var current = training.ProjectLinks.Select(link => link.ProjectId).ToHashSet();
        var desired = desiredProjectIds.ToHashSet();

        foreach (var link in training.ProjectLinks.Where(link => !desired.Contains(link.ProjectId)).ToList())
        {
            training.ProjectLinks.Remove(link);
            _db.TrainingProjects.Remove(link);
        }

        foreach (var projectId in desired.Where(id => !current.Contains(id)))
        {
            training.ProjectLinks.Add(new TrainingProject
            {
                TrainingId = training.Id,
                ProjectId = projectId,
                AllocationShare = 0,
                RowVersion = Guid.NewGuid().ToByteArray()
            });
        }
    }

    private sealed record ProjectValidationResult(bool Success, string? ErrorMessage, IReadOnlyCollection<int> ProjectIds)
    {
        public static ProjectValidationResult Successful(IReadOnlyCollection<int> projectIds) => new(true, null, projectIds);

        public static ProjectValidationResult Failure(string message) => new(false, message, Array.Empty<int>());
    }
}

public sealed record TrainingMutationCommand(
    Guid TrainingTypeId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? TrainingMonth,
    int? TrainingYear,
    int LegacyOfficers,
    int LegacyJcos,
    int LegacyOrs,
    string? Notes,
    IReadOnlyCollection<int> ProjectIds);

public sealed record TrainingMutationResult(
    bool Success,
    TrainingMutationFailureCode FailureCode,
    string? ErrorMessage,
    Guid? TrainingId,
    byte[]? RowVersion)
{
    public static TrainingMutationResult Success(Guid trainingId, byte[] rowVersion) => new(true, TrainingMutationFailureCode.None, null, trainingId, rowVersion);

    public static TrainingMutationResult Failure(TrainingMutationFailureCode code, string? message) => new(false, code, message, null, null);
};

public enum TrainingMutationFailureCode
{
    None = 0,
    TrainingTypeNotFound = 1,
    TrainingTypeInactive = 2,
    InvalidProjects = 3,
    TrainingNotFound = 4,
    ConcurrencyConflict = 5,
    MissingUserId = 6
}
