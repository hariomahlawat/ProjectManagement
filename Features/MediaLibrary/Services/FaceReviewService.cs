using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Transactional identity-governance service. Every mutation is human initiated,
/// concurrency protected and audit recorded.
/// </summary>
public sealed class FaceReviewService : IFaceReviewService
{
    private readonly MediaLibraryDbContext _db;
    private readonly IFaceCandidateRefreshQueueService _candidateRefreshQueue;
    private readonly IFaceIdentityGroupingRuntimeState _groupingState;
    private readonly MediaPeopleOptions _options;
    private readonly ILogger<FaceReviewService> _logger;

    public FaceReviewService(
        MediaLibraryDbContext db,
        IFaceCandidateRefreshQueueService candidateRefreshQueue,
        IFaceIdentityGroupingRuntimeState groupingState,
        IOptions<MediaLibraryOptions> options,
        ILogger<FaceReviewService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _candidateRefreshQueue = candidateRefreshQueue ?? throw new ArgumentNullException(nameof(candidateRefreshQueue));
        _groupingState = groupingState ?? throw new ArgumentNullException(nameof(groupingState));
        _options = options?.Value.People ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<Guid> CreatePersonAndAssignAsync(
        Guid faceId,
        string displayName,
        string userId,
        CancellationToken cancellationToken)
        => CreatePersonAndAssignManyAsync(
            new[] { faceId },
            displayName,
            userId,
            cancellationToken);

    public async Task<Guid> CreatePersonAndAssignManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        string displayName,
        string userId,
        CancellationToken cancellationToken)
    {
        var selectedFaces = NormalizeFaceSelection(faceIds);
        var normalized = NormalizeName(displayName);
        ValidateUserId(userId);
        if (selectedFaces.Count > Math.Clamp(_options.CandidateBatchConfirmationLimit, 1, 100))
        {
            throw new FaceIdentityConflictException(
                $"No more than {_options.CandidateBatchConfirmationLimit} appearances may be confirmed in one operation.");
        }
        if (selectedFaces.Count > 1)
        {
            await ValidateGroupSelectionAsync(selectedFaces, requireUnassigned: true, cancellationToken);
        }
        else
        {
            await ValidateUnassignedFaceAsync(selectedFaces[0], cancellationToken);
        }

        var initialTrustedReferenceFaceId = await SelectInitialTrustedReferenceAsync(
            selectedFaces,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var person = new MediaPerson
        {
            Id = Guid.NewGuid(),
            DisplayName = normalized.Display,
            NormalizedName = normalized.Search,
            Status = MediaPersonStatus.Confirmed,
            RepresentativeFaceId = selectedFaces[0],
            CreatedByUserId = userId,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await ExecuteTransactionalAsync(async () =>
        {
            _db.Persons.Add(person);
            await _db.SaveChangesAsync(cancellationToken);
            for (var index = 0; index < selectedFaces.Count; index++)
            {
                await AssignCoreAsync(
                    selectedFaces[index],
                    person,
                    userId,
                    null,
                    FaceAssignmentType.ManualAssignment,
                    trustAsReference: selectedFaces[index] == initialTrustedReferenceFaceId,
                    cancellationToken);
            }

            _db.IdentityAudits.Add(new MediaIdentityAudit
            {
                FaceId = selectedFaces[0],
                PersonId = person.Id,
                NewPersonId = person.Id,
                Action = selectedFaces.Count == 1 ? "PersonCreated" : "PersonGroupCreated",
                PerformedByUserId = userId,
                Notes = selectedFaces.Count == 1
                    ? $"Created person '{person.DisplayName}' and assigned the selected face."
                    : $"Created person '{person.DisplayName}' and assigned {selectedFaces.Count} reviewer-selected faces.",
                MetadataJson = JsonSerializer.Serialize(new { FaceIds = selectedFaces }),
                PerformedAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await RefreshCandidatesAfterIdentityChangeAsync(cancellationToken);
        return person.Id;
    }

    public Task AssignAsync(
        Guid faceId,
        Guid personId,
        string userId,
        double? confidence,
        CancellationToken cancellationToken)
        => AssignManyAsync(
            new[] { faceId },
            personId,
            userId,
            confidence,
            cancellationToken);

    public async Task AssignManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        Guid personId,
        string userId,
        double? confidence,
        CancellationToken cancellationToken)
    {
        var selectedFaces = NormalizeFaceSelection(faceIds);
        ValidateUserId(userId);
        if (selectedFaces.Count > Math.Clamp(_options.CandidateBatchConfirmationLimit, 1, 100))
        {
            throw new FaceIdentityConflictException(
                $"No more than {_options.CandidateBatchConfirmationLimit} appearances may be confirmed in one operation.");
        }
        if (selectedFaces.Count > 1)
        {
            await ValidateGroupSelectionAsync(selectedFaces, requireUnassigned: true, cancellationToken);
        }

        await ExecuteTransactionalAsync(async () =>
        {
            var person = await RequireActivePersonAsync(personId, cancellationToken);
            foreach (var faceId in selectedFaces)
            {
                await AssignCoreAsync(
                    faceId,
                    person,
                    userId,
                    confidence,
                    FaceAssignmentType.HumanConfirmed,
                    trustAsReference: false,
                    cancellationToken);
            }

            if (selectedFaces.Count > 1)
            {
                _db.IdentityAudits.Add(new MediaIdentityAudit
                {
                    FaceId = selectedFaces[0],
                    PersonId = person.Id,
                    NewPersonId = person.Id,
                    Action = "FaceGroupAssigned",
                    PerformedByUserId = userId,
                    Notes = $"Assigned {selectedFaces.Count} reviewer-selected faces to '{person.DisplayName}'.",
                    MetadataJson = JsonSerializer.Serialize(new { FaceIds = selectedFaces, Similarity = confidence }),
                    PerformedAtUtc = DateTimeOffset.UtcNow
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await RefreshCandidatesAfterIdentityChangeAsync(cancellationToken);
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
        try
        {
            await _candidateRefreshQueue.QueueFaceAsync(faceId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to queue the rejected face for the next bounded candidate-search pass.");
        }
    }

    public async Task RejectManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        Guid personId,
        string userId,
        CancellationToken cancellationToken)
    {
        var selectedFaces = NormalizeFaceSelection(faceIds);
        ValidateUserId(userId);
        if (personId == Guid.Empty)
        {
            throw new ArgumentException("A suggested person is required.", nameof(personId));
        }

        await ValidateGroupSelectionAsync(selectedFaces, requireUnassigned: true, cancellationToken);
        var activePerson = await _db.Persons
            .AsNoTracking()
            .AnyAsync(person => person.Id == personId
                                && !person.IsHidden
                                && person.Status == MediaPersonStatus.Confirmed,
                cancellationToken);
        if (!activePerson)
        {
            throw new KeyNotFoundException("The suggested person is no longer active.");
        }

        var existing = await _db.FaceReviewDecisions
            .Where(decision => selectedFaces.Contains(decision.MediaFaceId)
                               && decision.CandidatePersonId == personId
                               && decision.ModelKey == _options.Embedder.Key
                               && decision.ModelVersion == _options.Embedder.Version)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var decision in existing.Where(decision => decision.Decision == FaceReviewDecisionType.Pending))
        {
            decision.Decision = FaceReviewDecisionType.Rejected;
            decision.DecidedByUserId = userId;
            decision.DecidedAtUtc = now;
            decision.Notes = "Known-person suggestion rejected for this reviewer-selected identity group.";
            decision.ConcurrencyToken = Guid.NewGuid();
        }

        var alreadyRejected = existing
            .Where(decision => decision.Decision == FaceReviewDecisionType.Rejected)
            .Select(decision => decision.MediaFaceId)
            .ToHashSet();
        foreach (var faceId in selectedFaces.Where(faceId => !alreadyRejected.Contains(faceId)))
        {
            _db.FaceReviewDecisions.Add(new MediaFaceReviewDecision
            {
                MediaFaceId = faceId,
                CandidatePersonId = personId,
                Decision = FaceReviewDecisionType.Rejected,
                ModelKey = _options.Embedder.Key,
                ModelVersion = _options.Embedder.Version,
                DecidedByUserId = userId,
                Notes = "Known-person suggestion rejected for this reviewer-selected identity group.",
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAtUtc = now,
                DecidedAtUtc = now
            });
        }

        _db.IdentityAudits.Add(new MediaIdentityAudit
        {
            FaceId = selectedFaces[0],
            PersonId = personId,
            PreviousPersonId = personId,
            Action = "GroupCandidateRejected",
            PerformedByUserId = userId,
            Notes = $"Rejected the suggested person for {selectedFaces.Count} face appearance(s).",
            MetadataJson = JsonSerializer.Serialize(new { FaceIds = selectedFaces, CandidatePersonId = personId }),
            PerformedAtUtc = now
        });
        await SaveWithConflictTranslationAsync(cancellationToken);
        foreach (var faceId in selectedFaces)
        {
            try
            {
                await _candidateRefreshQueue.QueueFaceAsync(faceId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Unable to queue face {FaceId} after rejecting a grouped known-person candidate.",
                    faceId);
            }
        }
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
                face.CandidateSearchStatus = FaceCandidateSearchStatus.Ready;
                face.CandidateSearchFailureReason = null;
                face.CandidateSearchCompletedAtUtc = now;
                face.UpdatedAtUtc = now;
                face.ConcurrencyToken = Guid.NewGuid();
                await _db.SaveChangesAsync(cancellationToken);
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

            face.CandidateSearchStatus = FaceCandidateSearchStatus.Ready;
            face.CandidateSearchFailureReason = null;
            face.CandidateSearchCompletedAtUtc = now;
            face.UpdatedAtUtc = now;
            face.ConcurrencyToken = Guid.NewGuid();

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
        await RefreshCandidatesAfterIdentityChangeAsync(cancellationToken);
    }

    public Task SuppressAsync(
        Guid faceId,
        string userId,
        CancellationToken cancellationToken)
        => SuppressAsync(
            faceId,
            userId,
            "Reviewer marked the detection as not a valid face.",
            cancellationToken);

    public async Task SuppressAsync(
        Guid faceId,
        string userId,
        string reason,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var normalizedReason = RequireReason(reason);
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
            var affectedPersonIds = face.PersonAssignments
                .Select(assignment => assignment.MediaPersonId)
                .Distinct()
                .ToArray();

            face.IsSuppressed = true;
            face.QualityStatus = FaceQualityStatus.Suppressed;
            face.CandidateSearchStatus = FaceCandidateSearchStatus.NotRequested;
            face.CandidateSearchFailureReason = null;
            face.CandidateSearchCompletedAtUtc = now;
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
                assignment.RemovalReason = normalizedReason;
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
                decision.Notes = normalizedReason;
                decision.ConcurrencyToken = Guid.NewGuid();
            }

            if (affectedPersonIds.Length > 0)
            {
                var affectedPeople = await _db.Persons
                    .Where(person => affectedPersonIds.Contains(person.Id))
                    .ToListAsync(cancellationToken);
                foreach (var person in affectedPeople)
                {
                    await RefreshRepresentativeAfterRemovalAsync(
                        person,
                        new[] { faceId },
                        now,
                        cancellationToken);
                }
            }

            _db.IdentityAudits.Add(new MediaIdentityAudit
            {
                FaceId = faceId,
                Action = "FaceSuppressed",
                PerformedByUserId = userId,
                Notes = normalizedReason,
                PerformedAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        await RefreshCandidatesAfterIdentityChangeAsync(cancellationToken);
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

    public async Task SetReferenceStatusAsync(
        Guid personId,
        Guid faceId,
        FaceReferenceStatus referenceStatus,
        string userId,
        string reason,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var governanceReason = RequireReason(reason);
        if (referenceStatus == FaceReferenceStatus.NotReference)
        {
            throw new ArgumentException(
                "Choose TrustedReference or Excluded for an explicit governance change.",
                nameof(referenceStatus));
        }

        await ExecuteTransactionalAsync(async () =>
        {
            var person = await RequireActivePersonAsync(personId, cancellationToken);
            var assignment = await _db.PersonFaces
                .Include(item => item.MediaFace)
                    .ThenInclude(face => face.MediaAsset)
                .SingleOrDefaultAsync(item => item.MediaPersonId == personId
                                              && item.MediaFaceId == faceId
                                              && item.RemovedAtUtc == null,
                    cancellationToken)
                ?? throw new FaceIdentityConflictException(
                    "The appearance is no longer actively assigned to this person.");

            if (assignment.ReferenceStatus == referenceStatus)
            {
                return;
            }

            if (referenceStatus == FaceReferenceStatus.TrustedReference)
            {
                var face = assignment.MediaFace;
                if (face.IsSuppressed
                    || face.QualityStatus != FaceQualityStatus.EmbeddingEligible
                    || face.QualityScore < _options.CandidateMinimumTrustedReferenceQuality
                    || !face.MediaAsset.IsAvailable
                    || face.MediaAsset.IsDeleted
                    || face.MediaAsset.IsArchived)
                {
                    throw new FaceIdentityConflictException(
                        "Only an available, embedding-eligible, sufficiently clear face can be trusted for matching.");
                }

                var hasCurrentEmbedding = await _db.FaceEmbeddings.AsNoTracking().AnyAsync(
                    embedding => embedding.MediaFaceId == faceId
                                 && embedding.InvalidatedAtUtc == null
                                 && embedding.ModelKey == _options.Embedder.Key
                                 && embedding.ModelVersion == _options.Embedder.Version
                                 && embedding.Dimension == _options.Embedder.EmbeddingDimension,
                    cancellationToken);
                if (!hasCurrentEmbedding)
                {
                    throw new FaceIdentityConflictException(
                        "This appearance does not have a current valid embedding and cannot be used for matching.");
                }
            }
            else
            {
                var otherTrustedReferenceExists = await _db.PersonFaces.AsNoTracking().AnyAsync(
                    item => item.MediaPersonId == personId
                            && item.MediaFaceId != faceId
                            && item.RemovedAtUtc == null
                            && item.ReferenceStatus == FaceReferenceStatus.TrustedReference,
                    cancellationToken);
                if (!otherTrustedReferenceExists)
                {
                    throw new FaceIdentityConflictException(
                        "Promote another trusted reference before excluding the last matching reference for this person.");
                }
            }

            var now = DateTimeOffset.UtcNow;
            assignment.ReferenceStatus = referenceStatus;
            assignment.ReferenceChangedByUserId = userId;
            assignment.ReferenceChangedAtUtc = now;
            assignment.ReferenceChangeReason = governanceReason;
            assignment.ConcurrencyToken = Guid.NewGuid();
            person.UpdatedAtUtc = now;
            person.ConcurrencyToken = Guid.NewGuid();

            _db.IdentityAudits.Add(new MediaIdentityAudit
            {
                FaceId = faceId,
                PersonId = personId,
                Action = referenceStatus == FaceReferenceStatus.TrustedReference
                    ? "ReferenceTrusted"
                    : "ReferenceExcluded",
                PerformedByUserId = userId,
                Notes = governanceReason,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    ReferenceStatus = referenceStatus.ToString(),
                    assignment.MediaFace.QualityScore
                }),
                PerformedAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await RefreshCandidatesAfterIdentityChangeAsync(cancellationToken);
    }

    public Task RemoveAssignmentAsync(
        Guid faceId,
        Guid personId,
        string userId,
        string? reason,
        CancellationToken cancellationToken)
        => ReturnAssignmentsToReviewAsync(
            personId,
            new[] { faceId },
            userId,
            RequireReason(reason),
            cancellationToken);

    public async Task MoveAssignmentsAsync(
        Guid sourcePersonId,
        IReadOnlyCollection<Guid> faceIds,
        Guid targetPersonId,
        string userId,
        string reason,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var selectedFaces = NormalizeFaceSelection(faceIds);
        var correctionReason = RequireReason(reason);
        if (sourcePersonId == targetPersonId)
        {
            throw new ArgumentException("Choose a different target person.", nameof(targetPersonId));
        }

        await ExecuteTransactionalAsync(async () =>
        {
            var source = await RequireActivePersonAsync(sourcePersonId, cancellationToken);
            var target = await RequireActivePersonAsync(targetPersonId, cancellationToken);
            var assignments = await RequireActiveAssignmentsAsync(
                sourcePersonId,
                selectedFaces,
                cancellationToken);
            await EnsureNoSamePhotographConflictAsync(
                selectedFaces,
                targetPersonId,
                cancellationToken);

            var now = DateTimeOffset.UtcNow;
            foreach (var assignment in assignments)
            {
                assignment.RemovedAtUtc = now;
                assignment.RemovedByUserId = userId;
                assignment.RemovalReason = correctionReason;
                assignment.ConcurrencyToken = Guid.NewGuid();
            }

            await _db.SaveChangesAsync(cancellationToken);
            foreach (var assignment in assignments)
            {
                _db.PersonFaces.Add(new MediaPersonFace
                {
                    MediaPersonId = target.Id,
                    MediaFaceId = assignment.MediaFaceId,
                    AssignmentType = FaceAssignmentType.ManualAssignment,
                    AssignmentConfidence = assignment.AssignmentConfidence,
                    ReferenceStatus = FaceReferenceStatus.NotReference,
                    AssignedByUserId = userId,
                    AssignedAtUtc = now,
                    ConcurrencyToken = Guid.NewGuid()
                });
                await ResolvePendingDecisionsAsync(
                    assignment.MediaFaceId,
                    target.Id,
                    userId,
                    now,
                    cancellationToken);
                _db.IdentityAudits.Add(new MediaIdentityAudit
                {
                    FaceId = assignment.MediaFaceId,
                    PersonId = target.Id,
                    PreviousPersonId = source.Id,
                    NewPersonId = target.Id,
                    Action = "AssignmentMoved",
                    PerformedByUserId = userId,
                    Notes = correctionReason,
                    PerformedAtUtc = now
                });
            }

            await RefreshRepresentativeAfterRemovalAsync(source, selectedFaces, now, cancellationToken);
            if (!target.RepresentativeFaceId.HasValue)
            {
                target.RepresentativeFaceId = selectedFaces[0];
            }

            target.Status = MediaPersonStatus.Confirmed;
            target.IsHidden = false;
            target.UpdatedAtUtc = now;
            target.ConcurrencyToken = Guid.NewGuid();
            _db.IdentityAudits.Add(new MediaIdentityAudit
            {
                FaceId = selectedFaces[0],
                PersonId = target.Id,
                PreviousPersonId = source.Id,
                NewPersonId = target.Id,
                Action = "AppearancesMoved",
                PerformedByUserId = userId,
                Notes = $"Moved {selectedFaces.Count} appearance(s) from '{source.DisplayName}' to '{target.DisplayName}'. {correctionReason}",
                MetadataJson = JsonSerializer.Serialize(new { FaceIds = selectedFaces }),
                PerformedAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await RefreshCandidatesAfterIdentityChangeAsync(cancellationToken);
    }

    public async Task<Guid> SplitToNewPersonAsync(
        Guid sourcePersonId,
        IReadOnlyCollection<Guid> faceIds,
        string displayName,
        string userId,
        string reason,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var selectedFaces = NormalizeFaceSelection(faceIds);
        var normalized = NormalizeName(displayName);
        var correctionReason = RequireReason(reason);
        var newPersonId = Guid.NewGuid();

        await ExecuteTransactionalAsync(async () =>
        {
            var source = await RequireActivePersonAsync(sourcePersonId, cancellationToken);
            var assignments = await RequireActiveAssignmentsAsync(
                sourcePersonId,
                selectedFaces,
                cancellationToken);
            await EnsureDistinctPhotographsAsync(selectedFaces, cancellationToken);
            var initialTrustedReferenceFaceId = await SelectInitialTrustedReferenceAsync(
                selectedFaces,
                cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var newPerson = new MediaPerson
            {
                Id = newPersonId,
                DisplayName = normalized.Display,
                NormalizedName = normalized.Search,
                Status = MediaPersonStatus.Confirmed,
                RepresentativeFaceId = selectedFaces[0],
                CreatedByUserId = userId,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _db.Persons.Add(newPerson);

            foreach (var assignment in assignments)
            {
                assignment.RemovedAtUtc = now;
                assignment.RemovedByUserId = userId;
                assignment.RemovalReason = correctionReason;
                assignment.ConcurrencyToken = Guid.NewGuid();
            }

            await _db.SaveChangesAsync(cancellationToken);
            foreach (var assignment in assignments)
            {
                _db.PersonFaces.Add(new MediaPersonFace
                {
                    MediaPersonId = newPerson.Id,
                    MediaFaceId = assignment.MediaFaceId,
                    AssignmentType = FaceAssignmentType.ManualAssignment,
                    AssignmentConfidence = assignment.AssignmentConfidence,
                    ReferenceStatus = assignment.MediaFaceId == initialTrustedReferenceFaceId
                        ? FaceReferenceStatus.TrustedReference
                        : FaceReferenceStatus.NotReference,
                    ReferenceChangedByUserId = assignment.MediaFaceId == initialTrustedReferenceFaceId ? userId : null,
                    ReferenceChangedAtUtc = assignment.MediaFaceId == initialTrustedReferenceFaceId ? now : null,
                    ReferenceChangeReason = assignment.MediaFaceId == initialTrustedReferenceFaceId
                        ? "Initial trusted reference selected when the person was split."
                        : null,
                    AssignedByUserId = userId,
                    AssignedAtUtc = now,
                    ConcurrencyToken = Guid.NewGuid()
                });
                await ResolvePendingDecisionsAsync(
                    assignment.MediaFaceId,
                    newPerson.Id,
                    userId,
                    now,
                    cancellationToken);
                _db.IdentityAudits.Add(new MediaIdentityAudit
                {
                    FaceId = assignment.MediaFaceId,
                    PersonId = newPerson.Id,
                    PreviousPersonId = source.Id,
                    NewPersonId = newPerson.Id,
                    Action = "AssignmentMoved",
                    PerformedByUserId = userId,
                    Notes = correctionReason,
                    PerformedAtUtc = now
                });
            }

            await RefreshRepresentativeAfterRemovalAsync(source, selectedFaces, now, cancellationToken);
            _db.IdentityAudits.Add(new MediaIdentityAudit
            {
                FaceId = selectedFaces[0],
                PersonId = newPerson.Id,
                PreviousPersonId = source.Id,
                NewPersonId = newPerson.Id,
                Action = "PersonSplit",
                PerformedByUserId = userId,
                Notes = $"Created '{newPerson.DisplayName}' from {selectedFaces.Count} selected appearance(s) previously assigned to '{source.DisplayName}'. {correctionReason}",
                MetadataJson = JsonSerializer.Serialize(new { FaceIds = selectedFaces }),
                PerformedAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await RefreshCandidatesAfterIdentityChangeAsync(cancellationToken);
        return newPersonId;
    }

    public async Task ReturnAssignmentsToReviewAsync(
        Guid sourcePersonId,
        IReadOnlyCollection<Guid> faceIds,
        string userId,
        string reason,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var selectedFaces = NormalizeFaceSelection(faceIds);
        var correctionReason = RequireReason(reason);

        await ExecuteTransactionalAsync(async () =>
        {
            var source = await RequireActivePersonAsync(sourcePersonId, cancellationToken);
            var assignments = await RequireActiveAssignmentsAsync(
                sourcePersonId,
                selectedFaces,
                cancellationToken);
            var now = DateTimeOffset.UtcNow;
            foreach (var assignment in assignments)
            {
                assignment.RemovedAtUtc = now;
                assignment.RemovedByUserId = userId;
                assignment.RemovalReason = correctionReason;
                assignment.ConcurrencyToken = Guid.NewGuid();
            }

            var oldDecisions = await _db.FaceReviewDecisions
                .Where(decision => selectedFaces.Contains(decision.MediaFaceId)
                                   && decision.CandidatePersonId == sourcePersonId
                                   && decision.ModelKey == _options.Embedder.Key
                                   && decision.ModelVersion == _options.Embedder.Version)
                .ToListAsync(cancellationToken);
            foreach (var decision in oldDecisions.Where(decision =>
                         decision.Decision is FaceReviewDecisionType.Pending or FaceReviewDecisionType.Confirmed))
            {
                decision.Decision = FaceReviewDecisionType.Rejected;
                decision.DecidedByUserId = userId;
                decision.DecidedAtUtc = now;
                decision.Notes = $"Previous assignment was returned to review. {correctionReason}";
                decision.ConcurrencyToken = Guid.NewGuid();
            }

            var facesWithRejectedSource = oldDecisions
                .Where(decision => decision.Decision == FaceReviewDecisionType.Rejected)
                .Select(decision => decision.MediaFaceId)
                .ToHashSet();
            foreach (var faceId in selectedFaces.Where(faceId => !facesWithRejectedSource.Contains(faceId)))
            {
                _db.FaceReviewDecisions.Add(new MediaFaceReviewDecision
                {
                    MediaFaceId = faceId,
                    CandidatePersonId = sourcePersonId,
                    Decision = FaceReviewDecisionType.Rejected,
                    ModelKey = _options.Embedder.Key,
                    ModelVersion = _options.Embedder.Version,
                    DecidedByUserId = userId,
                    Notes = $"Previous assignment was returned to review. {correctionReason}",
                    ConcurrencyToken = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    DecidedAtUtc = now
                });
            }

            await RefreshRepresentativeAfterRemovalAsync(source, selectedFaces, now, cancellationToken);
            foreach (var faceId in selectedFaces)
            {
                _db.IdentityAudits.Add(new MediaIdentityAudit
                {
                    FaceId = faceId,
                    PersonId = source.Id,
                    PreviousPersonId = source.Id,
                    Action = "AssignmentRemoved",
                    PerformedByUserId = userId,
                    Notes = correctionReason,
                    PerformedAtUtc = now
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await RefreshCandidatesAfterIdentityChangeAsync(cancellationToken);
    }

    public async Task MergePeopleAsync(
        Guid sourcePersonId,
        Guid targetPersonId,
        string userId,
        string reason,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var mergeReason = RequireReason(reason);
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
            var sourceFaceIds = sourceAssignments.Select(assignment => assignment.MediaFaceId).ToList();
            if (sourceFaceIds.Count > 0)
            {
                await EnsureNoSamePhotographConflictAsync(
                    sourceFaceIds,
                    targetPersonId,
                    cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var assignment in sourceAssignments)
            {
                assignment.RemovedAtUtc = now;
                assignment.RemovedByUserId = userId;
                assignment.RemovalReason = $"Merged into {target.DisplayName}. {mergeReason}";
                assignment.ConcurrencyToken = Guid.NewGuid();
            }

            // Release the one-active-assignment-per-face constraint before inserting replacements.
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var sourceAssignment in sourceAssignments)
            {
                _db.PersonFaces.Add(new MediaPersonFace
                {
                    MediaPersonId = target.Id,
                    MediaFaceId = sourceAssignment.MediaFaceId,
                    AssignmentType = FaceAssignmentType.ManualAssignment,
                    AssignmentConfidence = sourceAssignment.AssignmentConfidence,
                    ReferenceStatus = sourceAssignment.ReferenceStatus,
                    ReferenceChangedByUserId = sourceAssignment.ReferenceChangedByUserId,
                    ReferenceChangedAtUtc = sourceAssignment.ReferenceChangedAtUtc,
                    ReferenceChangeReason = sourceAssignment.ReferenceChangeReason,
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
                    Notes = mergeReason,
                    PerformedAtUtc = now
                });
            }

            if (!target.RepresentativeFaceId.HasValue)
            {
                target.RepresentativeFaceId = source.RepresentativeFaceId.HasValue
                                              && sourceFaceIds.Contains(source.RepresentativeFaceId.Value)
                    ? source.RepresentativeFaceId
                    : sourceFaceIds.Select(faceId => (Guid?)faceId).FirstOrDefault();
            }

            source.Status = MediaPersonStatus.Merged;
            source.IsHidden = true;
            source.MergedIntoPersonId = target.Id;
            source.RepresentativeFaceId = null;
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
            var sourceDecisionFaceIds = sourceDecisions.Select(decision => decision.MediaFaceId).ToList();
            var targetPendingFaceIds = sourceDecisionFaceIds.Count == 0
                ? new HashSet<Guid>()
                : (await _db.FaceReviewDecisions
                    .AsNoTracking()
                    .Where(existing => sourceDecisionFaceIds.Contains(existing.MediaFaceId)
                                       && existing.CandidatePersonId == targetPersonId
                                       && existing.Decision == FaceReviewDecisionType.Pending)
                    .Select(existing => existing.MediaFaceId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
            foreach (var decision in sourceDecisions)
            {
                if (targetPendingFaceIds.Contains(decision.MediaFaceId))
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
                Notes = $"Merged '{source.DisplayName}' into '{target.DisplayName}'. {mergeReason}",
                MetadataJson = JsonSerializer.Serialize(new { FaceIds = sourceFaceIds }),
                PerformedAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await RefreshCandidatesAfterIdentityChangeAsync(cancellationToken);
    }

    private async Task AssignCoreAsync(
        Guid faceId,
        MediaPerson person,
        string userId,
        double? confidence,
        FaceAssignmentType assignmentType,
        bool trustAsReference,
        CancellationToken cancellationToken)
    {
        EnsureActive(person);
        var face = await _db.Faces
            .Include(item => item.MediaAsset)
            .SingleOrDefaultAsync(item => item.Id == faceId && !item.IsSuppressed, cancellationToken)
            ?? throw new KeyNotFoundException("The detected face is unavailable or has been suppressed.");
        var pendingEvidence = assignmentType == FaceAssignmentType.HumanConfirmed
            ? await _db.FaceReviewDecisions
                .AsNoTracking()
                .Where(decision => decision.MediaFaceId == faceId
                                   && decision.CandidatePersonId == person.Id
                                   && decision.Decision == FaceReviewDecisionType.Pending)
                .OrderByDescending(decision => decision.CreatedAtUtc)
                .Select(decision => new
                {
                    decision.Similarity,
                    decision.BestReferenceSimilarity,
                    decision.MeanTopSimilarity,
                    decision.ReferenceCount,
                    decision.MarginToNext,
                    decision.MarginAvailable,
                    decision.ConfidenceLevel,
                    decision.ModelKey,
                    decision.ModelVersion
                })
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var effectiveConfidence = pendingEvidence?.Similarity;
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
            AssignmentConfidence = effectiveConfidence,
            ReferenceStatus = trustAsReference
                ? FaceReferenceStatus.TrustedReference
                : FaceReferenceStatus.NotReference,
            ReferenceChangedByUserId = trustAsReference ? userId : null,
            ReferenceChangedAtUtc = trustAsReference ? now : null,
            ReferenceChangeReason = trustAsReference
                ? "Initial trusted reference selected when the person was created."
                : null,
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
        face.CandidateSearchStatus = FaceCandidateSearchStatus.Ready;
        face.CandidateSearchFailureReason = null;
        face.CandidateSearchCompletedAtUtc = now;
        face.UpdatedAtUtc = now;
        face.ConcurrencyToken = Guid.NewGuid();
        await ResolvePendingDecisionsAsync(faceId, person.Id, userId, now, cancellationToken);
        var assignmentMethod = pendingEvidence is null
            ? "manual reviewer assignment"
            : pendingEvidence.ConfidenceLevel == FaceCandidateConfidenceLevel.Strong
                ? "strong known-person candidate"
                : "possible known-person match";
        var separationText = pendingEvidence?.MarginAvailable == true
                             && pendingEvidence.MarginToNext.HasValue
            ? pendingEvidence.MarginToNext.Value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
            : "unavailable";
        var auditNotes = pendingEvidence is null
            ? $"Confirmed by manual reviewer assignment in '{face.MediaAsset.ContextTitle}'."
            : $"Confirmed from a {assignmentMethod} in '{face.MediaAsset.ContextTitle}'. "
              + $"Similarity {pendingEvidence.Similarity:0.000}; trusted references {pendingEvidence.ReferenceCount}; "
              + $"separation {separationText}.";

        _db.IdentityAudits.Add(new MediaIdentityAudit
        {
            FaceId = faceId,
            PersonId = person.Id,
            PreviousPersonId = previousPersonId,
            NewPersonId = person.Id,
            Action = previousPersonId.HasValue ? "FaceReassigned" : "FaceAssigned",
            PerformedByUserId = userId,
            Notes = TrimTo(auditNotes, 1024),
            MetadataJson = JsonSerializer.Serialize(new
            {
                SourceAssetId = face.MediaAssetId,
                SourceTitle = face.MediaAsset.ContextTitle,
                SourceSubtitle = face.MediaAsset.ContextSubtitle,
                AssignmentMethod = assignmentMethod,
                Similarity = pendingEvidence?.Similarity,
                BestReferenceSimilarity = pendingEvidence?.BestReferenceSimilarity,
                MeanTopSimilarity = pendingEvidence?.MeanTopSimilarity,
                ReferenceCount = pendingEvidence?.ReferenceCount ?? 0,
                MarginToNext = pendingEvidence?.MarginToNext,
                MarginAvailable = pendingEvidence?.MarginAvailable ?? false,
                ConfidenceLevel = pendingEvidence?.ConfidenceLevel.ToString(),
                ModelKey = pendingEvidence?.ModelKey,
                ModelVersion = pendingEvidence?.ModelVersion
            }),
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

    private async Task RefreshCandidatesAfterIdentityChangeAsync(CancellationToken cancellationToken)
    {
        _groupingState.Invalidate();
        try
        {
            await _candidateRefreshQueue.QueueAllUnassignedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Identity confirmation remains committed even if optional suggestion refresh fails.
            _logger.LogWarning(
                exception,
                "Unable to refresh remaining face candidates after an identity change.");
        }
    }


    private async Task<Guid?> SelectInitialTrustedReferenceAsync(
        IReadOnlyCollection<Guid> faceIds,
        CancellationToken cancellationToken)
    {
        if (faceIds.Count == 0)
        {
            return null;
        }

        var modelKey = _options.Embedder.Key;
        var modelVersion = _options.Embedder.Version;
        var dimension = _options.Embedder.EmbeddingDimension;
        return await _db.Faces
            .AsNoTracking()
            .Where(face => faceIds.Contains(face.Id)
                           && !face.IsSuppressed
                           && face.QualityStatus == FaceQualityStatus.EmbeddingEligible
                           && face.QualityScore >= _options.CandidateMinimumTrustedReferenceQuality
                           && face.MediaAsset.IsAvailable
                           && !face.MediaAsset.IsDeleted
                           && !face.MediaAsset.IsArchived
                           && face.Embeddings.Any(embedding =>
                               embedding.InvalidatedAtUtc == null
                               && embedding.ModelKey == modelKey
                               && embedding.ModelVersion == modelVersion
                               && embedding.Dimension == dimension))
            .OrderByDescending(face => face.QualityScore)
            .ThenBy(face => face.SequenceNumber)
            .Select(face => (Guid?)face.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<List<MediaPersonFace>> RequireActiveAssignmentsAsync(
        Guid sourcePersonId,
        IReadOnlyList<Guid> faceIds,
        CancellationToken cancellationToken)
    {
        var assignments = await _db.PersonFaces
            .Where(assignment => assignment.MediaPersonId == sourcePersonId
                                 && faceIds.Contains(assignment.MediaFaceId)
                                 && assignment.RemovedAtUtc == null
                                 && !assignment.MediaFace.IsSuppressed
                                 && assignment.MediaFace.MediaAsset.IsAvailable
                                 && !assignment.MediaFace.MediaAsset.IsDeleted
                                 && !assignment.MediaFace.MediaAsset.IsArchived)
            .ToListAsync(cancellationToken);
        if (assignments.Count != faceIds.Count)
        {
            throw new FaceIdentityConflictException(
                "One or more selected appearances are unavailable or no longer belong to this person. Refresh the page and try again.");
        }

        return assignments;
    }

    private async Task EnsureDistinctPhotographsAsync(
        IReadOnlyCollection<Guid> faceIds,
        CancellationToken cancellationToken)
    {
        if (faceIds.Count < 2)
        {
            return;
        }

        var assetIds = await _db.Faces
            .AsNoTracking()
            .Where(face => faceIds.Contains(face.Id))
            .Select(face => face.MediaAssetId)
            .ToListAsync(cancellationToken);
        if (assetIds.Count != faceIds.Count || assetIds.Distinct().Count() != assetIds.Count)
        {
            throw new FaceIdentityConflictException(
                "Two selected faces come from the same photograph. They cannot be assigned to one person in a batch; review them separately.");
        }
    }

    private async Task EnsureNoSamePhotographConflictAsync(
        IReadOnlyCollection<Guid> faceIds,
        Guid targetPersonId,
        CancellationToken cancellationToken)
    {
        await EnsureDistinctPhotographsAsync(faceIds, cancellationToken);
        var selectedAssetIds = await _db.Faces
            .AsNoTracking()
            .Where(face => faceIds.Contains(face.Id))
            .Select(face => face.MediaAssetId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (selectedAssetIds.Count == 0)
        {
            throw new FaceIdentityConflictException("The selected appearances are no longer available.");
        }

        var conflictExists = await _db.PersonFaces
            .AsNoTracking()
            .AnyAsync(assignment => assignment.MediaPersonId == targetPersonId
                                    && assignment.RemovedAtUtc == null
                                    && selectedAssetIds.Contains(assignment.MediaFace.MediaAssetId),
                cancellationToken);
        if (conflictExists)
        {
            throw new FaceIdentityConflictException(
                "The target person already has a confirmed face in one of the selected photographs. Correct the conflicting photograph before moving or merging identities.");
        }
    }

    private async Task RefreshRepresentativeAfterRemovalAsync(
        MediaPerson person,
        IReadOnlyCollection<Guid> removedFaceIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var activeAssignments = _db.PersonFaces
            .AsNoTracking()
            .Where(assignment => assignment.MediaPersonId == person.Id
                                 && assignment.RemovedAtUtc == null
                                 && !removedFaceIds.Contains(assignment.MediaFaceId)
                                 && !assignment.MediaFace.IsSuppressed);

        var hasAnyActiveAssignment = await activeAssignments.AnyAsync(cancellationToken);
        if (!hasAnyActiveAssignment)
        {
            person.RepresentativeFaceId = null;
            person.IsHidden = true;
            person.Status = MediaPersonStatus.Hidden;
            person.UpdatedAtUtc = now;
            person.ConcurrencyToken = Guid.NewGuid();
            return;
        }

        if (person.RepresentativeFaceId.HasValue
            && !removedFaceIds.Contains(person.RepresentativeFaceId.Value))
        {
            return;
        }

        // Prefer an appearance whose source is currently available, while still
        // retaining the person when only audited/unavailable appearances remain.
        var availableRepresentative = await activeAssignments
            .Where(assignment => assignment.MediaFace.MediaAsset.IsAvailable
                                 && !assignment.MediaFace.MediaAsset.IsDeleted
                                 && !assignment.MediaFace.MediaAsset.IsArchived)
            .OrderByDescending(assignment => assignment.MediaFace.QualityScore)
            .ThenByDescending(assignment => assignment.AssignedAtUtc)
            .Select(assignment => (Guid?)assignment.MediaFaceId)
            .FirstOrDefaultAsync(cancellationToken);
        person.RepresentativeFaceId = availableRepresentative
            ?? await activeAssignments
                .OrderByDescending(assignment => assignment.MediaFace.QualityScore)
                .ThenByDescending(assignment => assignment.AssignedAtUtc)
                .Select(assignment => (Guid?)assignment.MediaFaceId)
                .FirstOrDefaultAsync(cancellationToken);
        person.UpdatedAtUtc = now;
        person.ConcurrencyToken = Guid.NewGuid();
    }

    private async Task ValidateUnassignedFaceAsync(
        Guid faceId,
        CancellationToken cancellationToken)
    {
        var valid = await _db.Faces
            .AsNoTracking()
            .AnyAsync(face => face.Id == faceId
                              && !face.IsSuppressed
                              && face.QualityStatus != FaceQualityStatus.ProcessingFailed
                              && face.MediaAsset.IsAvailable
                              && !face.MediaAsset.IsDeleted
                              && !face.MediaAsset.IsArchived
                              && !face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null),
                cancellationToken);
        if (!valid)
        {
            throw new FaceIdentityConflictException(
                "The selected face is unavailable or has already been assigned. Refresh the page and try again.");
        }
    }

    private async Task ValidateGroupSelectionAsync(
        IReadOnlyList<Guid> faceIds,
        bool requireUnassigned,
        CancellationToken cancellationToken)
    {
        if (faceIds.Count > Math.Clamp(_options.GroupingMaximumGroupSize, 2, 500))
        {
            throw new ArgumentException(
                $"A maximum of {_options.GroupingMaximumGroupSize} appearances can be confirmed together.",
                nameof(faceIds));
        }

        var modelKey = _options.Embedder.Key;
        var modelVersion = _options.Embedder.Version;
        var dimension = _options.Embedder.EmbeddingDimension;
        var rows = await _db.Faces
            .AsNoTracking()
            .Where(face => faceIds.Contains(face.Id)
                           && !face.IsSuppressed
                           && face.QualityStatus == FaceQualityStatus.EmbeddingEligible
                           && face.MediaAsset.IsAvailable
                           && !face.MediaAsset.IsDeleted
                           && !face.MediaAsset.IsArchived
                           && (!requireUnassigned
                               || !face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null))
                           && face.Embeddings.Any(embedding =>
                               embedding.InvalidatedAtUtc == null
                               && embedding.ModelKey == modelKey
                               && embedding.ModelVersion == modelVersion
                               && embedding.Dimension == dimension))
            .Select(face => new { face.Id, face.MediaAssetId })
            .ToListAsync(cancellationToken);
        if (rows.Count != faceIds.Count)
        {
            throw new FaceIdentityConflictException(
                "One or more selected appearances are unavailable, already assigned, or no longer compatible with the current embedding model. Refresh the page and review the group again.");
        }

        if (faceIds.Count > 1 && rows.Select(row => row.MediaAssetId).Distinct().Count() != rows.Count)
        {
            throw new FaceIdentityConflictException(
                "Two faces from the same photograph cannot be confirmed as one person in a batch. Review those faces individually.");
        }
    }

    private static IReadOnlyList<Guid> NormalizeFaceSelection(IReadOnlyCollection<Guid> faceIds)
    {
        ArgumentNullException.ThrowIfNull(faceIds);
        var selected = faceIds
            .Where(faceId => faceId != Guid.Empty)
            .Distinct()
            .Take(500)
            .ToList();
        if (selected.Count == 0)
        {
            throw new ArgumentException("Select at least one detected face.", nameof(faceIds));
        }

        return selected;
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

    private static string RequireReason(string? value)
    {
        var reason = CleanOptionalText(value, 1024);
        if (reason is null || reason.Length < 3)
        {
            throw new ArgumentException("A correction reason of at least 3 characters is required.", nameof(value));
        }

        return reason;
    }

    private static string? CleanOptionalText(string? value, int maximumLength)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return cleaned is { Length: > 0 }
            ? cleaned[..Math.Min(cleaned.Length, maximumLength)]
            : null;
    }

    private static string TrimTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static void ValidateUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("An authenticated reviewer is required.", nameof(userId));
        }
    }
}
