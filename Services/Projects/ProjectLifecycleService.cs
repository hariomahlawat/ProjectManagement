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

    /// <summary>
    /// Marks an active project as completed or improves the completion information
    /// already recorded for a completed project.
    /// </summary>
    public async Task<ProjectLifecycleOperationResult> UpdateCompletionAsync(
        int projectId,
        string actorUserId,
        ProjectCompletionValue completion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("A valid user is required to update the lifecycle.", nameof(actorUserId));
        }

        ArgumentNullException.ThrowIfNull(completion);

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return ProjectLifecycleOperationResult.NotFound();
        }

        if (project.LifecycleStatus != ProjectLifecycleStatus.Active &&
            project.LifecycleStatus != ProjectLifecycleStatus.Completed)
        {
            return ProjectLifecycleOperationResult.InvalidStatus(
                "Only active or completed projects can have completion details recorded.");
        }

        var todayLocal = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, TimeZoneHelper.GetIst()));

        var validation = ValidateCompletion(completion, todayLocal);
        if (validation is not null)
        {
            return ProjectLifecycleOperationResult.ValidationFailed(validation);
        }

        var previousStatus = project.LifecycleStatus;
        ApplyCompletion(project, completion);
        project.LifecycleStatus = ProjectLifecycleStatus.Completed;
        project.CancelledOn = null;
        project.CancelReason = null;

        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectLifecycleCompletionUpdated(
                project.Id,
                actorUserId,
                previousStatus,
                completion.Precision,
                project.CompletedOn,
                project.CompletedYear,
                project.CompletedMonth)
            .WriteAsync(_audit);

        return ProjectLifecycleOperationResult.Success();
    }

    // Compatibility wrappers retained for callers outside the Project Overview workflow.
    public async Task<ProjectLifecycleOperationResult> MarkCompletedAsync(
        int projectId,
        string actorUserId,
        int? provisionalYear,
        CancellationToken cancellationToken = default)
    {
        var state = await _db.Projects
            .AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => new { project.LifecycleStatus, project.CompletedOn })
            .FirstOrDefaultAsync(cancellationToken);

        if (state is null)
        {
            return ProjectLifecycleOperationResult.NotFound();
        }

        var canUpdateExistingCompletion =
            state.LifecycleStatus == ProjectLifecycleStatus.Completed && state.CompletedOn is null;
        if (state.LifecycleStatus != ProjectLifecycleStatus.Active && !canUpdateExistingCompletion)
        {
            return ProjectLifecycleOperationResult.InvalidStatus(
                "Project must be active or awaiting endorsement to update completion details.");
        }

        return await UpdateCompletionAsync(
            projectId,
            actorUserId,
            provisionalYear.HasValue
                ? ProjectCompletionValue.YearOnly(provisionalYear.Value)
                : ProjectCompletionValue.NotKnown(),
            cancellationToken);
    }

    public async Task<ProjectLifecycleOperationResult> EndorseCompletionAsync(
        int projectId,
        string actorUserId,
        DateOnly completionDate,
        CancellationToken cancellationToken = default)
    {
        var state = await _db.Projects
            .AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => new { project.LifecycleStatus, project.CompletedYear })
            .FirstOrDefaultAsync(cancellationToken);

        if (state is null)
        {
            return ProjectLifecycleOperationResult.NotFound();
        }

        if (state.LifecycleStatus != ProjectLifecycleStatus.Completed)
        {
            return ProjectLifecycleOperationResult.InvalidStatus(
                "Only completed projects can be endorsed with a final date.");
        }

        if (!state.CompletedYear.HasValue)
        {
            return ProjectLifecycleOperationResult.InvalidStatus(
                "Set a completion year before endorsing an exact date.");
        }

        return await UpdateCompletionAsync(
            projectId,
            actorUserId,
            ProjectCompletionValue.Exact(completionDate),
            cancellationToken);
    }

    private static string? ValidateCompletion(ProjectCompletionValue completion, DateOnly todayLocal)
    {
        switch (completion.Precision)
        {
            case ProjectCompletionPrecision.ExactDate:
                if (!completion.ExactDate.HasValue)
                {
                    return "Completion date is required when exact date is selected.";
                }

                if (completion.ExactDate.Value.Year < 1900)
                {
                    return "Completion date must be on or after 01 Jan 1900.";
                }

                if (completion.ExactDate.Value > todayLocal)
                {
                    return "Completion date cannot be in the future.";
                }

                return null;

            case ProjectCompletionPrecision.MonthAndYear:
                if (!completion.Year.HasValue || !completion.Month.HasValue)
                {
                    return "Completion month and year are required when month and year is selected.";
                }

                if (completion.Year.Value < 1900 || completion.Year.Value > todayLocal.Year)
                {
                    return $"Completion year must be between 1900 and {todayLocal.Year}.";
                }

                if (completion.Month.Value is < 1 or > 12)
                {
                    return "Completion month must be between January and December.";
                }

                if (completion.Year.Value == todayLocal.Year && completion.Month.Value > todayLocal.Month)
                {
                    return "Completion month cannot be in the future.";
                }

                return null;

            case ProjectCompletionPrecision.YearOnly:
                if (!completion.Year.HasValue)
                {
                    return "Completion year is required when year only is selected.";
                }

                if (completion.Year.Value < 1900 || completion.Year.Value > todayLocal.Year)
                {
                    return $"Completion year must be between 1900 and {todayLocal.Year}.";
                }

                return null;

            case ProjectCompletionPrecision.NotKnown:
                return null;

            default:
                return "Select the completion information available.";
        }
    }

    private static void ApplyCompletion(Project project, ProjectCompletionValue completion)
    {
        project.CompletedOn = null;
        project.CompletedYear = null;
        project.CompletedMonth = null;

        switch (completion.Precision)
        {
            case ProjectCompletionPrecision.ExactDate:
                project.CompletedOn = completion.ExactDate;
                project.CompletedYear = completion.ExactDate!.Value.Year;
                break;

            case ProjectCompletionPrecision.MonthAndYear:
                project.CompletedYear = completion.Year;
                project.CompletedMonth = checked((short)completion.Month!.Value);
                break;

            case ProjectCompletionPrecision.YearOnly:
                project.CompletedYear = completion.Year;
                break;

            case ProjectCompletionPrecision.NotKnown:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(completion), "Unsupported completion precision.");
        }
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
        project.CompletedMonth = null;

        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectLifecycleCancelled(
                project.Id,
                actorUserId,
                cancelledOn,
                trimmedReason)
            .WriteAsync(_audit);

        return ProjectLifecycleOperationResult.Success();
    }

    public async Task<ProjectLifecycleOperationResult> ReactivateAsync(
        int projectId,
        string actorUserId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        // SECTION: Reactivate lifecycle
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("A valid user is required to update the lifecycle.", nameof(actorUserId));
        }

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return ProjectLifecycleOperationResult.NotFound();
        }

        if (project.LifecycleStatus != ProjectLifecycleStatus.Completed &&
            project.LifecycleStatus != ProjectLifecycleStatus.Cancelled)
        {
            return ProjectLifecycleOperationResult.InvalidStatus("Only completed or cancelled projects can be reactivated.");
        }

        var trimmedReason = reason?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedReason) && trimmedReason.Length > 512)
        {
            return ProjectLifecycleOperationResult.ValidationFailed("Reactivation reason must be 512 characters or fewer.");
        }

        var previousStatus = project.LifecycleStatus;
        project.LifecycleStatus = ProjectLifecycleStatus.Active;
        project.CompletedOn = null;
        project.CompletedYear = null;
        project.CompletedMonth = null;
        project.CancelledOn = null;
        project.CancelReason = null;

        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events.ProjectLifecycleReactivated(
                project.Id,
                actorUserId,
                previousStatus,
                trimmedReason)
            .WriteAsync(_audit);

        return ProjectLifecycleOperationResult.Success();
    }
}
