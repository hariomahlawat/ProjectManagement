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
    private readonly IFaceReviewInvalidationCoordinator _invalidation;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly IFaceReferenceReadinessService _referenceReadiness;
    private readonly MediaPeopleOptions _options;
    private readonly ILogger<FaceReviewService> _logger;

    public FaceReviewService(
        MediaLibraryDbContext db,
        IFaceReviewInvalidationCoordinator invalidation,
        IMediaAssetVisibilityPolicy visibility,
        IFaceReferenceReadinessService referenceReadiness,
        IOptions<MediaLibraryOptions> options,
        ILogger<FaceReviewService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _invalidation = invalidation ?? throw new ArgumentNullException(nameof(invalidation));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _referenceReadiness = referenceReadiness ?? throw new ArgumentNullException(nameof(referenceReadiness));
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

        if (initialTrustedReferenceFaceId.HasValue)
        {
            await _invalidation.NotifyReferenceEvidenceChangedAsync(cancellationToken);
        }
        else
        {
            _invalidation.NotifyGroupingChanged();
        }
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
        CancellationToken cancellationToken,
        string reviewSource = "IdentityReview")
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
                    cancellationToken,
                    reviewSource);
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
                    MetadataJson = JsonSerializer.Serialize(new { FaceIds = selectedFaces, Similarity = confidence, Source = reviewSource }),
                    PerformedAtUtc = DateTimeOffset.UtcNow
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        _invalidation.NotifyGroupingChanged();
    }

    public Task ConfirmCandidateAsync(
        Guid faceId,
        Guid personId,
        string userId,
        CancellationToken cancellationToken)
        => ConfirmCandidateManyAsync(
            new[] { faceId },
            personId,
            userId,
            cancellationToken);

    public async Task ConfirmCandidateManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        Guid personId,
        string userId,
        CancellationToken cancellationToken)
    {
        var selectedFaces = NormalizeFaceSelection(faceIds);
        ValidateUserId(userId);
        if (selectedFaces.Count > Math.Clamp(_options.CandidateBatchConfirmationLimit, 1, 100))
        {
            throw new FaceIdentityConflictException(
                $"No more than {_options.CandidateBatchConfirmationLimit} candidate appearances may be confirmed in one operation.");
        }
        if (selectedFaces.Count > 1)
        {
            await ValidateGroupSelectionAsync(selectedFaces, requireUnassigned: true, cancellationToken);
        }

        await ExecuteTransactionalAsync(async () =>
        {
            var person = await RequireActivePersonAsync(personId, cancellationToken);
            await RequireCurrentCandidateEvidenceAsync(selectedFaces, person.Id, cancellationToken);

            foreach (var faceId in selectedFaces)
            {
                await AssignCoreAsync(
                    faceId,
                    person,
                    userId,
                    null,
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
                    Notes = $"Confirmed {selectedFaces.Count} reviewer-selected candidate appearances as '{person.DisplayName}'.",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        FaceIds = selectedFaces,
                        Source = "PersonPhotoProfile"
                    }),
                    PerformedAtUtc = DateTimeOffset.UtcNow
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        _invalidation.NotifyGroupingChanged();
    }

    public Task RejectCandidateAsync(
        Guid faceId,
        Guid personId,
        string userId,
        CancellationToken cancellationToken)
        => RejectCandidateManyAsync(
            new[] { faceId },
            personId,
            userId,
            cancellationToken);

    public async Task RejectCandidateManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        Guid personId,
        string userId,
        CancellationToken cancellationToken)
    {
        var selectedFaces = NormalizeFaceSelection(faceIds);
        ValidateUserId(userId);
        if (selectedFaces.Count > Math.Clamp(_options.CandidateBatchConfirmationLimit, 1, 100))
        {
            throw new FaceIdentityConflictException(
                $"No more than {_options.CandidateBatchConfirmationLimit} candidate appearances may be rejected in one operation.");
        }

        await ExecuteTransactionalAsync(async () =>
        {
            var person = await RequireActivePersonAsync(personId, cancellationToken);
            await RequireCurrentCandidateEvidenceAsync(selectedFaces, person.Id, cancellationToken);

            var decisions = await _db.FaceReviewDecisions
                .Where(decision => selectedFaces.Contains(decision.MediaFaceId)
                                   && decision.CandidatePersonId == person.Id
                                   && decision.Decision == FaceReviewDecisionType.Pending
                                   && decision.ModelKey == _options.Embedder.Key
                                   && decision.ModelVersion == _options.Embedder.Version)
                .ToListAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            foreach (var decision in decisions)
            {
                decision.Decision = FaceReviewDecisionType.Rejected;
                decision.DecidedByUserId = userId;
                decision.DecidedAtUtc = now;
                decision.Notes = "Known-person suggestion rejected from the person photo profile.";
                decision.ConcurrencyToken = Guid.NewGuid();
            }

            _db.IdentityAudits.Add(new MediaIdentityAudit
            {
                FaceId = selectedFaces[0],
                PersonId = person.Id,
                PreviousPersonId = person.Id,
                Action = selectedFaces.Count == 1 ? "CandidateRejected" : "GroupCandidateRejected",
                PerformedByUserId = userId,
                Notes = selectedFaces.Count == 1
                    ? $"Rejected the suggestion that this appearance is '{person.DisplayName}'."
                    : $"Rejected the suggestion that {selectedFaces.Count} reviewer-selected appearances are '{person.DisplayName}'.",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    FaceIds = selectedFaces,
                    CandidatePersonId = person.Id,
                    Source = "PersonPhotoProfile"
                }),
                PerformedAtUtc = now
            });

            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await _invalidation.NotifyFacesNeedRematchAsync(selectedFaces, cancellationToken);
    }

    public async Task RejectAsync(
        Guid faceId,
        Guid? personId,
        string userId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        await ValidateUnassignedFaceAsync(faceId, cancellationToken);
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
        await _invalidation.NotifyFacesNeedRematchAsync(new[] { faceId }, cancellationToken);
    }

    public async Task RejectManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        Guid personId,
        string userId,
        CancellationToken cancellationToken,
        string reviewSource = "IdentityReview")
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
            MetadataJson = JsonSerializer.Serialize(new { FaceIds = selectedFaces, CandidatePersonId = personId, Source = reviewSource }),
            PerformedAtUtc = now
        });
        await SaveWithConflictTranslationAsync(cancellationToken);
        await _invalidation.NotifyFacesNeedRematchAsync(selectedFaces, cancellationToken);
    }

    public Task IgnoreAsync(
        Guid faceId,
        string userId,
        CancellationToken cancellationToken)
        => IgnoreManyAsync(new[] { faceId }, userId, cancellationToken);

    public async Task IgnoreManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        string userId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var selectedFaces = NormalizeFaceSelection(faceIds);
        var limit = Math.Clamp(_options.ReviewTriageBatchLimit, 1, 500);
        if (selectedFaces.Count > limit)
        {
            throw new ArgumentException(
                $"No more than {limit} appearances may be triaged in one operation.",
                nameof(faceIds));
        }

        await ExecuteTransactionalAsync(async () =>
        {
            var visibleAssetIds = BuildVisibleAssetIdsQuery();
            var faces = await _db.Faces
                .Include(item => item.Embeddings.Where(embedding => embedding.InvalidatedAtUtc == null))
                .Include(item => item.PersonAssignments.Where(assignment => assignment.RemovedAtUtc == null))
                .Where(item => selectedFaces.Contains(item.Id)
                               && !item.IsSuppressed
                               && visibleAssetIds.Contains(item.MediaAssetId))
                .ToListAsync(cancellationToken);
            if (faces.Count != selectedFaces.Count)
            {
                throw new FaceIdentityConflictException(
                    "One or more selected appearances are unavailable or were already processed. Refresh the page and review the remaining items.");
            }

            if (faces.Any(face => face.PersonAssignments.Count > 0))
            {
                throw new FaceIdentityConflictException(
                    "One or more selected appearances have already been assigned by another reviewer. Refresh the page before continuing.");
            }

            var pending = await _db.FaceReviewDecisions
                .Where(decision => selectedFaces.Contains(decision.MediaFaceId)
                                   && decision.Decision == FaceReviewDecisionType.Pending)
                .ToListAsync(cancellationToken);
            var acknowledged = await _db.FaceReviewDecisions
                .Where(decision => selectedFaces.Contains(decision.MediaFaceId)
                                   && !decision.CandidatePersonId.HasValue
                                   && decision.Decision == FaceReviewDecisionType.Ignored)
                .Select(decision => decision.MediaFaceId)
                .ToListAsync(cancellationToken);
            var acknowledgedSet = acknowledged.ToHashSet();
            var pendingByFace = pending
                .GroupBy(decision => decision.MediaFaceId)
                .ToDictionary(group => group.Key, group => group.ToList());
            var now = DateTimeOffset.UtcNow;

            foreach (var face in faces)
            {
                var changed = false;
                if (pendingByFace.TryGetValue(face.Id, out var facePending))
                {
                    foreach (var decision in facePending)
                    {
                        decision.Decision = FaceReviewDecisionType.Rejected;
                        decision.DecidedByUserId = userId;
                        decision.DecidedAtUtc = now;
                        decision.Notes = "Face intentionally closed as unidentified.";
                        decision.ConcurrencyToken = Guid.NewGuid();
                    }
                    changed = facePending.Count > 0;
                }

                if (!acknowledgedSet.Contains(face.Id))
                {
                    var embedding = face.Embeddings
                        .OrderByDescending(item => item.CreatedAtUtc)
                        .FirstOrDefault();
                    _db.FaceReviewDecisions.Add(new MediaFaceReviewDecision
                    {
                        MediaFaceId = face.Id,
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
                    changed = true;
                }

                face.CandidateSearchStatus = FaceCandidateSearchStatus.Ready;
                face.CandidateSearchFailureReason = null;
                face.CandidateSearchCompletedAtUtc = now;
                face.UpdatedAtUtc = now;
                face.ConcurrencyToken = Guid.NewGuid();

                if (changed)
                {
                    _db.IdentityAudits.Add(new MediaIdentityAudit
                    {
                        FaceId = face.Id,
                        Action = "FaceLeftUnidentified",
                        PerformedByUserId = userId,
                        Notes = "Authorised reviewer acknowledged the face without assigning an identity.",
                        PerformedAtUtc = now
                    });
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        _invalidation.NotifyGroupingChanged();
    }

    public Task ReopenUnidentifiedAsync(
        Guid faceId,
        string userId,
        CancellationToken cancellationToken)
        => ReopenUnidentifiedManyAsync(new[] { faceId }, userId, cancellationToken);

    public async Task ReopenUnidentifiedManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        string userId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var selectedFaces = NormalizeFaceSelection(faceIds);
        var limit = Math.Clamp(_options.ReviewTriageBatchLimit, 1, 500);
        if (selectedFaces.Count > limit)
        {
            throw new ArgumentException(
                $"No more than {limit} appearances may be reopened in one operation.",
                nameof(faceIds));
        }

        await ExecuteTransactionalAsync(async () =>
        {
            var visibleAssetIds = BuildVisibleAssetIdsQuery();
            var faces = await _db.Faces
                .Where(face => selectedFaces.Contains(face.Id)
                               && !face.IsSuppressed
                               && visibleAssetIds.Contains(face.MediaAssetId)
                               && !face.PersonAssignments.Any(assignment => assignment.RemovedAtUtc == null))
                .ToListAsync(cancellationToken);
            if (faces.Count != selectedFaces.Count)
            {
                throw new FaceIdentityConflictException(
                    "One or more selected appearances are unavailable, suppressed or already assigned. Refresh the page before continuing.");
            }

            var closures = await _db.FaceReviewDecisions
                .Where(decision => selectedFaces.Contains(decision.MediaFaceId)
                                   && !decision.CandidatePersonId.HasValue
                                   && decision.Decision == FaceReviewDecisionType.Ignored)
                .ToListAsync(cancellationToken);
            var closedFaceIds = closures.Select(decision => decision.MediaFaceId).ToHashSet();
            if (selectedFaces.Any(faceId => !closedFaceIds.Contains(faceId)))
            {
                throw new FaceIdentityConflictException(
                    "One or more selected appearances are no longer closed as unidentified. Refresh the page before continuing.");
            }

            _db.FaceReviewDecisions.RemoveRange(closures);
            var now = DateTimeOffset.UtcNow;
            foreach (var face in faces)
            {
                face.CandidateSearchFailureReason = null;
                face.CandidateSearchCompletedAtUtc = null;
                face.UpdatedAtUtc = now;
                face.ConcurrencyToken = Guid.NewGuid();
                _db.IdentityAudits.Add(new MediaIdentityAudit
                {
                    FaceId = face.Id,
                    Action = "FaceUnidentifiedReopened",
                    PerformedByUserId = userId,
                    Notes = "Closed-unidentified appearance reopened for human review and bounded candidate rematching.",
                    PerformedAtUtc = now
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await _invalidation.NotifyFacesNeedRematchAsync(selectedFaces, cancellationToken);
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

    public Task SuppressManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        string userId,
        CancellationToken cancellationToken)
        => SuppressManyAsync(
            faceIds,
            userId,
            "Reviewer marked the selected detections as not valid faces.",
            cancellationToken);

    public Task SuppressAsync(
        Guid faceId,
        string userId,
        string reason,
        CancellationToken cancellationToken)
        => SuppressManyAsync(new[] { faceId }, userId, reason, cancellationToken);

    private async Task SuppressManyAsync(
        IReadOnlyCollection<Guid> faceIds,
        string userId,
        string reason,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        var selectedFaces = NormalizeFaceSelection(faceIds);
        var limit = Math.Clamp(_options.ReviewTriageBatchLimit, 1, 500);
        if (selectedFaces.Count > limit)
        {
            throw new ArgumentException(
                $"No more than {limit} appearances may be triaged in one operation.",
                nameof(faceIds));
        }

        var normalizedReason = RequireReason(reason);
        var referenceEvidenceChanged = false;
        await ExecuteTransactionalAsync(async () =>
        {
            var visibleAssetIds = BuildVisibleAssetIdsQuery();
            var faces = await _db.Faces
                .Include(item => item.PersonAssignments.Where(assignment => assignment.RemovedAtUtc == null))
                .Include(item => item.Embeddings.Where(embedding => embedding.InvalidatedAtUtc == null))
                .Where(item => selectedFaces.Contains(item.Id)
                               && visibleAssetIds.Contains(item.MediaAssetId))
                .ToListAsync(cancellationToken);
            if (faces.Count != selectedFaces.Count)
            {
                throw new FaceIdentityConflictException(
                    "One or more selected detections no longer exist. Refresh the page and review the remaining items.");
            }

            var pending = await _db.FaceReviewDecisions
                .Where(decision => selectedFaces.Contains(decision.MediaFaceId)
                                   && decision.Decision == FaceReviewDecisionType.Pending)
                .ToListAsync(cancellationToken);
            var pendingByFace = pending
                .GroupBy(decision => decision.MediaFaceId)
                .ToDictionary(group => group.Key, group => group.ToList());
            var affectedFacesByPerson = new Dictionary<Guid, HashSet<Guid>>();
            var now = DateTimeOffset.UtcNow;

            foreach (var face in faces)
            {
                if (face.IsSuppressed)
                {
                    continue;
                }

                foreach (var assignment in face.PersonAssignments)
                {
                    if (assignment.ReferenceStatus == FaceReferenceStatus.TrustedReference)
                    {
                        referenceEvidenceChanged = true;
                    }

                    if (!affectedFacesByPerson.TryGetValue(assignment.MediaPersonId, out var removedFaces))
                    {
                        removedFaces = new HashSet<Guid>();
                        affectedFacesByPerson[assignment.MediaPersonId] = removedFaces;
                    }
                    removedFaces.Add(face.Id);

                    assignment.RemovedAtUtc = now;
                    assignment.RemovedByUserId = userId;
                    assignment.RemovalReason = normalizedReason;
                    assignment.ConcurrencyToken = Guid.NewGuid();
                }

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

                if (pendingByFace.TryGetValue(face.Id, out var facePending))
                {
                    foreach (var decision in facePending)
                    {
                        decision.Decision = FaceReviewDecisionType.Ignored;
                        decision.DecidedAtUtc = now;
                        decision.DecidedByUserId = userId;
                        decision.Notes = normalizedReason;
                        decision.ConcurrencyToken = Guid.NewGuid();
                    }
                }

                _db.IdentityAudits.Add(new MediaIdentityAudit
                {
                    FaceId = face.Id,
                    Action = "FaceSuppressed",
                    PerformedByUserId = userId,
                    Notes = normalizedReason,
                    PerformedAtUtc = now
                });
            }

            if (affectedFacesByPerson.Count > 0)
            {
                var affectedPersonIds = affectedFacesByPerson.Keys.ToArray();
                var affectedPeople = await _db.Persons
                    .Where(person => affectedPersonIds.Contains(person.Id))
                    .ToListAsync(cancellationToken);
                foreach (var person in affectedPeople)
                {
                    await RefreshRepresentativeAfterRemovalAsync(
                        person,
                        affectedFacesByPerson[person.Id],
                        now,
                        cancellationToken);
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        if (referenceEvidenceChanged)
        {
            await _invalidation.NotifyReferenceEvidenceChangedAsync(cancellationToken);
        }
        else
        {
            _invalidation.NotifyGroupingChanged();
        }
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
        await _invalidation.NotifyReferenceEvidenceChangedAsync(cancellationToken);
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
                    .ThenInclude(asset => asset.Source)
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

            FaceReferenceReadiness? trustReadiness = null;
            if (referenceStatus == FaceReferenceStatus.TrustedReference)
            {
                trustReadiness = await _referenceReadiness.GetAsync(
                    personId,
                    faceId,
                    cancellationToken);
                if (!trustReadiness.CanTrust)
                {
                    throw new FaceIdentityConflictException(trustReadiness.Message);
                }
            }
            else
            {
                var otherTrustedFaceIds = await _db.PersonFaces
                    .AsNoTracking()
                    .Where(item => item.MediaPersonId == personId
                                   && item.MediaFaceId != faceId
                                   && item.RemovedAtUtc == null
                                   && item.ReferenceStatus == FaceReferenceStatus.TrustedReference)
                    .Select(item => item.MediaFaceId)
                    .ToListAsync(cancellationToken);
                var otherReadiness = await _referenceReadiness.GetManyAsync(
                    personId,
                    otherTrustedFaceIds,
                    cancellationToken);
                if (!otherReadiness.Values.Any(item => item.IsTrusted && item.IsUsableReference))
                {
                    throw new FaceIdentityConflictException(
                        "Prepare and trust another usable matching reference before excluding the last usable reference for this person.");
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
                    ReferenceSuitability = trustReadiness?.Suitability.ToString(),
                    assignment.MediaFace.QualityScore
                }),
                PerformedAtUtc = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await _invalidation.NotifyReferenceEvidenceChangedAsync(cancellationToken);
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

        await _invalidation.NotifyReferenceEvidenceChangedAsync(cancellationToken);
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

        await _invalidation.NotifyReferenceEvidenceChangedAsync(cancellationToken);
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

        await _invalidation.NotifyReferenceEvidenceChangedAsync(cancellationToken);
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

            // A PRISM account link is institutional identity data, not merely presentation
            // metadata. Preserve it across a duplicate-person merge, but never collapse two
            // different linked user accounts into one media identity implicitly.
            var activeUserLinks = await _db.PersonUserLinks
                .Where(link => link.UnlinkedAtUtc == null
                               && (link.MediaPersonId == sourcePersonId
                                   || link.MediaPersonId == targetPersonId))
                .ToListAsync(cancellationToken);
            var sourceUserLink = activeUserLinks.SingleOrDefault(link => link.MediaPersonId == sourcePersonId);
            var targetUserLink = activeUserLinks.SingleOrDefault(link => link.MediaPersonId == targetPersonId);
            if (sourceUserLink is not null && targetUserLink is not null
                                           && !string.Equals(sourceUserLink.UserId, targetUserLink.UserId, StringComparison.Ordinal))
            {
                throw new FaceIdentityConflictException(
                    "Both identities are linked to different PRISM users. Unlink one account before merging the people records.");
            }

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

            if (sourceUserLink is not null && targetUserLink is null)
            {
                sourceUserLink.MediaPersonId = target.Id;
                sourceUserLink.ConcurrencyToken = Guid.NewGuid();
                _db.IdentityAudits.Add(new MediaIdentityAudit
                {
                    PersonId = target.Id,
                    PreviousPersonId = source.Id,
                    NewPersonId = target.Id,
                    Action = "PrismUserLinkTransferred",
                    PerformedByUserId = userId,
                    Notes = $"Transferred linked PRISM user '{sourceUserLink.UserId}' while merging '{source.DisplayName}' into '{target.DisplayName}'.",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        sourceUserLink.UserId,
                        LinkId = sourceUserLink.Id,
                        SourcePersonId = source.Id,
                        TargetPersonId = target.Id
                    }),
                    PerformedAtUtc = now
                });
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

        await _invalidation.NotifyReferenceEvidenceChangedAsync(cancellationToken);
    }

    private async Task AssignCoreAsync(
        Guid faceId,
        MediaPerson person,
        string userId,
        double? confidence,
        FaceAssignmentType assignmentType,
        bool trustAsReference,
        CancellationToken cancellationToken,
        string reviewSource = "IdentityReview")
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
        var selfConfirmed = string.Equals(reviewSource, "SelfConfirmed", StringComparison.OrdinalIgnoreCase);
        var sourceLabel = selfConfirmed
            ? "self-confirmation by the linked PRISM user"
            : "manual reviewer assignment";
        var auditNotes = pendingEvidence is null
            ? $"Confirmed by {sourceLabel} in '{face.MediaAsset.ContextTitle}'."
            : selfConfirmed
                ? $"Self-confirmed by the linked PRISM user from a {assignmentMethod} in '{face.MediaAsset.ContextTitle}'. "
                  + $"Similarity {pendingEvidence.Similarity:0.000}; trusted references {pendingEvidence.ReferenceCount}; "
                  + $"separation {separationText}."
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
                ModelVersion = pendingEvidence?.ModelVersion,
                ReviewSource = reviewSource
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

    private async Task RequireCurrentCandidateEvidenceAsync(
        IReadOnlyCollection<Guid> faceIds,
        Guid personId,
        CancellationToken cancellationToken)
    {
        var visibleAssetIds = BuildVisibleAssetIdsQuery();
        var eligibleFaceIds = await PersonPhotoDiscoveryQueryService
            .BuildCandidateRowsQuery(
                _db,
                personId,
                _options.Embedder.Key,
                _options.Embedder.Version,
                visibleAssetIds)
            .Where(row => faceIds.Contains(row.FaceId))
            .Select(row => row.FaceId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (eligibleFaceIds.Count != faceIds.Count)
        {
            throw new FaceIdentityConflictException(
                "One or more person-match suggestions are no longer current, visible, or unassigned. Refresh the profile and review the remaining suggestions.");
        }

        var hasTrustedReference = await PersonPhotoDiscoveryQueryService
            .BuildValidTrustedReferenceFacesQuery(
                _db,
                personId,
                _options.Embedder.Key,
                _options.Embedder.Version,
                _options.Embedder.EmbeddingDimension,
                visibleAssetIds)
            .AnyAsync(cancellationToken);
        if (!hasTrustedReference)
        {
            throw new FaceIdentityConflictException(
                "This person no longer has a current trusted matching reference. Review the identity before confirming suggested appearances.");
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
        var visibleAssetIds = BuildVisibleAssetIdsQuery();
        return await _db.Faces
            .AsNoTracking()
            .Where(face => faceIds.Contains(face.Id)
                           && !face.IsSuppressed
                           && face.QualityStatus == FaceQualityStatus.EmbeddingEligible
                           && face.QualityScore >= _options.CandidateMinimumTrustedReferenceQuality
                           && visibleAssetIds.Contains(face.MediaAssetId)
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
        var visibleAssetIds = BuildVisibleAssetIdsQuery();
        var assignments = await _db.PersonFaces
            .Where(assignment => assignment.MediaPersonId == sourcePersonId
                                 && faceIds.Contains(assignment.MediaFaceId)
                                 && assignment.RemovedAtUtc == null
                                 && !assignment.MediaFace.IsSuppressed
                                 && visibleAssetIds.Contains(assignment.MediaFace.MediaAssetId))
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

        // Prefer an appearance whose source is currently visible, while still
        // retaining the person when only audited/unavailable appearances remain.
        var visibleAssetIds = BuildVisibleAssetIdsQuery();
        var availableRepresentative = await activeAssignments
            .Where(assignment => visibleAssetIds.Contains(assignment.MediaFace.MediaAssetId))
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
        var visibleAssetIds = BuildVisibleAssetIdsQuery();
        var valid = await _db.Faces
            .AsNoTracking()
            .AnyAsync(face => face.Id == faceId
                              && !face.IsSuppressed
                              && face.QualityStatus != FaceQualityStatus.ProcessingFailed
                              && visibleAssetIds.Contains(face.MediaAssetId)
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
        var visibleAssetIds = BuildVisibleAssetIdsQuery();
        var rows = await _db.Faces
            .AsNoTracking()
            .Where(face => faceIds.Contains(face.Id)
                           && !face.IsSuppressed
                           && (face.QualityStatus == FaceQualityStatus.EmbeddingEligible
                               || face.QualityStatus == FaceQualityStatus.Detected
                               || face.QualityStatus == FaceQualityStatus.CropIncomplete
                               || face.QualityStatus == FaceQualityStatus.Occluded)
                           && visibleAssetIds.Contains(face.MediaAssetId)
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

    private IQueryable<long> BuildVisibleAssetIdsQuery()
        => _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);

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
