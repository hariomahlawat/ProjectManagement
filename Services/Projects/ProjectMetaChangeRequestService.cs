using System;
using System.Collections.Generic;
using System.Text.Json;
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

    public async Task<ProjectMetaChangeRequestSubmissionResult> SubmitAsync(
        ProjectMetaChangeRequestSubmission submission,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (submission is null)
        {
            throw new ArgumentNullException(nameof(submission));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A valid user identifier is required.", nameof(userId));
        }

        var project = await _db.Projects
            .SingleOrDefaultAsync(p => p.Id == submission.ProjectId, cancellationToken);

        if (project is null)
        {
            return ProjectMetaChangeRequestSubmissionResult.ProjectNotFound();
        }

        if (!string.Equals(project.LeadPoUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return ProjectMetaChangeRequestSubmissionResult.NotProjectOfficer();
        }

        var trimmedName = string.IsNullOrWhiteSpace(submission.Name)
            ? project.Name
            : submission.Name.Trim();
        var trimmedDescription = string.IsNullOrWhiteSpace(submission.Description)
            ? null
            : submission.Description.Trim();
        var trimmedCaseFileNumber = string.IsNullOrWhiteSpace(submission.CaseFileNumber)
            ? null
            : submission.CaseFileNumber.Trim();
        var categoryId = submission.CategoryId;
        var technicalCategoryId = submission.TechnicalCategoryId;
        var projectTypeId = submission.ProjectTypeId;
        var isBuild = submission.IsBuild;
        var sponsoringUnitId = submission.SponsoringUnitId;
        var sponsoringLineDirectorateId = submission.SponsoringLineDirectorateId;

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
                return ProjectMetaChangeRequestSubmissionResult.ValidationFailed(
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["CaseFileNumber"] = new[] { ProjectValidationMessages.DuplicateCaseFileNumber }
                    });
            }
        }

        if (categoryId.HasValue)
        {
            var categoryExists = await _db.ProjectCategories
                .AsNoTracking()
                .AnyAsync(c => c.Id == categoryId.Value && c.IsActive, cancellationToken);

            if (!categoryExists)
            {
                return ProjectMetaChangeRequestSubmissionResult.ValidationFailed(
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["CategoryId"] = new[] { ProjectValidationMessages.InactiveCategory }
                    });
            }
        }

        if (technicalCategoryId.HasValue)
        {
            var technicalCategoryExists = await _db.TechnicalCategories
                .AsNoTracking()
                .AnyAsync(c => c.Id == technicalCategoryId.Value && c.IsActive, cancellationToken);

            if (!technicalCategoryExists)
            {
                return ProjectMetaChangeRequestSubmissionResult.ValidationFailed(
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["TechnicalCategoryId"] = new[] { ProjectValidationMessages.InactiveTechnicalCategory }
                    });
            }
        }

        if (projectTypeId.HasValue)
        {
            var projectTypeExists = await _db.ProjectTypes
                .AsNoTracking()
                .AnyAsync(p => p.Id == projectTypeId.Value && p.IsActive, cancellationToken);

            if (!projectTypeExists)
            {
                return ProjectMetaChangeRequestSubmissionResult.ValidationFailed(
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ProjectTypeId"] = new[] { ProjectValidationMessages.InactiveProjectType }
                    });
            }
        }

        if (sponsoringUnitId.HasValue)
        {
            var unitExists = await _db.SponsoringUnits
                .AsNoTracking()
                .AnyAsync(u => u.Id == sponsoringUnitId.Value && u.IsActive, cancellationToken);

            if (!unitExists)
            {
                return ProjectMetaChangeRequestSubmissionResult.ValidationFailed(
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["SponsoringUnitId"] = new[] { ProjectValidationMessages.InactiveSponsoringUnit }
                    });
            }
        }

        if (sponsoringLineDirectorateId.HasValue)
        {
            var lineExists = await _db.LineDirectorates
                .AsNoTracking()
                .AnyAsync(l => l.Id == sponsoringLineDirectorateId.Value && l.IsActive, cancellationToken);

            if (!lineExists)
            {
                return ProjectMetaChangeRequestSubmissionResult.ValidationFailed(
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["SponsoringLineDirectorateId"] = new[] { ProjectValidationMessages.InactiveLineDirectorate }
                    });
            }
        }

        var payload = new ProjectMetaChangeRequestPayload
        {
            Name = trimmedName,
            Description = trimmedDescription,
            CaseFileNumber = trimmedCaseFileNumber,
            CategoryId = categoryId,
            TechnicalCategoryId = technicalCategoryId,
            ProjectTypeId = projectTypeId,
            IsBuild = isBuild,
            SponsoringUnitId = sponsoringUnitId,
            SponsoringLineDirectorateId = sponsoringLineDirectorateId
        };

        var reason = string.IsNullOrWhiteSpace(submission.Reason)
            ? null
            : submission.Reason.Trim();

        var serializedPayload = JsonSerializer.Serialize(payload);

        var pending = await _db.ProjectMetaChangeRequests
            .SingleOrDefaultAsync(
                r => r.ProjectId == project.Id && r.DecisionStatus == ProjectMetaDecisionStatuses.Pending,
                cancellationToken);

        var now = _clock.UtcNow;

        static byte[]? SnapshotRowVersion(Project project)
        {
            if (project.RowVersion is null || project.RowVersion.Length == 0)
            {
                return null;
            }

            var copy = new byte[project.RowVersion.Length];
            Array.Copy(project.RowVersion, copy, project.RowVersion.Length);
            return copy;
        }

        void ApplySnapshot(ProjectMetaChangeRequest target)
        {
            target.OriginalName = project.Name;
            target.OriginalDescription = project.Description;
            target.OriginalCaseFileNumber = project.CaseFileNumber;
            target.OriginalCategoryId = project.CategoryId;
            target.OriginalTechnicalCategoryId = project.TechnicalCategoryId;
            target.OriginalProjectTypeId = project.ProjectTypeId;
            target.OriginalIsBuild = project.IsBuild;
            target.OriginalRowVersion = SnapshotRowVersion(project);
            target.OriginalSponsoringUnitId = project.SponsoringUnitId;
            target.OriginalSponsoringLineDirectorateId = project.SponsoringLineDirectorateId;
            target.TechnicalCategoryId = technicalCategoryId;
        }

        if (pending is not null)
        {
            pending.Payload = serializedPayload;
            pending.RequestedByUserId = userId;
            pending.RequestedOnUtc = now;
            pending.ChangeType = ProjectMetaChangeRequestChangeTypes.Meta;
            pending.RequestNote = reason;
            ApplySnapshot(pending);

            await _db.SaveChangesAsync(cancellationToken);

            return ProjectMetaChangeRequestSubmissionResult.Success(pending.Id);
        }

        var request = new ProjectMetaChangeRequest
        {
            ProjectId = project.Id,
            ChangeType = ProjectMetaChangeRequestChangeTypes.Meta,
            Payload = serializedPayload,
            RequestedByUserId = userId,
            RequestedOnUtc = now,
            DecisionStatus = ProjectMetaDecisionStatuses.Pending,
            RequestNote = reason,
            OriginalName = project.Name,
            OriginalDescription = project.Description,
            OriginalCaseFileNumber = project.CaseFileNumber,
            OriginalCategoryId = project.CategoryId,
            OriginalTechnicalCategoryId = project.TechnicalCategoryId,
            OriginalProjectTypeId = project.ProjectTypeId,
            OriginalIsBuild = project.IsBuild,
            OriginalRowVersion = SnapshotRowVersion(project),
            OriginalSponsoringUnitId = project.SponsoringUnitId,
            OriginalSponsoringLineDirectorateId = project.SponsoringLineDirectorateId,
            TechnicalCategoryId = technicalCategoryId
        };

        await _db.ProjectMetaChangeRequests.AddAsync(request, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return ProjectMetaChangeRequestSubmissionResult.Success(request.Id);
    }
}

