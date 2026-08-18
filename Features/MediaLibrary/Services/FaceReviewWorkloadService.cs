using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;

namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Produces the authoritative workload counts shown throughout People review. The categories
/// deliberately reflect workflow state rather than raw face-table state: matching faces remain
/// unresolved, but are not simultaneously presented as actionable individual review items.
/// </summary>
public sealed class FaceReviewWorkloadService : IFaceReviewWorkloadService
{
    private readonly MediaLibraryDbContext _db;
    private readonly IMediaAssetVisibilityPolicy _visibility;
    private readonly IFaceIdentityGroupingRuntimeState _groupingState;
    private readonly bool _groupingOperational;

    public FaceReviewWorkloadService(
        MediaLibraryDbContext db,
        IMediaAssetVisibilityPolicy visibility,
        IFaceIdentityGroupingRuntimeState groupingState,
        IOptions<MediaLibraryOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _visibility = visibility ?? throw new ArgumentNullException(nameof(visibility));
        _groupingState = groupingState ?? throw new ArgumentNullException(nameof(groupingState));
        var configured = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _groupingOperational = configured.IsPeopleWorkerEnabled && configured.People.GroupingEnabled;
    }

    public async Task<FaceReviewWorkloadSummary> GetAsync(
        FaceReviewWorkloadQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var assetIds = (query.AssetIds ?? Array.Empty<long>())
            .Where(id => id > 0)
            .Distinct()
            .Take(250)
            .ToArray();
        var visibleAssetIds = _visibility
            .Apply(_db.Assets.AsNoTracking())
            .Select(asset => asset.Id);

        var active = MediaPeopleQueryService.BuildReviewableFacesQuery(_db)
            .Where(face => visibleAssetIds.Contains(face.MediaAssetId));
        if (assetIds.Length > 0)
        {
            active = active.Where(face => assetIds.Contains(face.MediaAssetId));
        }

        var hasPendingKnownPerson = BuildActivePendingKnownPersonDecisions(_db);

        var totalUnresolved = await active.CountAsync(cancellationToken);
        var knownMatches = await active.CountAsync(face =>
            face.CandidateSearchStatus == FaceCandidateSearchStatus.Ready
            && hasPendingKnownPerson.Any(decision => decision.MediaFaceId == face.Id),
            cancellationToken);
        var matching = await active.CountAsync(face =>
            face.CandidateSearchStatus == FaceCandidateSearchStatus.Pending
            || face.CandidateSearchStatus == FaceCandidateSearchStatus.Processing,
            cancellationToken);
        var individual = await active.CountAsync(face =>
            face.CandidateSearchStatus != FaceCandidateSearchStatus.Pending
            && face.CandidateSearchStatus != FaceCandidateSearchStatus.Processing
            && !(face.CandidateSearchStatus == FaceCandidateSearchStatus.Ready
                 && hasPendingKnownPerson.Any(decision => decision.MediaFaceId == face.Id)),
            cancellationToken);
        var failures = await active.CountAsync(face =>
            face.CandidateSearchStatus == FaceCandidateSearchStatus.Failed,
            cancellationToken);

        var closed = BuildClosedUnidentifiedFacesQuery(_db, visibleAssetIds);
        if (assetIds.Length > 0)
        {
            closed = closed.Where(face => assetIds.Contains(face.MediaAssetId));
        }
        var closedCount = await closed.CountAsync(cancellationToken);

        // Identity groups are a corpus-level snapshot. When review is explicitly scoped to a
        // selected set of media, do not mix global group counts or refresh state into the
        // scoped workload. The Groups workspace itself deliberately returns to corpus scope.
        var grouping = assetIds.Length == 0 && _groupingOperational
            ? _groupingState.GetSnapshot()
            : new FaceIdentityGroupingRuntimeSnapshot(null, null, null);
        var groupingResult = grouping.Result;
        return new FaceReviewWorkloadSummary(
            knownMatches,
            individual,
            matching,
            failures,
            closedCount,
            totalUnresolved,
            groupingResult?.TotalGroups ?? 0,
            groupingResult?.GroupedFaceCount ?? 0,
            groupingResult?.UngroupedFaceCount ?? 0,
            grouping.IsReady,
            grouping.IsRefreshPending,
            grouping.RefreshedAtUtc,
            grouping.FailureReason);
    }

    internal static IQueryable<MediaFaceReviewDecision> BuildActivePendingKnownPersonDecisions(
        MediaLibraryDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.FaceReviewDecisions
            .AsNoTracking()
            .Where(decision => decision.Decision == FaceReviewDecisionType.Pending
                               && decision.CandidatePersonId.HasValue
                               && decision.CandidatePerson != null
                               && !decision.CandidatePerson.IsHidden
                               && decision.CandidatePerson.Status == MediaPersonStatus.Confirmed);
    }

    internal static IQueryable<MediaFace> BuildClosedUnidentifiedFacesQuery(
        MediaLibraryDbContext db,
        IQueryable<long>? visibleAssetIds = null)
    {
        ArgumentNullException.ThrowIfNull(db);

        var query = db.Faces
            .AsNoTracking()
            .Where(face => !face.IsSuppressed
                           && face.QualityStatus != FaceQualityStatus.ProcessingFailed
                           && face.MediaAsset.IsAvailable
                           && !face.MediaAsset.IsDeleted
                           && !face.MediaAsset.IsArchived
                           && !db.PersonFaces.Any(assignment =>
                               assignment.MediaFaceId == face.Id
                               && assignment.RemovedAtUtc == null)
                           && db.FaceReviewDecisions.Any(decision =>
                               decision.MediaFaceId == face.Id
                               && !decision.CandidatePersonId.HasValue
                               && decision.Decision == FaceReviewDecisionType.Ignored));

        return visibleAssetIds is null
            ? query
            : query.Where(face => visibleAssetIds.Contains(face.MediaAssetId));
    }
}
