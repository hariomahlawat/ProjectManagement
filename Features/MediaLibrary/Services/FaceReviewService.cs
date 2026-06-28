using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Transactional identity-governance service. Every mutation is human initiated,
/// concurrency protected and audit recorded.
/// </summary>
public sealed class FaceReviewService : IFaceReviewService
{
    private readonly MediaLibraryDbContext _db;
    private readonly ILogger<FaceReviewService> _logger;

    public FaceReviewService(
        MediaLibraryDbContext db,
        ILogger<FaceReviewService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Guid> CreatePersonAndAssignAsync(
        Guid faceId,
        string displayName,
        string userId,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeName(displayName);
        ValidateUserId(userId);
        var now = DateTimeOffset.UtcNow;
        var person = new MediaPerson
        {
            Id = Guid.NewGuid(),
            DisplayName = normalized.Display,
            NormalizedName = normalized.Search,
            Status = MediaPersonStatus.Confirmed,
            RepresentativeFaceId = faceId,
            CreatedByUserId = userId,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await ExecuteTransactionalAsync(async () =>
        {
            _db.Persons.Add(person);
            await _db.SaveChangesAsync(cancellationToken);
            await AssignCoreAsync(
                faceId,
                person,
                userId,
                null,
                FaceAssignmentType.ManualAssignment,
                cancellationToken);
            _db.IdentityAudits.Add(new MediaIdentityAudit
            {
                FaceId = faceId,
                PersonId = person.Id,
                NewPersonId = person.Id,
                Action = "PersonCreated",
                PerformedByUserId = userId,
                Notes = $"Created person '{person.DisplayName}' and assigned the selected face.",
                PerformedAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return person.Id;
    }

    public async Task AssignAsync(
        Guid faceId,
        Guid personId,
        string userId,
        double? confidence,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        await ExecuteTransactionalAsync(async () =>
        {
            var person = await RequireActivePersonAsync(personId, cancellationToken);
            await AssignCoreAsync(
                faceId,
                person,
                userId,
                confidence,
                FaceAssignmentType.HumanConfirmed,
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task RejectAsync(
        Guid faceId,
        Guid? personId,
        string userId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var decisions = await _db.FaceReviewDecisions
            .Where(decision => decision.MediaFaceId == faceId
                               && decision.Decision == FaceReviewDecisionType.Pending
                               && (!personId.HasValue || decision.CandidatePersonId == personId))
            .ToListAsync(cancellationToken);
        if (decisions.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var decision in decisions)
        {
            decision.Decision = FaceReviewDecisionType.Rejected;
            decision.DecidedByUserId = userId;
            decision.DecidedAtUtc = now;
            decision.ConcurrencyToken = Guid.NewGuid();
        }

        _db.IdentityAudits.Add(new MediaIdentityAudit
        {
            FaceId = faceId,
            PersonId = personId,
            PreviousPersonId = personId,
            Action = "CandidateRejected",
            PerformedByUserId = userId,
            Notes = personId.HasValue
                ? "The suggested identity was rejected."
                : "All pending identity suggestions for the face were rejected.",
            PerformedAtUtc = now
        });
        await SaveWithConflictTranslationAsync(cancellationToken);
    }

    public async Task IgnoreAsync(
        Guid faceId,
        string userId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        await ExecuteTransactionalAsync(async () =>
        {
            var face = await _db.Faces
                .Include(item => item.Embeddings.Where(embedding => embedding.InvalidatedAtUtc == null))
                .Include(item => item.PersonAssignments.Where(assignment => assignment.RemovedAtUtc == null))
                .SingleOrDefaultAsync(item => item.Id == faceId && !item.IsSuppressed, cancellationToken)
                ?? throw new KeyNotFoundException("The detected face is unavailable or has been suppressed.");
            if (face.PersonAssignments.Count > 0)
            {
                throw new FaceIdentityConflictException(
                    "This face has already been assigned by another reviewer. Refresh the page and try again.");
            }

            var now = DateTimeOffset.UtcNow;
            var pending = await _db.FaceReviewDecisions
                .Where(decision => decision.MediaFaceId == faceId
                                   && decision.Decision == FaceReviewDecisionType.Pending)
                .ToListAsync(cancellationToken);
            foreach (var decision in pending)
            {
                decision.Decision = FaceReviewDecisionType.Rejected;
                decision.DecidedByUserId = userId;
                decision.DecidedAtUtc = now;
                decision.Notes = "Face intentionally left unidentified.";
                decision.ConcurrencyToken = Guid.NewGuid();
            }

            var acknowledged = await _db.FaceReviewDecisions.SingleOrDefaultAsync(
                decision => decision.MediaFaceId == faceId
                            && !decision.CandidatePersonId.HasValue
                            && decision.Decision == FaceReviewDecisionType.Ignored,
                cancellationToken);
            if (acknowledged is not null && pending.Count == 0)
            {
                return;
            }

            if (acknowledged is null)
            {
                var embedding = face.Embeddings
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .FirstOrDefault();
                _db.FaceReviewDecisions.Add(new MediaFaceReviewDecision
                {
                    MediaFaceId = faceId,
                    CandidatePersonId = null,
                    Decision = FaceReviewDecisionType.Ignored,
                    ModelKey = embedding?.ModelKey ?? face.DetectorModelKey,
                    ModelVersion = embedding?.ModelVersion ?? face.DetectorModelVersion,
                    DecidedByUserId = userId,
                    Notes = "Authorised reviewer intentionally left this face unidentified.",
                    ConcurrencyToken = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    DecidedAtUtc = now
                });
            }

            _db.IdentityAudits.Add(new MediaIdentityAudit
            {
                FaceId = faceId,
                Action = "FaceLeftUnidentified",
                PerformedByUserId = userId,
                Notes = "Authorised reviewer acknowledged the face without assigning an identity.",
                PerformedAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task SuppressAsync(
        Guid faceId,
        string userId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        await ExecuteTransactionalAsync(async () =>
        {
            var face = await _db.Faces
                .Include(item => item.PersonAssignments.Where(assignment => assignment.RemovedAtUtc == null))
                .Include(item => item.Embeddings.Where(embedding => embedding.InvalidatedAtUtc == null))
                .SingleOrDefaultAsync(item => item.Id == faceId, cancellationToken)
                ?? throw new KeyNotFoundException("The detected face no longer exists.");
            if (face.IsSuppressed)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            face.IsSuppressed = true;
            face.QualityStatus = FaceQualityStatus.Suppressed;
            face.SuppressedAtUtc = now;
            face.SuppressedByUserId = userId;
            face.UpdatedAtUtc = now;
            face.ConcurrencyToken = Guid.NewGuid();
            foreach (var embedding in face.Embeddings)
            {
                embedding.InvalidatedAtUtc = now;
            }

            foreach (var assignment in face.PersonAssignments)
            {
                assignment.RemovedAtUtc = now;
                assignment.RemovedByUserId = userId;
                assignment.RemovalReason = "Detection suppressed as not a usable face.";
                assignment.ConcurrencyToken = Guid.NewGuid();
            }

            var pending = await _db.FaceReviewDecisions
                .Where(decision => decision.MediaFaceId == faceId
                                   && decision.Decision == FaceReviewDecisionType.Pending)
                .ToListAsync(cancellationToken);
            foreach (var decision in pending)
            {
                decision.Decision = FaceReviewDecisionType.Ignored;
                decision.DecidedAtUtc = now;
                decision.DecidedByUserId = userId;
                decision.Notes = "Face detection suppressed.";
                decision.ConcurrencyToken = Guid.NewGuid();
            }

            _db.IdentityAudits.Add(new MediaIdentityAudit
            {
                FaceId = faceId,
                Action = "FaceSuppressed",
                PerformedByUserId = userId,
                Notes = "Marked as not a usable face.",
                PerformedAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task RenamePersonAsync(
        Guid personId,
        string displayName,
        string userId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var normalized = NormalizeName(displayName);
        var person = await RequireActivePersonAsync(personId, cancellationToken);
        if (string.Equals(person.DisplayName, normalized.Display, StringComparison.Ordinal))
        {
            return;
        }

        var previous = person.DisplayName;
        person.DisplayName = normalized.Display;
        person.NormalizedName = normalized.Search;
        person.UpdatedAtUtc = DateTimeOffset.UtcNow;
        person.ConcurrencyToken = Guid.NewGuid();
        _db.IdentityAudits.Add(new MediaIdentityAudit
        {
            PersonId = person.Id,
            PreviousPersonId = person.Id,
            NewPersonId = person.Id,
            Action = "PersonRenamed",
            PerformedByUserId = userId,
            Notes = $"Renamed '{previous}' to '{person.DisplayName}'.",
            MetadataJson = JsonSerializer.Serialize(new { PreviousName = previous, NewName = person.DisplayName }),
            PerformedAtUtc = person.UpdatedAtUtc
        });
        await SaveWithConflictTranslationAsync(cancellationToken);
    }

    public async Task SetPersonHiddenAsync(
        Guid personId,
        bool hidden,
        string userId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var person = await _db.Persons.SingleOrDefaultAsync(item => item.Id == personId, cancellationToken)
            ?? throw new KeyNotFoundException("The person no longer exists.");
        if (person.Status == MediaPersonStatus.Merged)
        {
            throw new FaceIdentityConflictException("A merged person cannot be restored or hidden independently.");
        }

        if (person.IsHidden == hidden)
        {
            return;
        }

        person.IsHidden = hidden;
        person.Status = hidden ? MediaPersonStatus.Hidden : MediaPersonStatus.Confirmed;
        person.UpdatedAtUtc = DateTimeOffset.UtcNow;
        person.ConcurrencyToken = Guid.NewGuid();
        _db.IdentityAudits.Add(new MediaIdentityAudit
        {
            PersonId = person.Id,
            Action = hidden ? "PersonHidden" : "PersonRestored",
            PerformedByUserId = userId,
            PerformedAtUtc = person.UpdatedAtUtc
        });
        await SaveWithConflictTranslationAsync(cancellationToken);
    }

    public async Task SetRepresentativeFaceAsync(
        Guid personId,
        Guid faceId,
        string userId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var person = await RequireActivePersonAsync(personId, cancellationToken);
        var isAssigned = await _db.PersonFaces.AnyAsync(
            assignment => assignment.MediaPersonId == personId
                          && assignment.MediaFaceId == faceId
                          && assignment.RemovedAtUtc == null
                          && !assignment.MediaFace.IsSuppressed,
            cancellationToken);
        if (!isAssigned)
        {
            throw new FaceIdentityConflictException(
                "The representative face must be an active, confirmed face of this person.");
        }

        person.RepresentativeFaceId = faceId;
        person.UpdatedAtUtc = DateTimeOffset.UtcNow;
        person.ConcurrencyToken = Guid.NewGuid();
        _db.IdentityAudits.Add(new MediaIdentityAudit
        {
            FaceId = faceId,
            PersonId = personId,
            Action = "RepresentativeFaceChanged",
            PerformedByUserId = userId,
            PerformedAtUtc = person.UpdatedAtUtc
        });
        await SaveWithConflictTranslationAsync(cancellationToken);
    }

    public async Task RemoveAssignmentAsync(
        Guid faceId,
        Guid personId,
        string userId,
        string? reason,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var assignment = await _db.PersonFaces
            .SingleOrDefaultAsync(
                item => item.MediaFaceId == faceId
                        && item.MediaPersonId == personId
                        && item.RemovedAtUtc == null,
                cancellationToken)
            ?? throw new KeyNotFoundException("The active face assignment no longer exists.");
        var person = await _db.Persons.SingleAsync(item => item.Id == personId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        assignment.RemovedAtUtc = now;
        assignment.RemovedByUserId = userId;
        assignment.RemovalReason = CleanOptionalText(reason, 1024) ?? "Removed by an authorised reviewer.";
        assignment.ConcurrencyToken = Guid.NewGuid();

        if (person.RepresentativeFaceId == faceId)
        {
            person.RepresentativeFaceId = await _db.PersonFaces
                .AsNoTracking()
                .Where(item => item.MediaPersonId == personId
                               && item.MediaFaceId != faceId
                               && item.RemovedAtUtc == null
                               && !item.MediaFace.IsSuppressed)
                .OrderByDescending(item => item.MediaFace.QualityScore)
                .ThenByDescending(item => item.AssignedAtUtc)
                .Select(item => (Guid?)item.MediaFaceId)
                .FirstOrDefaultAsync(cancellationToken);
            person.UpdatedAtUtc = now;
            person.ConcurrencyToken = Guid.NewGuid();
        }

        _db.IdentityAudits.Add(new MediaIdentityAudit
        {
            FaceId = faceId,
            PersonId = personId,
            PreviousPersonId = personId,
            Action = "AssignmentRemoved",
            PerformedByUserId = userId,
            Notes = assignment.RemovalReason,
            PerformedAtUtc = now
        });
        await SaveWithConflictTranslationAsync(cancellationToken);
    }

    public async Task MergePeopleAsync(
        Guid sourcePersonId,
        Guid targetPersonId,
        string userId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        if (sourcePersonId == targetPersonId)
        {
            throw new ArgumentException("The source and target person must be different.");
        }

        await ExecuteTransactionalAsync(async () =>
        {
            var people = await _db.Persons
                .Where(person => person.Id == sourcePersonId || person.Id == targetPersonId)
                .ToListAsync(cancellationToken);
            var source = people.SingleOrDefault(person => person.Id == sourcePersonId)
                ?? throw new KeyNotFoundException("The source person no longer exists.");
            var target = people.SingleOrDefault(person => person.Id == targetPersonId)
                ?? throw new KeyNotFoundException("The target person no longer exists.");
            EnsureActive(source);
            EnsureActive(target);

            var sourceAssignments = await _db.PersonFaces
                .Where(assignment => assignment.MediaPersonId == sourcePersonId
                                     && assignment.RemovedAtUtc == null)
                .ToListAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            foreach (var assignment in sourceAssignments)
            {
                assignment.RemovedAtUtc = now;
                assignment.RemovedByUserId = userId;
                assignment.RemovalReason = $"Merged into {target.DisplayName}.";
                assignment.ConcurrencyToken = Guid.NewGuid();
            }

            // Release the partial unique constraint before inserting the replacement assignments.
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var sourceAssignment in sourceAssignments)
            {
                _db.PersonFaces.Add(new MediaPersonFace
                {
                    MediaPersonId = target.Id,
                    MediaFaceId = sourceAssignment.MediaFaceId,
                    AssignmentType = FaceAssignmentType.ManualAssignment,
                    AssignmentConfidence = sourceAssignment.AssignmentConfidence,
                    AssignedByUserId = userId,
                    AssignedAtUtc = now,
                    ConcurrencyToken = Guid.NewGuid()
                });
                _db.IdentityAudits.Add(new MediaIdentityAudit
                {
                    FaceId = sourceAssignment.MediaFaceId,
                    PersonId = target.Id,
                    PreviousPersonId = source.Id,
                    NewPersonId = target.Id,
                    Action = "AssignmentMerged",
                    PerformedByUserId = userId,
                    PerformedAtUtc = now
                });
            }

            if (!target.RepresentativeFaceId.HasValue)
            {
                var sourceFaceIds = sourceAssignments
                    .Select(assignment => assignment.MediaFaceId)
                    .ToHashSet();
                target.RepresentativeFaceId = source.RepresentativeFaceId.HasValue
                                              && sourceFaceIds.Contains(source.RepresentativeFaceId.Value)
                    ? source.RepresentativeFaceId
                    : sourceAssignments.Select(assignment => (Guid?)assignment.MediaFaceId).FirstOrDefault();
            }

            source.Status = MediaPersonStatus.Merged;
            source.IsHidden = true;
            source.MergedIntoPersonId = target.Id;
            source.UpdatedAtUtc = now;
            source.ConcurrencyToken = Guid.NewGuid();
            target.Status = MediaPersonStatus.Confirmed;
            target.IsHidden = false;
            target.UpdatedAtUtc = now;
            target.ConcurrencyToken = Guid.NewGuid();

            var sourceDecisions = await _db.FaceReviewDecisions
                .Where(decision => decision.CandidatePersonId == sourcePersonId
                                   && decision.Decision == FaceReviewDecisionType.Pending)
                .ToListAsync(cancellationToken);
            foreach (var decision in sourceDecisions)
            {
                var targetPendingExists = await _db.FaceReviewDecisions.AnyAsync(
                    existing => existing.Id != decision.Id
                                && existing.MediaFaceId == decision.MediaFaceId
                                && existing.CandidatePersonId == targetPersonId
                                && existing.Decision == FaceReviewDecisionType.Pending,
                    cancellationToken);
                if (targetPendingExists)
                {
                    decision.Decision = FaceReviewDecisionType.Ignored;
                    decision.DecidedAtUtc = now;
                    decision.DecidedByUserId = userId;
                    decision.Notes = "Duplicate suggestion removed during person merge.";
                }
                else
                {
                    decision.CandidatePersonId = targetPersonId;
                    decision.Notes = "Candidate redirected during person merge.";
                }

                decision.ConcurrencyToken = Guid.NewGuid();
            }

            _db.IdentityAudits.Add(new MediaIdentityAudit
            {
                PersonId = target.Id,
                PreviousPersonId = source.Id,
                NewPersonId = target.Id,
                Action = "PeopleMerged",
                PerformedByUserId = userId,
                Notes = $"Merged '{source.DisplayName}' into '{target.DisplayName}'.",
                PerformedAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    private async Task AssignCoreAsync(
        Guid faceId,
        MediaPerson person,
        string userId,
        double? confidence,
        FaceAssignmentType assignmentType,
        CancellationToken cancellationToken)
    {
        EnsureActive(person);
        var face = await _db.Faces
            .SingleOrDefaultAsync(item => item.Id == faceId && !item.IsSuppressed, cancellationToken)
            ?? throw new KeyNotFoundException("The detected face is unavailable or has been suppressed.");
        var now = DateTimeOffset.UtcNow;
        var activeAssignments = await _db.PersonFaces
            .Where(assignment => assignment.MediaFaceId == faceId && assignment.RemovedAtUtc == null)
            .ToListAsync(cancellationToken);
        if (activeAssignments.Count == 1 && activeAssignments[0].MediaPersonId == person.Id)
        {
            await ResolvePendingDecisionsAsync(faceId, person.Id, userId, now, cancellationToken);
            return;
        }

        var previousPersonId = activeAssignments.Select(assignment => (Guid?)assignment.MediaPersonId).FirstOrDefault();
        foreach (var assignment in activeAssignments)
        {
            assignment.RemovedAtUtc = now;
            assignment.RemovedByUserId = userId;
            assignment.RemovalReason = $"Reassigned to {person.DisplayName}.";
            assignment.ConcurrencyToken = Guid.NewGuid();
        }

        if (activeAssignments.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        _db.PersonFaces.Add(new MediaPersonFace
        {
            MediaFaceId = faceId,
            MediaPersonId = person.Id,
            AssignmentType = assignmentType,
            AssignmentConfidence = confidence,
            AssignedByUserId = userId,
            AssignedAtUtc = now,
            ConcurrencyToken = Guid.NewGuid()
        });
        if (!person.RepresentativeFaceId.HasValue)
        {
            person.RepresentativeFaceId = faceId;
        }

        person.Status = MediaPersonStatus.Confirmed;
        person.IsHidden = false;
        person.UpdatedAtUtc = now;
        person.ConcurrencyToken = Guid.NewGuid();
        face.UpdatedAtUtc = now;
        face.ConcurrencyToken = Guid.NewGuid();
        await ResolvePendingDecisionsAsync(faceId, person.Id, userId, now, cancellationToken);
        _db.IdentityAudits.Add(new MediaIdentityAudit
        {
            FaceId = faceId,
            PersonId = person.Id,
            PreviousPersonId = previousPersonId,
            NewPersonId = person.Id,
            Action = previousPersonId.HasValue ? "FaceReassigned" : "FaceAssigned",
            PerformedByUserId = userId,
            MetadataJson = confidence.HasValue
                ? JsonSerializer.Serialize(new { Similarity = confidence.Value })
                : null,
            PerformedAtUtc = now
        });
    }

    private async Task ResolvePendingDecisionsAsync(
        Guid faceId,
        Guid selectedPersonId,
        string userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pending = await _db.FaceReviewDecisions
            .Where(decision => decision.MediaFaceId == faceId
                               && decision.Decision == FaceReviewDecisionType.Pending)
            .ToListAsync(cancellationToken);
        foreach (var decision in pending)
        {
            decision.Decision = decision.CandidatePersonId == selectedPersonId
                ? FaceReviewDecisionType.Confirmed
                : FaceReviewDecisionType.Rejected;
            decision.DecidedByUserId = userId;
            decision.DecidedAtUtc = now;
            decision.ConcurrencyToken = Guid.NewGuid();
        }
    }

    private async Task<MediaPerson> RequireActivePersonAsync(
        Guid personId,
        CancellationToken cancellationToken)
    {
        var person = await _db.Persons.SingleOrDefaultAsync(item => item.Id == personId, cancellationToken)
            ?? throw new KeyNotFoundException("The person no longer exists.");
        EnsureActive(person);
        return person;
    }

    private static void EnsureActive(MediaPerson person)
    {
        if (person.Status is MediaPersonStatus.Merged or MediaPersonStatus.Archived)
        {
            throw new FaceIdentityConflictException("The selected person is no longer active.");
        }
    }

    private async Task ExecuteTransactionalAsync(
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await action();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(exception, "Concurrent identity-governance update rejected.");
            throw new FaceIdentityConflictException(
                "This identity record was changed by another reviewer. Refresh the page and try again.");
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(exception, "Identity-governance database constraint rejected an update.");
            throw new FaceIdentityConflictException(
                "The identity operation conflicts with a more recent assignment. Refresh the page and try again.");
        }
    }

    private async Task SaveWithConflictTranslationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(exception, "Concurrent identity-governance update rejected.");
            throw new FaceIdentityConflictException(
                "This identity record was changed by another reviewer. Refresh the page and try again.");
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(exception, "Identity-governance database constraint rejected an update.");
            throw new FaceIdentityConflictException(
                "The identity operation conflicts with a more recent assignment. Refresh the page and try again.");
        }
    }

    private static (string Display, string Search) NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A display name is required.", nameof(value));
        }

        var display = string.Join(
                ' ',
                value.Normalize(NormalizationForm.FormKC)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        if (display.Length is < 2 or > 200)
        {
            throw new ArgumentException("The display name must contain between 2 and 200 characters.", nameof(value));
        }

        return (display, display.ToUpperInvariant());
    }

    private static string? CleanOptionalText(string? value, int maximumLength)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return cleaned is { Length: > 0 }
            ? cleaned[..Math.Min(cleaned.Length, maximumLength)]
            : null;
    }

    private static void ValidateUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("An authenticated reviewer is required.", nameof(userId));
        }
    }
}
