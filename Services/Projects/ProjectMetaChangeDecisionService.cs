using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    private readonly IAuditService _audit;

    public ProjectMetaChangeDecisionService(
        ApplicationDbContext db,
        IClock clock,
        ILogger<ProjectMetaChangeDecisionService> logger,
        IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
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

        var driftFields = ProjectMetaChangeDriftDetector.Detect(project, request);

        ProjectMetaChangeRequestPayload? appliedPayload = null;
        Dictionary<string, string?>? beforeValues = null;

        if (input.Action == ProjectMetaDecisionAction.Approve)
        {
            var applyResult = await ApplyApprovedChangeAsync(request, project, cancellationToken);
            if (applyResult.Error is not null)
            {
                return applyResult.Error;
            }

            appliedPayload = applyResult.Payload;
            beforeValues = applyResult.Before;
            request.Payload = JsonSerializer.Serialize(appliedPayload);
            request.DecisionStatus = ProjectMetaDecisionStatuses.Approved;
        }
        else
        {
            request.DecisionStatus = ProjectMetaDecisionStatuses.Rejected;
        }

        request.DecisionNote = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        request.DecidedByUserId = user.UserId;
        request.DecidedOnUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Project meta change request {RequestId} marked as {Status} by {UserId}.",
            input.RequestId,
            request.DecisionStatus,
            user.UserId);

        if (request.DecisionStatus == ProjectMetaDecisionStatuses.Approved && appliedPayload is not null && beforeValues is not null)
        {
            await LogApprovalAsync(request, project, user, appliedPayload, beforeValues, driftFields);
        }
        else if (request.DecisionStatus == ProjectMetaDecisionStatuses.Rejected)
        {
            await _audit.LogAsync(
                "Projects.MetaChangeRejected",
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = project.Id.ToString(),
                    ["RequestId"] = request.Id.ToString(),
                    ["DecidedByUserId"] = request.DecidedByUserId,
                    ["DecisionNote"] = request.DecisionNote,
                    ["RequestedByUserId"] = request.RequestedByUserId
                },
                userId: user.UserId);
        }

        return ProjectMetaDecisionResult.Success();
    }

    private async Task<(ProjectMetaDecisionResult? Error, ProjectMetaChangeRequestPayload Payload, Dictionary<string, string?> Before)> ApplyApprovedChangeAsync(
        ProjectMetaChangeRequest request,
        Project project,
        CancellationToken cancellationToken)
    {
        ProjectMetaChangeRequestPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<ProjectMetaChangeRequestPayload>(request.Payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse project meta change payload for request {RequestId}.", request.Id);
            return (ProjectMetaDecisionResult.ValidationFailed("Unable to read the proposed changes."), default!, new Dictionary<string, string?>());
        }

        if (payload is null)
        {
            _logger.LogError("Project meta change payload for request {RequestId} was null after deserialisation.", request.Id);
            return (ProjectMetaDecisionResult.ValidationFailed("Proposed changes are missing."), default!, new Dictionary<string, string?>());
        }

        var trimmedName = string.IsNullOrWhiteSpace(payload.Name) ? null : payload.Name.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return (ProjectMetaDecisionResult.ValidationFailed("Project name is required."), default!, new Dictionary<string, string?>());
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(payload.Description)
            ? null
            : payload.Description.Trim();

        var trimmedCaseFileNumber = string.IsNullOrWhiteSpace(payload.CaseFileNumber)
            ? null
            : payload.CaseFileNumber.Trim();

        if (!string.IsNullOrEmpty(trimmedCaseFileNumber))
        {
            var duplicate = await _db.Projects
                .AsNoTracking()
                .AnyAsync(
                    p => p.Id != project.Id
                        && p.CaseFileNumber != null
                        && p.CaseFileNumber == trimmedCaseFileNumber,
                    cancellationToken);

            if (duplicate)
            {
                return (ProjectMetaDecisionResult.ValidationFailed(ProjectValidationMessages.DuplicateCaseFileNumber), default!, new Dictionary<string, string?>());
            }
        }

        var categoryId = payload.CategoryId;
        if (categoryId.HasValue)
        {
            var categoryExists = await _db.ProjectCategories
                .AsNoTracking()
                .AnyAsync(c => c.Id == categoryId.Value && c.IsActive, cancellationToken);

            if (!categoryExists)
            {
                return (ProjectMetaDecisionResult.ValidationFailed(ProjectValidationMessages.InactiveCategory), default!, new Dictionary<string, string?>());
            }
        }

        var sponsoringUnitId = payload.SponsoringUnitId;
        string? newSponsoringUnitName = null;
        if (sponsoringUnitId.HasValue)
        {
            var unit = await _db.SponsoringUnits
                .AsNoTracking()
                .Select(u => new { u.Id, u.Name, u.IsActive })
                .SingleOrDefaultAsync(u => u.Id == sponsoringUnitId.Value, cancellationToken);

            if (unit is null || !unit.IsActive)
            {
                return (ProjectMetaDecisionResult.ValidationFailed(ProjectValidationMessages.InactiveSponsoringUnit), default!, new Dictionary<string, string?>());
            }

            newSponsoringUnitName = unit.Name;
        }

        var sponsoringLineDirectorateId = payload.SponsoringLineDirectorateId;
        string? newLineDirectorateName = null;
        if (sponsoringLineDirectorateId.HasValue)
        {
            var line = await _db.LineDirectorates
                .AsNoTracking()
                .Select(l => new { l.Id, l.Name, l.IsActive })
                .SingleOrDefaultAsync(l => l.Id == sponsoringLineDirectorateId.Value, cancellationToken);

            if (line is null || !line.IsActive)
            {
                return (ProjectMetaDecisionResult.ValidationFailed(ProjectValidationMessages.InactiveLineDirectorate), default!, new Dictionary<string, string?>());
            }

            newLineDirectorateName = line.Name;
        }

        string? currentUnitName = null;
        if (project.SponsoringUnitId.HasValue)
        {
            currentUnitName = await _db.SponsoringUnits
                .AsNoTracking()
                .Where(u => u.Id == project.SponsoringUnitId.Value)
                .Select(u => u.Name)
                .SingleOrDefaultAsync(cancellationToken);
        }

        string? currentLineDirectorateName = null;
        if (project.SponsoringLineDirectorateId.HasValue)
        {
            currentLineDirectorateName = await _db.LineDirectorates
                .AsNoTracking()
                .Where(l => l.Id == project.SponsoringLineDirectorateId.Value)
                .Select(l => l.Name)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var before = new Dictionary<string, string?>
        {
            ["NameBefore"] = project.Name,
            ["DescriptionBefore"] = project.Description,
            ["CaseFileNumberBefore"] = project.CaseFileNumber,
            ["CategoryIdBefore"] = project.CategoryId?.ToString(),
            ["SponsoringUnitIdBefore"] = project.SponsoringUnitId?.ToString(),
            ["SponsoringUnitNameBefore"] = currentUnitName,
            ["SponsoringLineDirectorateIdBefore"] = project.SponsoringLineDirectorateId?.ToString(),
            ["SponsoringLineDirectorateNameBefore"] = currentLineDirectorateName
        };

        project.Name = trimmedName!;
        project.Description = trimmedDescription;
        project.CaseFileNumber = trimmedCaseFileNumber;
        project.CategoryId = categoryId;
        project.SponsoringUnitId = sponsoringUnitId;
        project.SponsoringLineDirectorateId = sponsoringLineDirectorateId;

        var cleanedPayload = new ProjectMetaChangeRequestPayload
        {
            Name = trimmedName!,
            Description = trimmedDescription,
            CaseFileNumber = trimmedCaseFileNumber,
            CategoryId = categoryId,
            SponsoringUnitId = sponsoringUnitId,
            SponsoringLineDirectorateId = sponsoringLineDirectorateId
        };

        if (!string.IsNullOrEmpty(newSponsoringUnitName))
        {
            before["SponsoringUnitNameAfter"] = newSponsoringUnitName;
        }

        if (!string.IsNullOrEmpty(newLineDirectorateName))
        {
            before["SponsoringLineDirectorateNameAfter"] = newLineDirectorateName;
        }

        return (null, cleanedPayload, before);
    }

    private async Task LogApprovalAsync(
        ProjectMetaChangeRequest request,
        Project project,
        ProjectMetaDecisionUser user,
        ProjectMetaChangeRequestPayload payload,
        IReadOnlyDictionary<string, string?> before,
        IReadOnlyCollection<string> driftFields)
    {
        var driftDetected = driftFields.Count > 0;

        var header = new Dictionary<string, string?>
        {
            ["ProjectId"] = project.Id.ToString(),
            ["RequestId"] = request.Id.ToString(),
            ["DecidedByUserId"] = request.DecidedByUserId,
            ["DecisionNote"] = request.DecisionNote,
            ["RequestedByUserId"] = request.RequestedByUserId,
            ["RequestNote"] = request.RequestNote,
            ["DriftDetected"] = driftDetected ? "true" : "false",
            ["DriftFields"] = driftDetected ? string.Join(',', driftFields) : null
        };

        await _audit.LogAsync(
            "Projects.MetaChangeApproved",
            data: header,
            userId: user.UserId);

        var afterUnitName = before.TryGetValue("SponsoringUnitNameAfter", out var suAfter)
            ? suAfter
            : null;
        if (afterUnitName is null && payload.SponsoringUnitId.HasValue)
        {
            afterUnitName = await _db.SponsoringUnits
                .AsNoTracking()
                .Where(u => u.Id == payload.SponsoringUnitId.Value)
                .Select(u => u.Name)
                .SingleOrDefaultAsync();
        }

        var afterLineName = before.TryGetValue("SponsoringLineDirectorateNameAfter", out var slAfter)
            ? slAfter
            : null;
        if (afterLineName is null && payload.SponsoringLineDirectorateId.HasValue)
        {
            afterLineName = await _db.LineDirectorates
                .AsNoTracking()
                .Where(l => l.Id == payload.SponsoringLineDirectorateId.Value)
                .Select(l => l.Name)
                .SingleOrDefaultAsync();
        }

        var diff = new Dictionary<string, string?>
        {
            ["ProjectId"] = project.Id.ToString(),
            ["RequestId"] = request.Id.ToString(),
            ["NameBefore"] = before.TryGetValue("NameBefore", out var nb) ? nb : null,
            ["NameAfter"] = payload.Name,
            ["DescriptionBefore"] = before.TryGetValue("DescriptionBefore", out var db) ? db : null,
            ["DescriptionAfter"] = payload.Description,
            ["CaseFileNumberBefore"] = before.TryGetValue("CaseFileNumberBefore", out var cb) ? cb : null,
            ["CaseFileNumberAfter"] = payload.CaseFileNumber,
            ["CategoryIdBefore"] = before.TryGetValue("CategoryIdBefore", out var cab) ? cab : null,
            ["CategoryIdAfter"] = payload.CategoryId?.ToString(),
            ["SponsoringUnitIdBefore"] = before.TryGetValue("SponsoringUnitIdBefore", out var sub) ? sub : null,
            ["SponsoringUnitIdAfter"] = payload.SponsoringUnitId?.ToString(),
            ["SponsoringUnitNameBefore"] = before.TryGetValue("SponsoringUnitNameBefore", out var sunb) ? sunb : null,
            ["SponsoringUnitNameAfter"] = afterUnitName,
            ["SponsoringLineDirectorateIdBefore"] = before.TryGetValue("SponsoringLineDirectorateIdBefore", out var slib) ? slib : null,
            ["SponsoringLineDirectorateIdAfter"] = payload.SponsoringLineDirectorateId?.ToString(),
            ["SponsoringLineDirectorateNameBefore"] = before.TryGetValue("SponsoringLineDirectorateNameBefore", out var slnb) ? slnb : null,
            ["SponsoringLineDirectorateNameAfter"] = afterLineName
        };

        await _audit.LogAsync(
            "Projects.MetaChangedViaApproval",
            data: diff,
            userId: user.UserId);
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
    AlreadyDecided,
    ValidationFailed
}

public sealed record ProjectMetaDecisionResult(ProjectMetaDecisionOutcome Outcome, string? Error = null)
{
    public static ProjectMetaDecisionResult Success() => new(ProjectMetaDecisionOutcome.Success);

    public static ProjectMetaDecisionResult Forbidden() => new(ProjectMetaDecisionOutcome.Forbidden);

    public static ProjectMetaDecisionResult RequestNotFound() => new(ProjectMetaDecisionOutcome.RequestNotFound);

    public static ProjectMetaDecisionResult AlreadyDecided() => new(ProjectMetaDecisionOutcome.AlreadyDecided);

    public static ProjectMetaDecisionResult ValidationFailed(string message)
        => new(ProjectMetaDecisionOutcome.ValidationFailed, message);
}
