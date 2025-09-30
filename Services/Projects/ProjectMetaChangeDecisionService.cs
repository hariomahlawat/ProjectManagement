using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectMetaChangeDecisionService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<ProjectMetaChangeDecisionService> _logger;

    public ProjectMetaChangeDecisionService(
        ApplicationDbContext db,
        IClock clock,
        ILogger<ProjectMetaChangeDecisionService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProjectMetaDecisionResult> DecideAsync(
        ProjectMetaDecisionInput input,
        ProjectMetaDecisionUser user,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (string.IsNullOrWhiteSpace(user.UserId))
        {
            return ProjectMetaDecisionResult.Forbidden();
        }

        var request = await _db.ProjectMetaChangeRequests
            .SingleOrDefaultAsync(r => r.Id == input.RequestId, cancellationToken);

        if (request is null)
        {
            _logger.LogWarning(
                "Project meta change request {RequestId} was not found.",
                input.RequestId);
            return ProjectMetaDecisionResult.RequestNotFound();
        }

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == request.ProjectId, cancellationToken);

        if (project is null)
        {
            _logger.LogWarning(
                "Project meta change request {RequestId} references missing project {ProjectId}.",
                input.RequestId,
                request.ProjectId);
            return ProjectMetaDecisionResult.RequestNotFound();
        }

        if (user.IsHoD && !user.IsAdmin &&
            !string.Equals(project.HodUserId, user.UserId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "User {UserId} attempted to decide meta change request {RequestId} for project {ProjectId} but is not the assigned HoD.",
                user.UserId,
                input.RequestId,
                request.ProjectId);
            return ProjectMetaDecisionResult.Forbidden();
        }

        if (!string.Equals(request.DecisionStatus, ProjectMetaDecisionStatuses.Pending, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Project meta change request {RequestId} has already been decided with status {Status}.",
                input.RequestId,
                request.DecisionStatus);
            return ProjectMetaDecisionResult.AlreadyDecided();
        }

        request.DecisionStatus = input.Action == ProjectMetaDecisionAction.Approve
            ? ProjectMetaDecisionStatuses.Approved
            : ProjectMetaDecisionStatuses.Rejected;
        request.DecisionNote = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        request.DecidedByUserId = user.UserId;
        request.DecidedOnUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Project meta change request {RequestId} marked as {Status} by {UserId}.",
            input.RequestId,
            request.DecisionStatus,
            user.UserId);

        return ProjectMetaDecisionResult.Success();
    }
}

public readonly record struct ProjectMetaDecisionUser(string UserId, bool IsAdmin, bool IsHoD);

public sealed record ProjectMetaDecisionInput(int RequestId, ProjectMetaDecisionAction Action, string? Note);

public enum ProjectMetaDecisionAction
{
    Approve,
    Reject
}

public static class ProjectMetaDecisionStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public enum ProjectMetaDecisionOutcome
{
    Success,
    Forbidden,
    RequestNotFound,
    AlreadyDecided
}

public sealed record ProjectMetaDecisionResult(ProjectMetaDecisionOutcome Outcome, string? Error = null)
{
    public static ProjectMetaDecisionResult Success() => new(ProjectMetaDecisionOutcome.Success);

    public static ProjectMetaDecisionResult Forbidden() => new(ProjectMetaDecisionOutcome.Forbidden);

    public static ProjectMetaDecisionResult RequestNotFound() => new(ProjectMetaDecisionOutcome.RequestNotFound);

    public static ProjectMetaDecisionResult AlreadyDecided() => new(ProjectMetaDecisionOutcome.AlreadyDecided);
}
