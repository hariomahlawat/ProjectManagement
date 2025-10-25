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

        var counters = CreateCounters(trainingId, command.LegacyOfficers, command.LegacyJcos, command.LegacyOrs, now, TrainingCounterSource.Legacy);
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
        UpdateCounters(training, command.LegacyOfficers, command.LegacyJcos, command.LegacyOrs, now, TrainingCounterSource.Legacy);

        await _db.SaveChangesAsync(cancellationToken);

        return TrainingMutationResult.Success(training.Id, training.RowVersion);
    }

    private static TrainingCounters CreateCounters(Guid trainingId, int officers, int jcos, int ors, DateTimeOffset timestamp, TrainingCounterSource source)
    {
        return new TrainingCounters
        {
            TrainingId = trainingId,
            Officers = officers,
            JuniorCommissionedOfficers = jcos,
            OtherRanks = ors,
            Total = officers + jcos + ors,
            Source = source,
            UpdatedAtUtc = timestamp,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
    }

    private static void UpdateCounters(Training training, int officers, int jcos, int ors, DateTimeOffset timestamp, TrainingCounterSource source)
    {
        var counters = training.Counters;
        if (counters is null)
        {
            counters = CreateCounters(training.Id, officers, jcos, ors, timestamp, source);
            training.Counters = counters;
        }
        else
        {
            counters.Officers = officers;
            counters.JuniorCommissionedOfficers = jcos;
            counters.OtherRanks = ors;
            counters.Total = officers + jcos + ors;
            counters.Source = source;
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

    public async Task<TrainingRosterUpdateResult> UpsertRosterAsync(
        Guid trainingId,
        IReadOnlyCollection<TrainingRosterRow> rows,
        byte[]? expectedRowVersion,
        string userId,
        CancellationToken cancellationToken)
    {
        if (trainingId == Guid.Empty)
        {
            return TrainingRosterUpdateResult.Failure(TrainingRosterFailureCode.InvalidRequest, "The training identifier is required.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return TrainingRosterUpdateResult.Failure(TrainingRosterFailureCode.MissingUserId, "The current user context is not available.");
        }

        var training = await _db.Trainings
            .Include(x => x.Counters)
            .FirstOrDefaultAsync(x => x.Id == trainingId, cancellationToken);

        if (training is null)
        {
            return TrainingRosterUpdateResult.Failure(TrainingRosterFailureCode.TrainingNotFound, "The training could not be found.");
        }

        if (expectedRowVersion is not null && expectedRowVersion.Length > 0 && !training.RowVersion.SequenceEqual(expectedRowVersion))
        {
            return TrainingRosterUpdateResult.Failure(TrainingRosterFailureCode.ConcurrencyConflict, "Another user has updated this training. Reload and try again.");
        }

        var normalization = await NormalizeRosterRowsAsync(rows, cancellationToken);
        if (!normalization.Success)
        {
            return TrainingRosterUpdateResult.Failure(TrainingRosterFailureCode.DuplicateArmyNumber, normalization.ErrorMessage);
        }

        var normalizedRows = normalization.Rows;

        var existing = await _db.TrainingTrainees
            .Where(x => x.TrainingId == trainingId)
            .ToListAsync(cancellationToken);

        var existingById = existing.ToDictionary(x => x.Id);
        var idsToKeep = new HashSet<int>();

        foreach (var row in normalizedRows)
        {
            if (row.Id.HasValue && existingById.TryGetValue(row.Id.Value, out var entity))
            {
                entity.ArmyNumber = row.ArmyNumber;
                entity.Rank = row.Rank;
                entity.Name = row.Name;
                entity.UnitName = row.UnitName;
                entity.Category = row.Category;
                entity.RowVersion = Guid.NewGuid().ToByteArray();
                idsToKeep.Add(entity.Id);
            }
            else
            {
                var entity = new TrainingTrainee
                {
                    TrainingId = trainingId,
                    ArmyNumber = row.ArmyNumber,
                    Rank = row.Rank,
                    Name = row.Name,
                    UnitName = row.UnitName,
                    Category = row.Category,
                    RowVersion = Guid.NewGuid().ToByteArray()
                };

                _db.TrainingTrainees.Add(entity);
            }
        }

        foreach (var entity in existing)
        {
            if (!idsToKeep.Contains(entity.Id) && normalizedRows.All(r => r.Id != entity.Id))
            {
                _db.TrainingTrainees.Remove(entity);
            }
        }

        var now = _clock.UtcNow;
        training.LastModifiedAtUtc = now;
        training.LastModifiedByUserId = userId;
        training.RowVersion = Guid.NewGuid().ToByteArray();

        if (normalizedRows.Count > 0)
        {
            var officers = normalizedRows.Count(x => x.Category == 0);
            var jcos = normalizedRows.Count(x => x.Category == 1);
            var ors = normalizedRows.Count(x => x.Category == 2);
            UpdateCounters(training, officers, jcos, ors, now, TrainingCounterSource.Roster);
        }
        else
        {
            UpdateCounters(training, training.LegacyOfficerCount, training.LegacyJcoCount, training.LegacyOrCount, now, TrainingCounterSource.Legacy);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateArmyNumberViolation(ex))
        {
            return TrainingRosterUpdateResult.Failure(TrainingRosterFailureCode.DuplicateArmyNumber, "Each trainee must have a unique Army number.");
        }

        var roster = await _db.TrainingTrainees
            .AsNoTracking()
            .Where(x => x.TrainingId == trainingId)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => new TrainingRosterRow
            {
                Id = x.Id,
                ArmyNumber = x.ArmyNumber,
                Rank = x.Rank,
                Name = x.Name,
                UnitName = x.UnitName,
                Category = x.Category
            })
            .ToListAsync(cancellationToken);

        var countersEntity = training.Counters ?? CreateCounters(training.Id, training.LegacyOfficerCount, training.LegacyJcoCount, training.LegacyOrCount, now, TrainingCounterSource.Legacy);

        var counters = new TrainingRosterCounters(
            countersEntity.Officers,
            countersEntity.JuniorCommissionedOfficers,
            countersEntity.OtherRanks,
            countersEntity.Total,
            countersEntity.Source);

        return TrainingRosterUpdateResult.Success(training.RowVersion, roster, counters);
    }

    private async Task<RosterNormalizationResult> NormalizeRosterRowsAsync(IReadOnlyCollection<TrainingRosterRow> rows, CancellationToken cancellationToken)
    {
        var normalized = new List<NormalizedRosterRow>();
        var seenArmyNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rankMap = await _db.TrainingRankCategoryMaps
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Rank, x => x.Category, StringComparer.OrdinalIgnoreCase, cancellationToken);

        if (rows is null)
        {
            return RosterNormalizationResult.Success(normalized);
        }

        foreach (var row in rows)
        {
            if (row is null)
            {
                continue;
            }

            var id = row.Id;
            var armyNumber = string.IsNullOrWhiteSpace(row.ArmyNumber) ? null : row.ArmyNumber.Trim();
            var rank = (row.Rank ?? string.Empty).Trim();
            var name = (row.Name ?? string.Empty).Trim();
            var unit = (row.UnitName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(armyNumber) && string.IsNullOrWhiteSpace(rank) && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(unit))
            {
                continue;
            }

            if (armyNumber is not null && !seenArmyNumbers.Add(armyNumber))
            {
                return RosterNormalizationResult.Failure($"The Army number \"{armyNumber}\" is already listed.");
            }

            var category = ResolveCategory(rankMap, row.Category, rank);

            normalized.Add(new NormalizedRosterRow(id, armyNumber, rank, name, unit, category));
        }

        return RosterNormalizationResult.Success(normalized);
    }

    private static byte ResolveCategory(IReadOnlyDictionary<string, byte> rankMap, byte proposedCategory, string rank)
    {
        if (proposedCategory is 0 or 1 or 2)
        {
            return proposedCategory;
        }

        if (!string.IsNullOrWhiteSpace(rank) && rankMap.TryGetValue(rank, out var mappedCategory))
        {
            return mappedCategory;
        }

        if (string.IsNullOrWhiteSpace(rank))
        {
            return 2;
        }

        var normalized = rank.Trim().ToLowerInvariant();

        if (normalized.Contains("gen") || normalized.Contains("brig") || normalized.Contains("maj") || normalized.Contains("lt") || normalized.Contains("capt") || normalized.Contains("colonel") || normalized.Contains("col"))
        {
            return 0;
        }

        if (normalized.Contains("subedar") || normalized.Contains("naib") || normalized.Contains("jco"))
        {
            return 1;
        }

        return 2;
    }

    private static bool IsDuplicateArmyNumberViolation(DbUpdateException exception)
    {
        if (exception is null)
        {
            return false;
        }

        var message = exception.InnerException?.Message ?? exception.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("IX_TrainingTrainees_TrainingId_ArmyNumber", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record NormalizedRosterRow(int? Id, string? ArmyNumber, string Rank, string Name, string UnitName, byte Category);

    private sealed record RosterNormalizationResult(bool Success, string? ErrorMessage, List<NormalizedRosterRow> Rows)
    {
        public static RosterNormalizationResult Success(List<NormalizedRosterRow> rows) => new(true, null, rows);

        public static RosterNormalizationResult Failure(string message) => new(false, message, new List<NormalizedRosterRow>());
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
    bool IsSuccess,
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
