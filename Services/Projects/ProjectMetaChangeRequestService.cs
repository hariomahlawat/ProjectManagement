using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectMetaChangeRequestService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public ProjectMetaChangeRequestService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProjectMetaChangeRequestResult> CreateAsync(
        ProjectMetaChangeRequestInput input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.RequestedByUserId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.ChangeType))
        {
            throw new ArgumentException("A change type is required.", nameof(input));
        }

        if (input.Payload is null)
        {
            throw new ArgumentException("A payload is required.", nameof(input));
        }

        var project = await _db.Projects
            .SingleOrDefaultAsync(p => p.Id == input.ProjectId, cancellationToken);

        if (project is null)
        {
            return ProjectMetaChangeRequestResult.ProjectNotFound();
        }

        var trimmedCaseFileNumber = string.IsNullOrWhiteSpace(input.ProposedCaseFileNumber)
            ? null
            : input.ProposedCaseFileNumber.Trim();

        if (!string.IsNullOrEmpty(trimmedCaseFileNumber))
        {
            var exists = await _db.Projects
                .AnyAsync(
                    p => p.CaseFileNumber == trimmedCaseFileNumber && p.Id != project.Id,
                    cancellationToken);

            if (exists)
            {
                return ProjectMetaChangeRequestResult.ValidationFailed("Case file number already exists.");
            }
        }

        var request = new ProjectMetaChangeRequest
        {
            ProjectId = project.Id,
            ChangeType = input.ChangeType.Trim(),
            Payload = input.Payload,
            RequestedByUserId = input.RequestedByUserId,
            RequestedOnUtc = _clock.UtcNow
        };

        await _db.ProjectMetaChangeRequests.AddAsync(request, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return ProjectMetaChangeRequestResult.Success(request.Id);
    }
}

public sealed record ProjectMetaChangeRequestInput(
    int ProjectId,
    string RequestedByUserId,
    string ChangeType,
    string Payload,
    string? ProposedCaseFileNumber);

public enum ProjectMetaChangeRequestOutcome
{
    Success,
    ProjectNotFound,
    ValidationFailed
}

public sealed record ProjectMetaChangeRequestResult(
    ProjectMetaChangeRequestOutcome Outcome,
    string? Error = null,
    int? RequestId = null)
{
    public static ProjectMetaChangeRequestResult Success(int requestId)
        => new(ProjectMetaChangeRequestOutcome.Success, null, requestId);

    public static ProjectMetaChangeRequestResult ProjectNotFound()
        => new(ProjectMetaChangeRequestOutcome.ProjectNotFound);

    public static ProjectMetaChangeRequestResult ValidationFailed(string error)
        => new(ProjectMetaChangeRequestOutcome.ValidationFailed, error);
}
