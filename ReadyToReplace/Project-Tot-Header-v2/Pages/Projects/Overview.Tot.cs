using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities.PartialDates;

namespace ProjectManagement.Pages.Projects;

public partial class OverviewModel
{
    public sealed class TotDrawerInput
    {
        public int ProjectId { get; set; }

        public ProjectTotStatus Status { get; set; } = ProjectTotStatus.NotStarted;

        [Range(1900, 2100)]
        public int? StartYear { get; set; }

        [Range(1, 12)]
        public int? StartMonth { get; set; }

        [Range(1, 31)]
        public int? StartDay { get; set; }

        [Range(1900, 2100)]
        public int? CompletionYear { get; set; }

        [Range(1, 12)]
        public int? CompletionMonth { get; set; }

        [Range(1, 31)]
        public int? CompletionDay { get; set; }

        [MaxLength(2000)]
        public string? MetDetails { get; set; }

        public DateOnly? MetCompletedOn { get; set; }

        public bool? FirstProductionModelManufactured { get; set; }

        public DateOnly? FirstProductionModelManufacturedOn { get; set; }

        public PartialDateInput StartPartial() => new()
        {
            Year = StartYear,
            Month = StartMonth,
            Day = StartDay
        };

        public PartialDateInput CompletionPartial() => new()
        {
            Year = CompletionYear,
            Month = CompletionMonth,
            Day = CompletionDay
        };
    }

    public async Task<IActionResult> OnGetTotDetailsAsync(int id, CancellationToken ct)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .Include(item => item.Tot)
            .Where(item => item.Id == id && !item.IsDeleted)
            .Select(item => new
            {
                item.Id,
                item.LifecycleStatus,
                item.IsArchived,
                item.LeadPoUserId,
                Tot = item.Tot == null
                    ? null
                    : new
                    {
                        item.Tot.Status,
                        item.Tot.StartedOn,
                        item.Tot.CompletedOn,
                        item.Tot.MetDetails,
                        item.Tot.MetCompletedOn,
                        item.Tot.FirstProductionModelManufactured,
                        item.Tot.FirstProductionModelManufacturedOn
                    }
            })
            .SingleOrDefaultAsync(ct);

        if (project is null)
        {
            return new JsonResult(new { error = "Project not found." })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }

        if (project.LifecycleStatus != ProjectLifecycleStatus.Completed)
        {
            return new JsonResult(new { error = "Transfer of Technology can be recorded only after the project is completed." })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }

        var pending = await _db.ProjectTotRequests
            .AsNoTracking()
            .Where(request => request.ProjectId == id && request.DecisionState == ProjectTotRequestDecisionState.Pending)
            .Select(request => new
            {
                request.ProposedStatus,
                request.SubmittedOnUtc
            })
            .SingleOrDefaultAsync(ct);

        var tot = project.Tot;
        var status = tot?.Status ?? ProjectTotStatus.NotStarted;
        var hasRecord = tot is not null;

