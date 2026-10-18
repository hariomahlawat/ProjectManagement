using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services.Plans;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Stages;

public record PlanDiffRow(string StageCode, DateOnly? NewStart, DateOnly? NewDue, DateOnly? OldStart, DateOnly? OldDue);

public sealed record PlanReviewHistory(string Action, string? Note, DateTimeOffset PerformedOn, string? PerformedBy);

/// <summary>The exact submitted revision and its comparison, bound together for review.</summary>
public sealed record PlanReviewSnapshot(
    int PlanVersionId,
    int VersionNo,
    PlanVersionStatus Status,
    DateTimeOffset? SubmittedOn,
    string? SubmittedByUserId,
    string? SubmittedBy,
    IReadOnlyList<PlanDiffRow> Rows,
    string? RealignmentSourceStageCode,
    int? RealignmentDelayDays,
    IReadOnlyList<PlanReviewHistory> ApprovalHistory);

public sealed class PlanCompareService
{
    private readonly ApplicationDbContext _db;

    public PlanCompareService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlanDiffRow>> GetDiffAsync(int projectId, CancellationToken ct = default)
    {
        var lastSnapshot = await _db.ProjectPlanSnapshots
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.TakenAt)
            .FirstOrDefaultAsync(ct);

        var currentStages = await _db.ProjectStages
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.StageCode)
            .ToListAsync(ct);

        if (lastSnapshot is null)
        {
            return currentStages
                .Select(stage => new PlanDiffRow(
                    stage.StageCode,
                    stage.PlannedStart,
                    stage.PlannedDue,
                    null,
                    null))
                .ToList();
        }

        var approved = await _db.ProjectPlanSnapshotRows
            .Where(r => r.SnapshotId == lastSnapshot.Id)
            .ToDictionaryAsync(r => r.StageCode, ct);

        return currentStages
            .Select(stage =>
            {
                approved.TryGetValue(stage.StageCode, out var row);
                return new PlanDiffRow(
                    stage.StageCode,
                    stage.PlannedStart,
                    stage.PlannedDue,
                    row?.PlannedStart,
                    row?.PlannedDue);
            })
            .ToList();
    }

    // Compatibility read: callers that display a project-level pending review should
    // resolve its identity once and carry that identity through the resulting form.
    public async Task<IReadOnlyList<PlanDiffRow>> GetDraftVsCurrentAsync(
        int projectId, CancellationToken ct = default)
    {
        var review = await GetLatestPendingReviewAsync(projectId, ct);
        return review?.Rows ?? Array.Empty<PlanDiffRow>();
    }

    public async Task<PlanReviewSnapshot?> GetLatestPendingReviewAsync(
        int projectId, CancellationToken ct = default)
    {
        var pendingId = await PlanRevisionConsistency.ActionablePending(_db)
            .AsNoTracking()
            .Where(v => v.ProjectId == projectId)
            .OrderByDescending(v => v.SubmittedOn)
            .ThenByDescending(v => v.VersionNo)
            .Select(v => (int?)v.Id)
            .FirstOrDefaultAsync(ct);

        return pendingId.HasValue
            ? await GetReviewAsync(projectId, pendingId.Value, ct)
            : null;
    }

    // An exact request must never display another pending revision's dates.
    public async Task<IReadOnlyList<PlanDiffRow>> GetVersionVsCurrentAsync(
        int projectId, int planVersionId, CancellationToken ct = default)
    {
        var review = await GetReviewAsync(projectId, planVersionId, ct);
        return review?.Rows ?? Array.Empty<PlanDiffRow>();
    }

    public async Task<PlanReviewSnapshot?> GetReviewAsync(
        int projectId, int planVersionId, CancellationToken ct = default)
    {
        var draft = await _db.PlanVersions
            .AsNoTracking()
            .Include(v => v.StagePlans)
            .Include(v => v.SubmittedByUser)
            .Include(v => v.ApprovalLogs).ThenInclude(log => log.PerformedByUser)
            .SingleOrDefaultAsync(v => v.ProjectId == projectId && v.Id == planVersionId, ct);

        if (draft is null)
            return null;

        var draftLookup = draft.StagePlans
            .Where(sp => !string.IsNullOrWhiteSpace(sp.StageCode))
            .ToDictionary(sp => sp.StageCode!, sp => sp, StringComparer.OrdinalIgnoreCase);

        var currentStages = await _db.ProjectStages
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.StageCode)
            .ToListAsync(ct);

        var workflowVersion = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.WorkflowVersion)
            .FirstOrDefaultAsync(ct);

        var stageOrderLookup = ProcurementWorkflow.BuildOrderLookup(workflowVersion);
        var currentLookup = currentStages
            .Where(s => !string.IsNullOrWhiteSpace(s.StageCode))
            .ToDictionary(s => s.StageCode!, s => s, StringComparer.OrdinalIgnoreCase);

        var codes = currentLookup.Keys
            .Union(draftLookup.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => stageOrderLookup.TryGetValue(code, out var index) ? index : int.MaxValue)
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var diffs = new List<PlanDiffRow>(codes.Count);
        foreach (var code in codes)
        {
            draftLookup.TryGetValue(code, out var draftRow);
            currentLookup.TryGetValue(code, out var currentRow);
            diffs.Add(new PlanDiffRow(
                code,
                draftRow?.PlannedStart,
                draftRow?.PlannedDue,
                currentRow?.PlannedStart,
                currentRow?.PlannedDue));
        }

        var submittedBy = draft.SubmittedByUser?.FullName
            ?? draft.SubmittedByUser?.UserName
            ?? draft.SubmittedByUserId;
        var realignment = await _db.PlanRealignmentAudits
            .AsNoTracking()
            .Where(audit => audit.ProjectId == projectId &&
                            audit.PlanVersionNo == draft.VersionNo)
            .FirstOrDefaultAsync(ct);
        var history = draft.ApprovalLogs
            .OrderByDescending(log => log.PerformedOn)
            .Take(5)
            .Select(log => new PlanReviewHistory(
                log.Action,
                string.IsNullOrWhiteSpace(log.Note) ? null : log.Note.Trim(),
                log.PerformedOn,
                log.PerformedByUser?.FullName ?? log.PerformedByUser?.UserName ?? log.PerformedByUserId))
            .ToArray();

        return new PlanReviewSnapshot(
            draft.Id, draft.VersionNo, draft.Status, draft.SubmittedOn, draft.SubmittedByUserId, submittedBy, diffs,
            realignment?.SourceStageCode, realignment?.DelayDays, history);
    }
}
