using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Projects;

public class ProjectMetaChangeRequestService
{
    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public ProjectMetaChangeRequestService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProjectMetaChangeRequestResult> SubmitAsync(
        ProjectMetaChangeRequestInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.RequestedByUserId))
        {
            throw new ArgumentException("A valid requester is required.", nameof(input));
        }

        var project = await _db.Projects
            .SingleOrDefaultAsync(p => p.Id == input.ProjectId, cancellationToken);

        if (project is null)
        {
            return ProjectMetaChangeRequestResult.ProjectNotFound();
        }

        var (caseFileNumber, errors) = await ValidateCaseFileNumberAsync(
            input.ProjectId,
            input.ProposedCaseFileNumber,
            cancellationToken);

        if (errors.Count > 0)
        {
            return ProjectMetaChangeRequestResult.ValidationFailed(errors);
        }

        var request = new ProjectMetaChangeRequest
        {
            ProjectId = project.Id,
            ProposedName = string.IsNullOrWhiteSpace(input.ProposedName)
                ? null
                : input.ProposedName.Trim(),
            ProposedDescription = string.IsNullOrWhiteSpace(input.ProposedDescription)
                ? null
                : input.ProposedDescription.Trim(),
            ProposedCaseFileNumber = caseFileNumber,
            RequestedByUserId = input.RequestedByUserId,
            RequestedOnUtc = _clock.UtcNow.UtcDateTime,
            DecisionStatus = "Pending"
        };

        await _db.ProjectMetaChangeRequests.AddAsync(request, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return ProjectMetaChangeRequestResult.Success(request.Id);
    }

    public async Task<ProjectMetaDirectEditResult> DirectEditAsync(
        ProjectMetaDirectEditInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var project = await _db.Projects
            .SingleOrDefaultAsync(p => p.Id == input.ProjectId, cancellationToken);

        if (project is null)
        {
            return ProjectMetaDirectEditResult.ProjectNotFound();
        }

        var (caseFileNumber, errors) = await ValidateCaseFileNumberAsync(
            project.Id,
            input.CaseFileNumber,
            cancellationToken);

        if (errors.Count > 0)
        {
            return ProjectMetaDirectEditResult.ValidationFailed(errors);
        }

        project.CaseFileNumber = caseFileNumber;

        if (!string.IsNullOrWhiteSpace(input.Name))
        {
            project.Name = input.Name.Trim();
        }

        if (input.Description is not null)
        {
            project.Description = string.IsNullOrWhiteSpace(input.Description)
                ? null
                : input.Description.Trim();
        }

        await _db.SaveChangesAsync(cancellationToken);

        return ProjectMetaDirectEditResult.Success();
    }

    private async Task<(string? TrimmedCaseFileNumber, IReadOnlyDictionary<string, string[]> Errors)> ValidateCaseFileNumberAsync(
        int projectId,
        string? proposedCaseFileNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(proposedCaseFileNumber))
        {
            return (null, EmptyErrors);
        }

        var trimmed = proposedCaseFileNumber.Trim();
        if (trimmed.Length == 0)
        {
            return (null, EmptyErrors);
        }

        var exists = await _db.Projects
            .AnyAsync(
                p => p.CaseFileNumber == trimmed && p.Id != projectId,
                cancellationToken);

        if (exists)
        {
            return (trimmed, new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["CaseFileNumber"] = new[] { "Case file number already exists." }
            });
        }

        return (trimmed, EmptyErrors);
    }
}

public sealed record ProjectMetaChangeRequestInput
{
    public int ProjectId { get; init; }
    public string? ProposedName { get; init; }
    public string? ProposedDescription { get; init; }
    public string? ProposedCaseFileNumber { get; init; }
    public string RequestedByUserId { get; init; } = string.Empty;
}

public sealed record ProjectMetaDirectEditInput
{
    public int ProjectId { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? CaseFileNumber { get; init; }
}

public sealed record ProjectMetaChangeRequestResult
{
    public ProjectMetaChangeRequestOutcome Outcome { get; init; }
    public int? RequestId { get; init; }
    public IReadOnlyDictionary<string, string[]> Errors { get; init; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    private ProjectMetaChangeRequestResult(
        ProjectMetaChangeRequestOutcome outcome,
        int? requestId,
        IReadOnlyDictionary<string, string[]> errors)
    {
        Outcome = outcome;
        RequestId = requestId;
        Errors = errors;
    }

    public static ProjectMetaChangeRequestResult Success(int requestId) =>
        new(ProjectMetaChangeRequestOutcome.Success, requestId, EmptyErrors);

    public static ProjectMetaChangeRequestResult ValidationFailed(IReadOnlyDictionary<string, string[]> errors) =>
        new(ProjectMetaChangeRequestOutcome.ValidationFailed, null, errors);

    public static ProjectMetaChangeRequestResult ProjectNotFound() =>
        new(ProjectMetaChangeRequestOutcome.ProjectNotFound, null, EmptyErrors);
}

public enum ProjectMetaChangeRequestOutcome
{
    Success,
    ValidationFailed,
    ProjectNotFound
}

public sealed record ProjectMetaDirectEditResult
{
    public ProjectMetaDirectEditOutcome Outcome { get; init; }
    public IReadOnlyDictionary<string, string[]> Errors { get; init; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    private ProjectMetaDirectEditResult(
        ProjectMetaDirectEditOutcome outcome,
        IReadOnlyDictionary<string, string[]> errors)
    {
        Outcome = outcome;
        Errors = errors;
    }

    public static ProjectMetaDirectEditResult Success() =>
        new(ProjectMetaDirectEditOutcome.Success, EmptyErrors);

    public static ProjectMetaDirectEditResult ValidationFailed(IReadOnlyDictionary<string, string[]> errors) =>
        new(ProjectMetaDirectEditOutcome.ValidationFailed, errors);

    public static ProjectMetaDirectEditResult ProjectNotFound() =>
        new(ProjectMetaDirectEditOutcome.ProjectNotFound, EmptyErrors);
}

public enum ProjectMetaDirectEditOutcome
{
    Success,
    ValidationFailed,
    ProjectNotFound
}