        return new JsonResult(new
        {
            success = true,
            canManage = await CanManageTotFromOverviewAsync(id, project.LeadPoUserId, project.IsArchived, ct),
            pendingApproval = pending is not null,
            pending = pending is null
                ? null
                : new
                {
                    proposedStatus = pending.ProposedStatus.ToString(),
                    proposedStatusLabel = TotStatusLabel(pending.ProposedStatus),
                    submittedOnUtc = pending.SubmittedOnUtc
                },
            input = new
            {
                projectId = id,
                status = status.ToString(),
                startYear = tot?.StartedOn?.Year,
                startMonth = tot?.StartedOn?.Month,
                startDay = tot?.StartedOn?.Day,
                completionYear = tot?.CompletedOn?.Year,
                completionMonth = tot?.CompletedOn?.Month,
                completionDay = tot?.CompletedOn?.Day,
                metDetails = tot?.MetDetails,
                metCompletedOn = tot?.MetCompletedOn?.ToString("yyyy-MM-dd"),
                firstProductionModelManufactured = tot?.FirstProductionModelManufactured,
                firstProductionModelManufacturedOn = tot?.FirstProductionModelManufacturedOn?.ToString("yyyy-MM-dd")
            },
            card = BuildTotCard(status, hasRecord, pending?.ProposedStatus),
            summary = BuildTotSummaryPayload(tot)
        });
    }

    public async Task<IActionResult> OnPostTotAsync(int id, [FromForm] TotDrawerInput? input, CancellationToken ct)
    {
        if (input is null || input.ProjectId != id || id <= 0)
        {
            return new JsonResult(new { error = "The Transfer of Technology form is not valid for this project." })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        var project = await _db.Projects
            .AsNoTracking()
            .Where(item => item.Id == id && !item.IsDeleted)
            .Select(item => new
            {
                item.Id,
                item.LifecycleStatus,
                item.IsArchived,
                item.LeadPoUserId
            })
            .SingleOrDefaultAsync(ct);

        if (project is null)
        {
            return new JsonResult(new { error = "Project not found." })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }

        if (project.LifecycleStatus != ProjectLifecycleStatus.Completed)
        {
            return new JsonResult(new { error = "Transfer of Technology can be updated only after the project is completed." })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }

        if (!await CanManageTotFromOverviewAsync(id, project.LeadPoUserId, project.IsArchived, ct))
        {
            return new JsonResult(new { error = "You are not authorised to update Transfer of Technology for this project." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        var hasPendingRequest = await _db.ProjectTotRequests
            .AsNoTracking()
            .AnyAsync(request => request.ProjectId == id && request.DecisionState == ProjectTotRequestDecisionState.Pending, ct);
        if (hasPendingRequest)
        {
            return new JsonResult(new { error = "A Transfer of Technology update is already pending approval for this project." })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }

        if (!TryBuildTotRequest(input, out var request, out var validationError))
        {
            return new JsonResult(new { error = validationError })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        var actorUserId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Forbid();
        }

        var service = new ProjectTotService(_db, _clock);
        var result = await service.UpdateAsync(id, request!, actorUserId, ct);
        if (result.Status == ProjectTotUpdateStatus.NotFound)
        {
            return new JsonResult(new { error = "Project not found." })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }

        if (!result.IsSuccess)
        {
            return new JsonResult(new { error = result.ErrorMessage ?? "Unable to update Transfer of Technology details." })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        _logger.LogInformation(
            "Project ToT updated from overview. ProjectId={ProjectId}, UserId={UserId}, Status={Status}",
            id,
            actorUserId,
            input.Status);

        return new JsonResult(new
        {
            success = true,
            message = "Transfer of Technology details updated.",
            card = BuildTotCard(input.Status, true, null)
        });
    }

    private async Task<bool> CanManageTotFromOverviewAsync(
        int projectId,
        string? leadPoUserId,
        bool isArchived,
        CancellationToken ct)
    {
        if (isArchived)
        {
            return false;
        }

        var currentUserId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return false;
        }

        if (User.IsInRole("Admin") || User.IsInRole("HoD"))
        {
            return await _db.Projects
                .AsNoTracking()
                .AnyAsync(project => project.Id == projectId && !project.IsDeleted, ct);
        }

        return User.IsInRole("Project Officer") &&
               string.Equals(leadPoUserId, currentUserId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryBuildTotRequest(
        TotDrawerInput input,
        out ProjectTotUpdateRequest? request,
        out string? error)
    {
        request = null;
        error = null;

        var startPartial = input.StartPartial();
        var completionPartial = input.CompletionPartial();
        var startPrecision = startPartial.GetPrecision();
        var completionPrecision = completionPartial.GetPrecision();
        DateOnly? startedOn = null;
        DateOnly? completedOn = null;

        switch (input.Status)
        {
            case ProjectTotStatus.InProgress:
                if (!PartialDateHelper.TryToStartDate(startPartial, out var inProgressStart, out var startError))
                {
                    error = startError ?? "Start date is required when ToT is in progress.";
                    return false;
                }
                startedOn = inProgressStart;
                if (completionPrecision != PartialDatePrecision.None)
                {
                    error = "Completion date must be empty while ToT is in progress.";
                    return false;
                }
                break;

            case ProjectTotStatus.Completed:
                if (!PartialDateHelper.TryToCompletionDate(completionPartial, out var completionDate, out var completionError))
                {
                    error = completionError ?? "Completion date is required when ToT is completed.";
                    return false;
                }
                completedOn = completionDate;
                if (startPrecision != PartialDatePrecision.None)
                {
                    if (!PartialDateHelper.TryToStartDate(startPartial, out var completedStart, out var completedStartError))
                    {
                        error = completedStartError ?? "The ToT start date is not valid.";
                        return false;
                    }
                    startedOn = completedStart;
                }
                break;

            case ProjectTotStatus.NotStarted:
            case ProjectTotStatus.NotRequired:
                if (startPrecision != PartialDatePrecision.None || completionPrecision != PartialDatePrecision.None)
                {
                    error = "Start and completion dates must be empty when ToT is not started or not required.";
                    return false;
                }
                break;

            default:
                error = "Select a valid Transfer of Technology status.";
                return false;
        }

        request = new ProjectTotUpdateRequest(
            input.Status,
            startedOn,
            startPrecision,
            completedOn,
            completionPrecision,
            input.MetDetails,
            input.MetCompletedOn,
            input.FirstProductionModelManufactured,
            input.FirstProductionModelManufacturedOn);
        return true;
    }

    private static object BuildTotCard(
        ProjectTotStatus status,
        bool hasRecord,
        ProjectTotStatus? proposedStatus)
    {
        if (proposedStatus.HasValue)
        {
            return new
            {
                title = "Approval pending",
                summary = $"Proposed: {TotStatusLabel(proposedStatus.Value)}",
                tone = "info"
            };
        }

        var title = hasRecord ? TotStatusLabel(status) : "Not recorded";
        var summary = status switch
        {
            ProjectTotStatus.NotRequired => "No ToT action required",
            ProjectTotStatus.NotStarted => hasRecord ? "ToT action pending" : "Record ToT position",
            ProjectTotStatus.InProgress => "ToT is in progress",
            ProjectTotStatus.Completed => "ToT marked completed",
            _ => "Record ToT position"
        };
        var tone = status switch
        {
            ProjectTotStatus.Completed => "positive",
            ProjectTotStatus.InProgress => "info",
            ProjectTotStatus.NotRequired => "neutral",
            _ => "warning"
        };

        return new { title, summary, tone };
    }

    private static object BuildTotSummaryPayload(ProjectTot? tot)
    {
        if (tot is null)
        {
            return new
            {
                hasRecord = false,
                statusLabel = "Not recorded",
                summary = "No Transfer of Technology position has been recorded.",
                facts = Array.Empty<object>()
            };
        }

        var facts = new System.Collections.Generic.List<object>();
        if (tot.StartedOn.HasValue) facts.Add(new { label = "Started on", value = tot.StartedOn.Value.ToString("dd MMM yyyy") });
        if (tot.CompletedOn.HasValue) facts.Add(new { label = "Completed on", value = tot.CompletedOn.Value.ToString("dd MMM yyyy") });
        if (!string.IsNullOrWhiteSpace(tot.MetDetails)) facts.Add(new { label = "MET details", value = tot.MetDetails });
        if (tot.MetCompletedOn.HasValue) facts.Add(new { label = "MET completed on", value = tot.MetCompletedOn.Value.ToString("dd MMM yyyy") });
        if (tot.FirstProductionModelManufactured.HasValue) facts.Add(new { label = "First production model", value = tot.FirstProductionModelManufactured.Value ? "Manufactured" : "Not manufactured" });
        if (tot.FirstProductionModelManufacturedOn.HasValue) facts.Add(new { label = "Manufactured on", value = tot.FirstProductionModelManufacturedOn.Value.ToString("dd MMM yyyy") });

        var summary = tot.Status switch
        {
            ProjectTotStatus.NotRequired => "Transfer of Technology is not required for this project.",
            ProjectTotStatus.NotStarted => "Transfer of Technology has not started.",
            ProjectTotStatus.InProgress => "Transfer of Technology is in progress.",
            ProjectTotStatus.Completed => "Transfer of Technology is completed.",
            _ => "Transfer of Technology details are unavailable."
        };

        return new
        {
            hasRecord = true,
            statusLabel = TotStatusLabel(tot.Status),
            summary,
            facts
        };
    }

    private static string TotStatusLabel(ProjectTotStatus status) => status switch
    {
        ProjectTotStatus.NotRequired => "Not required",
        ProjectTotStatus.NotStarted => "Not started",
        ProjectTotStatus.InProgress => "In progress",
        ProjectTotStatus.Completed => "Completed",
        _ => status.ToString()
    };
}
