using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectLifecycleService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public ProjectLifecycleService(ApplicationDbContext db, IAuditService audit, IClock clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ProjectLifecycleOperationResult> MarkCompletedAsync(
        int projectId,
        string actorUserId,
        int? provisionalYear,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("A valid user is required to update the lifecycle.", nameof(actorUserId));
        }

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return ProjectLifecycleOperationResult.NotFound();
        }

        var canUpdateExistingCompletion = project.LifecycleStatus == ProjectLifecycleStatus.Completed && project.CompletedOn is null;
        var isActive = project.LifecycleStatus == ProjectLifecycleStatus.Active;

        if (!isActive && !canUpdateExistingCompletion)
        {
            return ProjectLifecycleOperationResult.InvalidStatus("Project must be active or awaiting endorsement to update completion details.");
        }

        if (provisionalYear.HasValue)
        {
            var year = provisionalYear.Value;
            var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, TimeZoneHelper.GetIst());
            var maxYear = todayLocal.Year;
            if (year < 1900 || year > maxYear)
            {
                return ProjectLifecycleOperationResult.ValidationFailed($"Completion year must be between 1900 and {maxYear}.");
            }
        }

        project.LifecycleStatus = ProjectLifecycleStatus.Completed;
        project.CompletedYear = provisionalYear;
        if (isActive)
        {
            project.CompletedOn = null;
        }
        project.CancelledOn = null;
        project.CancelReason = null;

        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectLifecycleMarkedCompleted(
                project.Id,
                actorUserId,
                provisionalYear)
            .WriteAsync(_audit);

        return ProjectLifecycleOperationResult.Success();
    }

    public async Task<ProjectLifecycleOperationResult> EndorseCompletionAsync(
        int projectId,
        string actorUserId,
        DateOnly completionDate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("A valid user is required to update the lifecycle.", nameof(actorUserId));
        }

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return ProjectLifecycleOperationResult.NotFound();
        }

        if (project.LifecycleStatus != ProjectLifecycleStatus.Completed)
        {
            return ProjectLifecycleOperationResult.InvalidStatus("Only completed projects can be endorsed with a final date.");
        }

        if (!project.CompletedYear.HasValue)
        {
            return ProjectLifecycleOperationResult.InvalidStatus("Set a completion year before endorsing an exact date.");
        }

        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, TimeZoneHelper.GetIst()));
        if (completionDate > todayLocal)
        {
            return ProjectLifecycleOperationResult.ValidationFailed("Completion date cannot be in the future.");
        }

        project.CompletedOn = completionDate;
        project.CompletedYear = completionDate.Year;
        project.CancelledOn = null;
        project.CancelReason = null;

        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectLifecycleCompletionEndorsed(
                project.Id,
                actorUserId,
                completionDate,
                project.CompletedYear)
            .WriteAsync(_audit);

        return ProjectLifecycleOperationResult.Success();
    }

    public async Task<ProjectLifecycleOperationResult> CancelProjectAsync(
        int projectId,
        string actorUserId,
        DateOnly cancelledOn,
        string cancelReason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("A valid user is required to update the lifecycle.", nameof(actorUserId));
        }

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return ProjectLifecycleOperationResult.NotFound();
        }

        if (project.LifecycleStatus == ProjectLifecycleStatus.Cancelled)
        {
            return ProjectLifecycleOperationResult.InvalidStatus("Project is already cancelled.");
        }

        if (project.LifecycleStatus == ProjectLifecycleStatus.Completed)
        {
            return ProjectLifecycleOperationResult.InvalidStatus("Completed projects cannot be cancelled.");
        }

        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, TimeZoneHelper.GetIst()));
        if (cancelledOn > todayLocal)
        {
            return ProjectLifecycleOperationResult.ValidationFailed("Cancellation date cannot be in the future.");
        }

        var trimmedReason = cancelReason?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedReason))
        {
            return ProjectLifecycleOperationResult.ValidationFailed("Cancellation reason is required.");
        }

        if (trimmedReason.Length > 512)
        {
            return ProjectLifecycleOperationResult.ValidationFailed("Cancellation reason must be 512 characters or fewer.");
        }

        project.LifecycleStatus = ProjectLifecycleStatus.Cancelled;
        project.CancelledOn = cancelledOn;
        project.CancelReason = trimmedReason;
        project.CompletedOn = null;
        project.CompletedYear = null;

        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectLifecycleCancelled(
                project.Id,
                actorUserId,
                cancelledOn,
                trimmedReason)
            .WriteAsync(_audit);

        return ProjectLifecycleOperationResult.Success();
    }
}
