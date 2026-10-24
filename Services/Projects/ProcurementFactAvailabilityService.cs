using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services.Arpp;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// Decides which stages' procurement figures a project can record now, using
/// <see cref="ProcurementStageRules.CanRecordFact"/>. Predecessors read the way a stage
/// request reads them: the published ARPP record settles IPA, and a pending request to
/// complete or skip a predecessor counts as resolving it.
/// </summary>
public sealed class ProcurementFactAvailabilityService
{
    private const string PendingDecisionStatus = "Pending";

    private readonly ApplicationDbContext _db;
    private readonly IProjectStageWorkflowPolicy _workflowPolicy;
    private readonly IArppIpaStageAuthorityService _arppIpaStageAuthority;

    public ProcurementFactAvailabilityService(
        ApplicationDbContext db,
        IProjectStageWorkflowPolicy workflowPolicy,
        IArppIpaStageAuthorityService? arppIpaStageAuthority = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _workflowPolicy = workflowPolicy ?? throw new ArgumentNullException(nameof(workflowPolicy));
        _arppIpaStageAuthority = arppIpaStageAuthority ?? new ArppIpaStageAuthorityService(db);
    }

    public static ProcurementFactAvailabilityService Create(ApplicationDbContext db)
        => new(db, new ProjectStageWorkflowPolicy(db, new WorkflowStageMetadataProvider()));

    public async Task<Func<string, bool>> GetRecordableStagesAsync(int projectId, CancellationToken ct = default)
    {
        var stages = await _db.ProjectStages
            .AsNoTracking()
            .Where(stage => stage.ProjectId == projectId && stage.StageCode != null)
            .Select(stage => new { stage.StageCode, stage.Status, stage.CompletedOn })
            .ToListAsync(ct);
        if (stages.Count == 0)
        {
            return _ => false;
        }

        var workflow = await _workflowPolicy.GetAsync(projectId, ct);
        var ipaAuthority = await _arppIpaStageAuthority.ResolveAsync(projectId, ct);

        var statuses = stages
            .GroupBy(stage => stage.StageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var stage = group.First();
                    return StageEffectiveState.Resolve(
                        stage.StageCode,
                        new StageChronologyState(stage.Status, stage.CompletedOn),
                        ipaAuthority)!.Status;
                },
                StringComparer.OrdinalIgnoreCase);

        var pendingRequests = await _db.StageChangeRequests
            .AsNoTracking()
            .Where(request => request.ProjectId == projectId && request.DecisionStatus == PendingDecisionStatus)
            .Select(request => new { request.StageCode, request.RequestedStatus, request.RequestedOn, request.Id })
            .ToListAsync(ct);
        var pendingResolved = pendingRequests
            .GroupBy(request => request.StageCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(request => request.RequestedOn).ThenByDescending(request => request.Id).First())
            .Where(request => Enum.TryParse<StageStatus>(request.RequestedStatus, ignoreCase: true, out var status)
                && status is StageStatus.Completed or StageStatus.Skipped)
            .Select(request => request.StageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        StageStatus? PredecessorStatus(string code)
            => pendingResolved.Contains(code)
                ? StageStatus.Completed
                : statuses.TryGetValue(code, out var status) ? status : null;

        return stageCode =>
        {
            if (!statuses.TryGetValue(stageCode, out var own))
            {
                return false;
            }

            return ProcurementStageRules.CanRecordFact(
                workflow,
                stageCode,
                code => string.Equals(code, stageCode, StringComparison.OrdinalIgnoreCase) ? own : PredecessorStatus(code));
        };
    }
}
