using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectTotUpdateService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IClock _clock;

    public ProjectTotUpdateService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProjectTotProgressUpdateListResult> GetUpdatesAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var projectExists = await _db.Projects
            .AsNoTracking()
            .AnyAsync(p => p.Id == projectId, cancellationToken);

        if (!projectExists)
        {
            return ProjectTotProgressUpdateListResult.NotFound();
        }

        var updates = await _db.ProjectTotProgressUpdates
            .AsNoTracking()
            .Include(u => u.SubmittedByUser)
            .Include(u => u.DecidedByUser)
            .Where(u => u.ProjectId == projectId)
            .OrderByDescending(u => u.SubmittedOnUtc)
            .ToListAsync(cancellationToken);

        var mapped = updates
            .Select(update => new ProjectTotProgressUpdateView(
                update.Id,
                update.Body,
                update.EventDate,
                update.State,
                update.SubmittedByRole,
                update.SubmittedByUserId,
                DisplayName(update.SubmittedByUser, update.SubmittedByUserId),
                update.SubmittedOnUtc,
                update.DecidedByRole,
                update.DecidedByUserId,
                DisplayName(update.DecidedByUser, update.DecidedByUserId),
                update.DecidedOnUtc,
                update.DecisionRemarks,
                update.PublishedOnUtc,
                update.RowVersion))
            .ToList();

        return ProjectTotProgressUpdateListResult.Success(mapped);
    }

    public async Task<ProjectTotProgressUpdateActionResult> SubmitAsync(
        int projectId,
        string? body,
        DateOnly? eventDate,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (principal is null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        var user = await _users.GetUserAsync(principal);
        if (user is null)
        {
            return ProjectTotProgressUpdateActionResult.Forbidden("User context is required to submit an update.");
        }

        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null)
        {
            return ProjectTotProgressUpdateActionResult.NotFound();
        }

        var actor = await ResolveActorAsync(project, user, cancellationToken);
        if (!actor.IsAuthorized)
        {
            return ProjectTotProgressUpdateActionResult.Forbidden("You are not allowed to submit updates for this project.");
        }

        var trimmedBody = string.IsNullOrWhiteSpace(body) ? null : body.Trim();
        if (string.IsNullOrEmpty(trimmedBody))
        {
            return ProjectTotProgressUpdateActionResult.ValidationFailed("Update details are required.");
        }

        if (trimmedBody.Length > 2000)
        {
            return ProjectTotProgressUpdateActionResult.ValidationFailed("Updates must be 2000 characters or fewer.");
        }

        var now = _clock.UtcNow.UtcDateTime;
        var update = new ProjectTotProgressUpdate
        {
            ProjectId = project.Id,
            Body = trimmedBody,
            EventDate = eventDate,
            SubmittedByUserId = user.Id,
            SubmittedByRole = actor.Role!.Value,
            SubmittedOnUtc = now,
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        if (actor.AutoApprove)
        {
            update.State = ProjectTotProgressUpdateState.Approved;
            update.DecidedByUserId = user.Id;
            update.DecidedByRole = actor.Role;
            update.DecidedOnUtc = now;
            update.PublishedOnUtc = now;
        }
        else
        {
            update.State = ProjectTotProgressUpdateState.Pending;
        }

        _db.ProjectTotProgressUpdates.Add(update);
        await _db.SaveChangesAsync(cancellationToken);

        return ProjectTotProgressUpdateActionResult.Success();
    }

    public async Task<ProjectTotProgressUpdateActionResult> DecideAsync(
        int projectId,
        int updateId,
        bool approve,
        string? decisionRemarks,
        byte[]? expectedRowVersion,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (principal is null)
        {
            throw new ArgumentNullException(nameof(principal));
        }

        var user = await _users.GetUserAsync(principal);
        if (user is null)
        {
            return ProjectTotProgressUpdateActionResult.Forbidden("User context is required to complete the decision.");
        }

        var update = await _db.ProjectTotProgressUpdates
            .Include(u => u.Project)
            .FirstOrDefaultAsync(u => u.ProjectId == projectId && u.Id == updateId, cancellationToken);

        if (update is null || update.Project is null)
        {
            return ProjectTotProgressUpdateActionResult.NotFound();
        }

        var actor = await ResolveActorAsync(update.Project, user, cancellationToken);
        if (!actor.CanDecide)
        {
            return ProjectTotProgressUpdateActionResult.Forbidden("You are not allowed to decide on this update.");
        }

        if (update.State != ProjectTotProgressUpdateState.Pending)
        {
            return ProjectTotProgressUpdateActionResult.Conflict("The update has already been decided.");
        }

        if (expectedRowVersion is not null && !update.RowVersion.SequenceEqual(expectedRowVersion))
        {
            return ProjectTotProgressUpdateActionResult.Conflict("The update was modified by another user. Refresh and try again.");
        }

        var trimmedRemarks = string.IsNullOrWhiteSpace(decisionRemarks) ? null : decisionRemarks.Trim();
        if (trimmedRemarks is { Length: > 2000 })
        {
            return ProjectTotProgressUpdateActionResult.ValidationFailed("Decision remarks must be 2000 characters or fewer.");
        }

        var now = _clock.UtcNow.UtcDateTime;
        update.State = approve ? ProjectTotProgressUpdateState.Approved : ProjectTotProgressUpdateState.Rejected;
        update.DecidedByUserId = user.Id;
        update.DecidedByRole = actor.Role;
        update.DecidedOnUtc = now;
        update.DecisionRemarks = trimmedRemarks;
        update.PublishedOnUtc = approve ? now : null;
        update.DecidedByUser = null;
        update.SubmittedByUser = null!;
        update.RowVersion = Guid.NewGuid().ToByteArray();

        await _db.SaveChangesAsync(cancellationToken);

        return ProjectTotProgressUpdateActionResult.Success();
    }

    private async Task<ActorResolutionResult> ResolveActorAsync(
        Project project,
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var roles = await _users.GetRolesAsync(user);
        var roleSet = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (roleSet.Contains("Admin"))
        {
            return ActorResolutionResult.Administrator();
        }

        if (roleSet.Contains("HoD") || string.Equals(project.HodUserId, user.Id, StringComparison.Ordinal))
        {
            return ActorResolutionResult.HeadOfDepartment();
        }

        if (roleSet.Contains("ProjectOffice") || roleSet.Contains("Project Office"))
        {
            return ActorResolutionResult.ProjectOffice();
        }

        var isPoRole = roleSet.Contains("Project Officer") || roleSet.Contains("ProjectOfficer");
        var isAssignedPo = !string.IsNullOrEmpty(project.LeadPoUserId)
            && string.Equals(project.LeadPoUserId, user.Id, StringComparison.Ordinal);

        if (isAssignedPo && isPoRole)
        {
            return ActorResolutionResult.ProjectOfficer();
        }

        if (!isPoRole && isAssignedPo)
        {
            return ActorResolutionResult.ProjectOfficer();
        }

        return ActorResolutionResult.NotAuthorized();
    }

    private static string DisplayName(ApplicationUser? user, string? fallback)
    {
        if (user is null)
        {
            return FallbackName(fallback);
        }

        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            return user.FullName;
        }

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            return user.UserName!;
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email!;
        }

        return FallbackName(fallback);
    }

    private static string FallbackName(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Unknown user" : value;

    private sealed record ActorResolutionResult(bool IsAuthorized, ProjectTotUpdateActorRole? Role, bool AutoApprove)
    {
        public bool CanDecide => IsAuthorized && Role is ProjectTotUpdateActorRole.Administrator or ProjectTotUpdateActorRole.HeadOfDepartment;

        public static ActorResolutionResult Administrator() => new(true, ProjectTotUpdateActorRole.Administrator, true);
        public static ActorResolutionResult HeadOfDepartment() => new(true, ProjectTotUpdateActorRole.HeadOfDepartment, true);
        public static ActorResolutionResult ProjectOffice() => new(true, ProjectTotUpdateActorRole.ProjectOffice, false);
        public static ActorResolutionResult ProjectOfficer() => new(true, ProjectTotUpdateActorRole.ProjectOfficer, false);
        public static ActorResolutionResult NotAuthorized() => new(false, null, false);
    }
}

