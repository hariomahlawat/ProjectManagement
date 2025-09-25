using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models.Plans;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Plans;

public class PlanApprovalService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<PlanApprovalService> _logger;

    public PlanApprovalService(ApplicationDbContext db, IClock clock, ILogger<PlanApprovalService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task SubmitAsync(int projectId, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required to submit a plan for approval.", nameof(userId));
        }

        var plan = await _db.PlanVersions
            .Include(p => p.StagePlans)
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.Draft, cancellationToken);

        if (plan == null)
        {
            throw new InvalidOperationException("No draft plan was found to submit for approval.");
        }

        var errors = await ValidateStagePlansAsync(plan, cancellationToken);
        if (errors.Count > 0)
        {
            throw new PlanApprovalValidationException(errors);
        }

        plan.Status = PlanVersionStatus.PendingApproval;
        plan.SubmittedByUserId = userId;
        plan.SubmittedOn = _clock.UtcNow;
        plan.ApprovedByUserId = null;
        plan.ApprovedOn = null;

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Plan version {PlanVersionId} for project {ProjectId} submitted for approval by {UserId}.", plan.Id, projectId, userId);
    }

    public async Task ApproveAsync(int projectId, string approverUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(approverUserId))
        {
            throw new ArgumentException("A valid approver identifier is required.", nameof(approverUserId));
        }

        var plan = await _db.PlanVersions
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.PendingApproval, cancellationToken);

        if (plan == null)
        {
            throw new InvalidOperationException("No plan is currently pending approval for this project.");
        }

        plan.Status = PlanVersionStatus.Approved;
        plan.ApprovedByUserId = approverUserId;
        plan.ApprovedOn = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Plan version {PlanVersionId} for project {ProjectId} approved by {UserId}.", plan.Id, projectId, approverUserId);
    }

    public async Task RejectAsync(int projectId, string approverUserId, string note, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(approverUserId))
        {
            throw new ArgumentException("A valid approver identifier is required.", nameof(approverUserId));
        }

        var trimmedNote = note?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedNote))
        {
            throw new PlanApprovalValidationException(new[] { "A rejection note is required." });
        }

        var plan = await _db.PlanVersions
            .Include(p => p.ApprovalLogs)
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.Status == PlanVersionStatus.PendingApproval, cancellationToken);

        if (plan == null)
        {
            throw new InvalidOperationException("No plan is currently pending approval for this project.");
        }

        plan.Status = PlanVersionStatus.Draft;
        plan.SubmittedByUserId = null;
        plan.SubmittedOn = null;
        plan.ApprovedByUserId = null;
        plan.ApprovedOn = null;

        plan.ApprovalLogs.Add(new PlanApprovalLog
        {
            PlanVersionId = plan.Id,
            Action = "Rejected",
            Note = trimmedNote,
            PerformedByUserId = approverUserId,
            PerformedOn = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Plan version {PlanVersionId} for project {ProjectId} was rejected by {UserId}.", plan.Id, projectId, approverUserId);
    }

    private async Task<List<string>> ValidateStagePlansAsync(PlanVersion plan, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        var templates = await _db.StageTemplates
            .AsNoTracking()
            .Where(t => t.Version == PlanConstants.StageTemplateVersion)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);

        var templateNames = templates.ToDictionary(t => t.Code, t => t.Name, StringComparer.OrdinalIgnoreCase);

        var dependencies = await _db.StageDependencyTemplates
            .AsNoTracking()
            .Where(d => d.Version == PlanConstants.StageTemplateVersion)
            .ToListAsync(cancellationToken);

        var dependenciesByStage = dependencies
            .GroupBy(d => d.FromStageCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var stagePlans = plan.StagePlans.ToDictionary(s => s.StageCode, StringComparer.OrdinalIgnoreCase);

        foreach (var template in templates)
        {
            if (!stagePlans.TryGetValue(template.Code, out var stage))
            {
                errors.Add($"Stage {template.Name} ({template.Code}) is missing from the plan.");
                continue;
            }

            if (stage.PlannedStart is not DateOnly start || stage.PlannedDue is not DateOnly due)
            {
                errors.Add($"Stage {template.Name} must have both a planned start and due date.");
                continue;
            }

            if (due < start)
            {
                errors.Add($"Stage {template.Name} must end on or after its planned start date.");
            }

            if (!dependenciesByStage.TryGetValue(template.Code, out var stageDependencies))
            {
                continue;
            }

            foreach (var dependency in stageDependencies)
            {
                var dependencyCode = dependency.DependsOnStageCode;
                var dependencyName = templateNames.TryGetValue(dependencyCode, out var friendlyName)
                    ? friendlyName
                    : dependencyCode;

                if (!stagePlans.TryGetValue(dependencyCode, out var prerequisite) || prerequisite.PlannedDue is not DateOnly prerequisiteDue)
                {
                    errors.Add($"Stage {template.Name} requires {dependencyName} to have a planned due date before submission.");
                    continue;
                }

                if (start < prerequisiteDue)
                {
                    errors.Add($"Stage {template.Name} must start on or after {dependencyName}'s planned due date ({prerequisiteDue:dd MMM yyyy}).");
                }
            }
        }

        return errors;
    }
}

public class PlanApprovalValidationException : Exception
{
    public PlanApprovalValidationException(IEnumerable<string> errors)
        : base("The plan is not ready for approval.")
    {
        Errors = errors?.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct().ToArray() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Errors { get; }
}