public sealed class ProjectMetaChangeRequestSubmission
{
    public int ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? CaseFileNumber { get; set; }

    public int? CategoryId { get; set; }

    public int? TechnicalCategoryId { get; set; }

    // SECTION: Project type and build flag updates
    public int? ProjectTypeId { get; set; }

    public bool? IsBuild { get; set; }

    public int? SponsoringUnitId { get; set; }

    public int? SponsoringLineDirectorateId { get; set; }

    public string? Reason { get; set; }
}

public sealed record ProjectMetaChangeRequestSubmissionResult
{
    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors
        = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    private ProjectMetaChangeRequestSubmissionResult(
        ProjectMetaChangeRequestSubmissionOutcome outcome,
        int? requestId,
        IReadOnlyDictionary<string, string[]> errors)
    {
        Outcome = outcome;
        RequestId = requestId;
        Errors = errors;
    }

    public ProjectMetaChangeRequestSubmissionOutcome Outcome { get; }

    public int? RequestId { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static ProjectMetaChangeRequestSubmissionResult Success(int requestId)
        => new(ProjectMetaChangeRequestSubmissionOutcome.Success, requestId, EmptyErrors);

    public static ProjectMetaChangeRequestSubmissionResult ProjectNotFound()
        => new(ProjectMetaChangeRequestSubmissionOutcome.ProjectNotFound, null, EmptyErrors);

    public static ProjectMetaChangeRequestSubmissionResult NotProjectOfficer()
        => new(ProjectMetaChangeRequestSubmissionOutcome.NotProjectOfficer, null, EmptyErrors);

    public static ProjectMetaChangeRequestSubmissionResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> errors)
    {
        if (errors is null)
        {
            throw new ArgumentNullException(nameof(errors));
        }

        var copy = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in errors)
        {
            copy[key] = value;
        }

        return new(
            ProjectMetaChangeRequestSubmissionOutcome.ValidationFailed,
            null,
            copy);
    }
}

public enum ProjectMetaChangeRequestSubmissionOutcome
{
    Success,
    ProjectNotFound,
    NotProjectOfficer,
    ValidationFailed
}

public static class ProjectMetaChangeRequestChangeTypes
{
    public const string Meta = "Meta";
}
