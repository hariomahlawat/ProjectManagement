using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services;
using ProjectManagement.Services.ProjectOfficeReports.Training;
using DomainTraining = ProjectManagement.Areas.ProjectOfficeReports.Domain.Training;
using DomainTrainingCounters = ProjectManagement.Areas.ProjectOfficeReports.Domain.TrainingCounters;
using DomainTrainingProject = ProjectManagement.Areas.ProjectOfficeReports.Domain.TrainingProject;
using DomainTrainingTrainee = ProjectManagement.Areas.ProjectOfficeReports.Domain.TrainingTrainee;
using DomainTrainingDeleteRequest = ProjectManagement.Areas.ProjectOfficeReports.Domain.TrainingDeleteRequest;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class TrainingWriteService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ITrainingNotificationService _notifications;
    private readonly ILogger<TrainingWriteService> _logger;

    public TrainingWriteService(
        ApplicationDbContext db,
        IClock clock,
        ITrainingNotificationService notifications,
        ILogger<TrainingWriteService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        var training = new DomainTraining
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
            training.ProjectLinks.Add(new DomainTrainingProject
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
        await RefreshCountersAsync(
            training,
            command.LegacyOfficers,
            command.LegacyJcos,
            command.LegacyOrs,
            now,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return TrainingMutationResult.Success(training.Id, training.RowVersion);
    }

    private static DomainTrainingCounters CreateCounters(Guid trainingId, int officers, int jcos, int ors, DateTimeOffset timestamp, TrainingCounterSource source)
    {
        return new DomainTrainingCounters
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

    private static void UpdateCounters(DomainTraining training, int officers, int jcos, int ors, DateTimeOffset timestamp, TrainingCounterSource source)
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

    // Roster data always takes precedence over legacy counts. When a snapshot is provided (e.g. during
    // roster upserts) it is used directly because the change tracker may not yet reflect pending inserts
    // or deletions. Otherwise the method inspects the persisted roster state to choose the appropriate
    // counter source.
    private async Task RefreshCountersAsync(
        DomainTraining training,
        int legacyOfficers,
        int legacyJcos,
        int legacyOrs,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken,
        RosterCounterSnapshot? rosterSnapshot = null)
    {
        if (training is null)
        {
            throw new ArgumentNullException(nameof(training));
        }

        if (rosterSnapshot.HasValue)
        {
            var snapshot = rosterSnapshot.Value;
            if (snapshot.HasRoster)
            {
                UpdateCounters(training, snapshot.Officers, snapshot.JuniorCommissionedOfficers, snapshot.OtherRanks, timestamp, TrainingCounterSource.Roster);
            }
            else
            {
                UpdateCounters(training, legacyOfficers, legacyJcos, legacyOrs, timestamp, TrainingCounterSource.Legacy);
            }

            return;
        }

        var rosterGroups = await _db.TrainingTrainees
            .AsNoTracking()
            .Where(x => x.TrainingId == training.Id)
            .GroupBy(x => x.Category)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        if (rosterGroups.Count > 0)
        {
            var officers = 0;
            var jcos = 0;
            var ors = 0;

            foreach (var group in rosterGroups)
            {
                switch (group.Key)
                {
                    case 0:
                        officers = group.Count;
                        break;
                    case 1:
                        jcos = group.Count;
                        break;
                    case 2:
                        ors = group.Count;
                        break;
                }
            }

            UpdateCounters(training, officers, jcos, ors, timestamp, TrainingCounterSource.Roster);
            return;
        }

        UpdateCounters(training, legacyOfficers, legacyJcos, legacyOrs, timestamp, TrainingCounterSource.Legacy);
    }

    private readonly record struct RosterCounterSnapshot(
        int Officers,
        int JuniorCommissionedOfficers,
        int OtherRanks,
        bool HasRoster);

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
            var message = normalization.ErrorMessage ?? "The roster could not be saved.";
            return TrainingRosterUpdateResult.Failure(normalization.FailureCode, message);
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
                var newTrainee = new DomainTrainingTrainee
                {
                    TrainingId = trainingId,
                    ArmyNumber = row.ArmyNumber,
                    Rank = row.Rank,
                    Name = row.Name,
                    UnitName = row.UnitName,
                    Category = row.Category,
                    RowVersion = Guid.NewGuid().ToByteArray()
                };

                _db.TrainingTrainees.Add(newTrainee);
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

        RosterCounterSnapshot? rosterSnapshot;
        if (normalizedRows.Count > 0)
        {
            var officers = normalizedRows.Count(x => x.Category == 0);
            var jcos = normalizedRows.Count(x => x.Category == 1);
            var ors = normalizedRows.Count(x => x.Category == 2);
            rosterSnapshot = new RosterCounterSnapshot(officers, jcos, ors, HasRoster: true);
        }
        else
        {
            rosterSnapshot = new RosterCounterSnapshot(0, 0, 0, HasRoster: false);
        }

        await RefreshCountersAsync(
            training,
            training.LegacyOfficerCount,
            training.LegacyJcoCount,
            training.LegacyOrCount,
            now,
            cancellationToken,
            rosterSnapshot);

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

    public async Task<TrainingDeleteRequestResult> RequestDeleteAsync(
        Guid trainingId,
        string reason,
        byte[]? expectedRowVersion,
        string userId,
        CancellationToken cancellationToken)
    {
        if (trainingId == Guid.Empty)
        {
            return TrainingDeleteRequestResult.Failure(
                TrainingDeleteFailureCode.TrainingNotFound,
                "A training identifier is required.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return TrainingDeleteRequestResult.Failure(
                TrainingDeleteFailureCode.MissingUserId,
                "The current user context is not available.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return TrainingDeleteRequestResult.Failure(
                TrainingDeleteFailureCode.InvalidReason,
                "Provide a reason for the deletion request.");
        }

        var normalizedReason = NormalizeDeleteReason(reason);

        var training = await _db.Trainings
            .Include(x => x.Counters)
            .Include(x => x.TrainingType)
            .FirstOrDefaultAsync(x => x.Id == trainingId, cancellationToken);

        if (training is null)
        {
            return TrainingDeleteRequestResult.Failure(
                TrainingDeleteFailureCode.TrainingNotFound,
                "The training could not be found.");
        }

        if (expectedRowVersion is not null
            && expectedRowVersion.Length > 0
            && !training.RowVersion.SequenceEqual(expectedRowVersion))
        {
            return TrainingDeleteRequestResult.Failure(
                TrainingDeleteFailureCode.ConcurrencyConflict,
                "The training was updated by another user. Reload and try again.");
        }

        var hasPendingRequest = await _db.TrainingDeleteRequests
            .AsNoTracking()
            .AnyAsync(
                request => request.TrainingId == trainingId
                    && request.Status == TrainingDeleteRequestStatus.Pending,
                cancellationToken);

        if (hasPendingRequest)
        {
            return TrainingDeleteRequestResult.Failure(
                TrainingDeleteFailureCode.PendingRequestExists,
                "A delete request is already pending for this training.");
        }

        var now = _clock.UtcNow;
        var request = new DomainTrainingDeleteRequest
        {
            Id = Guid.NewGuid(),
            TrainingId = trainingId,
            RequestedByUserId = userId,
            RequestedAtUtc = now,
            Reason = normalizedReason,
            Status = TrainingDeleteRequestStatus.Pending,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        _db.TrainingDeleteRequests.Add(request);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to submit delete request for training {TrainingId} due to concurrency.",
                trainingId);

            return TrainingDeleteRequestResult.Failure(
                TrainingDeleteFailureCode.ConcurrencyConflict,
                "The training was updated by another user. Reload and try again.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist delete request for training {TrainingId}.",
                trainingId);

            return TrainingDeleteRequestResult.Failure(
                TrainingDeleteFailureCode.ConcurrencyConflict,
                "The delete request could not be saved. Please try again.");
        }

        var notificationContext = CreateNotificationContext(training, request);

        try
        {
            await _notifications.NotifyDeleteRequestedAsync(notificationContext, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish delete request notification for training {TrainingId}.",
                trainingId);
        }

        return TrainingDeleteRequestResult.Success(request.Id);
    }

    public async Task<TrainingDeleteDecisionResult> ApproveDeleteAsync(
        Guid requestId,
        string approverUserId,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.RequestNotFound,
                "The delete request could not be found.");
        }

        if (string.IsNullOrWhiteSpace(approverUserId))
        {
            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.MissingUserId,
                "The current user context is not available.");
        }

        var request = await _db.TrainingDeleteRequests
            .Include(r => r.Training)!
                .ThenInclude(t => t!.Counters)
            .Include(r => r.Training)!
                .ThenInclude(t => t!.TrainingType)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request is null)
        {
            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.RequestNotFound,
                "The delete request could not be found.");
        }

        if (request.Status != TrainingDeleteRequestStatus.Pending)
        {
            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.RequestNotPending,
                "The delete request is no longer pending.");
        }

        if (request.Training is null)
        {
            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.TrainingNotFound,
                "The associated training could not be found.");
        }

        var training = request.Training;
        var trainingId = training.Id;
        var notificationContext = CreateNotificationContext(training, request);

        var trainees = _db.TrainingTrainees.Where(trainee => trainee.TrainingId == trainingId);
        var projectLinks = _db.TrainingProjects.Where(link => link.TrainingId == trainingId);
        var counters = await _db.TrainingCounters
            .FirstOrDefaultAsync(counter => counter.TrainingId == trainingId, cancellationToken);

        _db.TrainingTrainees.RemoveRange(trainees);
        _db.TrainingProjects.RemoveRange(projectLinks);
        if (counters is not null)
        {
            _db.TrainingCounters.Remove(counters);
        }

        _db.Trainings.Remove(training);

        request.Status = TrainingDeleteRequestStatus.Approved;
        request.DecidedByUserId = approverUserId;
        request.DecidedAtUtc = _clock.UtcNow;
        request.DecisionNotes = null;
        request.RowVersion = Guid.NewGuid().ToByteArray();

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to approve delete request {RequestId} for training {TrainingId} due to concurrency.",
                requestId,
                trainingId);

            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.ConcurrencyConflict,
                "The delete request was updated by another user. Refresh the page and try again.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(
                ex,
                "Failed to approve delete request {RequestId} for training {TrainingId}.",
                requestId,
                trainingId);

            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.ConcurrencyConflict,
                "The delete request could not be approved. Please try again.");
        }

        try
        {
            await _notifications.NotifyDeleteApprovedAsync(notificationContext, approverUserId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish approval notification for training {TrainingId}.",
                trainingId);
        }

        return TrainingDeleteDecisionResult.Success();
    }

    public async Task<TrainingDeleteDecisionResult> RejectDeleteAsync(
        Guid requestId,
        string decisionReason,
        string approverUserId,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
        {
            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.RequestNotFound,
                "The delete request could not be found.");
        }

        if (string.IsNullOrWhiteSpace(approverUserId))
        {
            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.MissingUserId,
                "The current user context is not available.");
        }

        if (string.IsNullOrWhiteSpace(decisionReason))
        {
            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.InvalidReason,
                "Provide a reason for rejecting the delete request.");
        }

        var normalizedReason = NormalizeDeleteReason(decisionReason);

        var request = await _db.TrainingDeleteRequests
            .Include(r => r.Training)!
                .ThenInclude(t => t!.Counters)
            .Include(r => r.Training)!
                .ThenInclude(t => t!.TrainingType)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request is null)
        {
            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.RequestNotFound,
                "The delete request could not be found.");
        }

        if (request.Status != TrainingDeleteRequestStatus.Pending)
        {
            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.RequestNotPending,
                "The delete request is no longer pending.");
        }

        if (request.Training is null)
        {
            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.TrainingNotFound,
                "The associated training could not be found.");
        }

        var notificationContext = CreateNotificationContext(request.Training, request);

        request.Status = TrainingDeleteRequestStatus.Rejected;
        request.DecidedByUserId = approverUserId;
        request.DecidedAtUtc = _clock.UtcNow;
        request.DecisionNotes = normalizedReason;
        request.RowVersion = Guid.NewGuid().ToByteArray();

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to reject delete request {RequestId} due to concurrency.",
                requestId);

            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.ConcurrencyConflict,
                "The delete request was updated by another user. Refresh the page and try again.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(
                ex,
                "Failed to reject delete request {RequestId}.",
                requestId);

            return TrainingDeleteDecisionResult.Failure(
                TrainingDeleteFailureCode.ConcurrencyConflict,
                "The delete request could not be rejected. Please try again.");
        }

        try
        {
            await _notifications.NotifyDeleteRejectedAsync(
                notificationContext,
                approverUserId,
                normalizedReason,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish rejection notification for delete request {RequestId}.",
                requestId);
        }

        return TrainingDeleteDecisionResult.Success();
    }

    private async Task<RosterNormalizationResult> NormalizeRosterRowsAsync(IReadOnlyCollection<TrainingRosterRow> rows, CancellationToken cancellationToken)
    {
        var normalized = new List<NormalizedRosterRow>();
        var seenArmyNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowIndex = 0;

        var rankMap = await _db.TrainingRankCategoryMaps
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.Rank, x => (byte)x.Category, StringComparer.OrdinalIgnoreCase, cancellationToken);

        if (rows is null)
        {
            return RosterNormalizationResult.CreateSuccess(normalized);
        }

        foreach (var row in rows)
        {
            rowIndex++;
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

            if (string.IsNullOrWhiteSpace(rank) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(unit))
            {
                var message = $"Row {rowIndex} is missing required information. Each roster row must include a Rank, Name, and Unit.";
                return RosterNormalizationResult.Failure(TrainingRosterFailureCode.InvalidRequest, message);
            }

            if (armyNumber is not null && !seenArmyNumbers.Add(armyNumber))
            {
                return RosterNormalizationResult.Failure(TrainingRosterFailureCode.DuplicateArmyNumber, $"The Army number \"{armyNumber}\" is already listed.");
            }

            var category = ResolveCategory(rankMap, row.Category, rank);

            normalized.Add(new NormalizedRosterRow(id, armyNumber, rank, name, unit, category));
        }

        return RosterNormalizationResult.CreateSuccess(normalized);
    }

    private static TrainingDeleteNotificationContext CreateNotificationContext(
        DomainTraining training,
        DomainTrainingDeleteRequest request)
    {
        if (training is null)
        {
            throw new ArgumentNullException(nameof(training));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var counters = training.Counters;
        var officers = counters?.Officers ?? training.LegacyOfficerCount;
        var jcos = counters?.JuniorCommissionedOfficers ?? training.LegacyJcoCount;
        var ors = counters?.OtherRanks ?? training.LegacyOrCount;
        var total = counters?.Total ?? (officers + jcos + ors);

        var trainingTypeName = training.TrainingType?.Name ?? "Training";

        return new TrainingDeleteNotificationContext(
            training.Id,
            request.Id,
            trainingTypeName,
            training.StartDate,
            training.EndDate,
            training.TrainingMonth,
            training.TrainingYear,
            officers,
            jcos,
            ors,
            total,
            request.RequestedByUserId,
            request.RequestedAtUtc,
            request.Reason);
    }

    private static string NormalizeDeleteReason(string reason)
    {
        var trimmed = reason.Trim();
        return trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
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

    private sealed record RosterNormalizationResult(
        bool Success,
        TrainingRosterFailureCode FailureCode,
        string? ErrorMessage,
        List<NormalizedRosterRow> Rows)
    {
        public static RosterNormalizationResult CreateSuccess(List<NormalizedRosterRow> rows)
            => new(true, TrainingRosterFailureCode.None, null, rows);

        public static RosterNormalizationResult Failure(TrainingRosterFailureCode failureCode, string message)
            => new(false, failureCode, message, new List<NormalizedRosterRow>());
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

    private void UpdateProjectLinks(DomainTraining training, IReadOnlyCollection<int> desiredProjectIds)
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
            training.ProjectLinks.Add(new DomainTrainingProject
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