public enum ProjectTotProgressUpdateActionStatus
{
    Success = 0,
    NotFound = 1,
    Forbidden = 2,
    ValidationFailed = 3,
    Conflict = 4
}

public sealed record ProjectTotProgressUpdateActionResult(
    ProjectTotProgressUpdateActionStatus Status,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == ProjectTotProgressUpdateActionStatus.Success;

    public static ProjectTotProgressUpdateActionResult Success() => new(ProjectTotProgressUpdateActionStatus.Success);

    public static ProjectTotProgressUpdateActionResult NotFound() => new(ProjectTotProgressUpdateActionStatus.NotFound);

    public static ProjectTotProgressUpdateActionResult Forbidden(string message) =>
        new(ProjectTotProgressUpdateActionStatus.Forbidden, message);

    public static ProjectTotProgressUpdateActionResult ValidationFailed(string message) =>
        new(ProjectTotProgressUpdateActionStatus.ValidationFailed, message);

    public static ProjectTotProgressUpdateActionResult Conflict(string message) =>
        new(ProjectTotProgressUpdateActionStatus.Conflict, message);
}

public sealed record ProjectTotProgressUpdateListResult(
    ProjectTotProgressUpdateActionStatus Status,
    IReadOnlyList<ProjectTotProgressUpdateView> Updates,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == ProjectTotProgressUpdateActionStatus.Success;

    public static ProjectTotProgressUpdateListResult Success(IReadOnlyList<ProjectTotProgressUpdateView> updates) =>
        new(ProjectTotProgressUpdateActionStatus.Success, updates);

    public static ProjectTotProgressUpdateListResult NotFound() =>
        new(ProjectTotProgressUpdateActionStatus.NotFound, Array.Empty<ProjectTotProgressUpdateView>());
}

public sealed record ProjectTotProgressUpdateView(
    int Id,
    string Body,
    DateOnly? EventDate,
    ProjectTotProgressUpdateState State,
    ProjectTotUpdateActorRole SubmittedByRole,
    string SubmittedByUserId,
    string SubmittedByName,
    DateTime SubmittedOnUtc,
    ProjectTotUpdateActorRole? DecidedByRole,
    string? DecidedByUserId,
    string? DecidedByName,
    DateTime? DecidedOnUtc,
    string? DecisionRemarks,
    DateTime? PublishedOnUtc,
    byte[] RowVersion);
