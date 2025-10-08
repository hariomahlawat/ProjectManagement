using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectTotService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public ProjectTotService(ApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ProjectTotUpdateResult> UpdateAsync(
        int projectId,
        ProjectTotUpdateRequest request,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("A valid user is required to update Transfer of Technology details.", nameof(actorUserId));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var project = await _db.Projects
            .Include(p => p.Tot)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null)
        {
            return ProjectTotUpdateResult.NotFound();
        }

        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            _clock.UtcNow.UtcDateTime,
            TimeZoneHelper.GetIst()));

        var trimmedRemarks = string.IsNullOrWhiteSpace(request.Remarks)
            ? null
            : request.Remarks.Trim();

        if (trimmedRemarks is { Length: > 2000 })
        {
            return ProjectTotUpdateResult.ValidationFailed("Remarks must be 2000 characters or fewer.");
        }

        switch (request.Status)
        {
            case ProjectTotStatus.NotRequired:
            {
                if (request.StartedOn.HasValue || request.CompletedOn.HasValue)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "Start and completion dates must be empty when ToT is not required.");
                }
                break;
            }
            case ProjectTotStatus.NotStarted:
            {
                if (request.StartedOn.HasValue || request.CompletedOn.HasValue)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "Start and completion dates must be empty until ToT is in progress.");
                }
                break;
            }
            case ProjectTotStatus.InProgress:
            {
                if (request.StartedOn is null)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Start date is required when ToT is in progress.");
                }

                if (request.StartedOn.Value > todayLocal)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Start date cannot be in the future.");
                }

                if (request.CompletedOn.HasValue)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "Completion date must be empty until ToT is completed.");
                }

                break;
            }
            case ProjectTotStatus.Completed:
            {
                if (request.StartedOn is null)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Start date is required when ToT is completed.");
                }

                if (request.CompletedOn is null)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Completion date is required when ToT is completed.");
                }

                if (request.CompletedOn.Value < request.StartedOn.Value)
                {
                    return ProjectTotUpdateResult.ValidationFailed(
                        "Completion date cannot be earlier than the start date.");
                }

                if (request.StartedOn.Value > todayLocal)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Start date cannot be in the future.");
                }

                if (request.CompletedOn.Value > todayLocal)
                {
                    return ProjectTotUpdateResult.ValidationFailed("Completion date cannot be in the future.");
                }

                break;
            }
            default:
            {
                return ProjectTotUpdateResult.ValidationFailed("Invalid ToT status specified.");
            }
        }

        var tot = project.Tot;
        if (tot is null)
        {
            tot = new ProjectTot
            {
                ProjectId = projectId
            };
            project.Tot = tot;
            _db.ProjectTots.Add(tot);
        }

        tot.Status = request.Status;
        tot.Remarks = trimmedRemarks;

        switch (request.Status)
        {
            case ProjectTotStatus.NotRequired:
            case ProjectTotStatus.NotStarted:
                tot.StartedOn = null;
                tot.CompletedOn = null;
                break;
            case ProjectTotStatus.InProgress:
                tot.StartedOn = request.StartedOn;
                tot.CompletedOn = null;
                break;
            case ProjectTotStatus.Completed:
                tot.StartedOn = request.StartedOn;
                tot.CompletedOn = request.CompletedOn;
                break;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return ProjectTotUpdateResult.Success();
    }
}
