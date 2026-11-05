using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services.Authorization;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities;
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
            .SingleOrDefaultAsync(item => item.Id == id && !item.IsDeleted, ct);

        if (project is null)
        {
            return new JsonResult(new { error = "Project not found." })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }

        var applicabilityError = ProjectTotApplicabilityPolicy.GetIneligibilityReason(project);
        if (project.IsBuild)
        {
            return new JsonResult(new
            {
                success = true,
                canManage = false,
                pendingApproval = false,
                pending = (object?)null,
                input = new { projectId = id },
                card = new
                {
                    title = "Not applicable",
                    summary = "Repeat Build",
                    tone = "neutral"
                },
                summary = new
                {
                    hasRecord = false,
                    statusLabel = "Not applicable",
                    summary = "Transfer of Technology is not applicable to Repeat Build projects.",
                    facts = Array.Empty<object>()
                },
                latestRemark = (object?)null
            });
        }

        if (applicabilityError is not null && project.LifecycleStatus != ProjectLifecycleStatus.Completed)
        {
            return new JsonResult(new { error = applicabilityError })
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
        var canManage = await CanManageTotFromOverviewAsync(id, project.LeadPoUserId, project.IsArchived, ct);
        // Rows saved before precision was tracked carry a full date; show all of it.
        var startPrecision = EffectivePrecision(tot?.StartDatePrecision);
        var completionPrecision = EffectivePrecision(tot?.CompletionDatePrecision);

        return new JsonResult(new
        {
            success = true,
            canManage,
            pendingApproval = pending is not null,
            pending = pending is null
                ? null
                : new
                {
                    proposedStatus = pending.ProposedStatus.ToString(),
                    proposedStatusLabel = ProjectTotPresentation.StatusLabel(pending.ProposedStatus),
                    submittedOnUtc = pending.SubmittedOnUtc
                },
            input = new
            {
                projectId = id,
                status = status.ToString(),
                startYear = tot?.StartedOn?.Year,
                startMonth = startPrecision >= PartialDatePrecision.Month ? tot?.StartedOn?.Month : null,
                startDay = startPrecision == PartialDatePrecision.Day ? tot?.StartedOn?.Day : null,
                completionYear = tot?.CompletedOn?.Year,
                completionMonth = completionPrecision >= PartialDatePrecision.Month ? tot?.CompletedOn?.Month : null,
                completionDay = completionPrecision == PartialDatePrecision.Day ? tot?.CompletedOn?.Day : null,
                metDetails = tot?.MetDetails,
                metCompletedOn = tot?.MetCompletedOn?.ToString("yyyy-MM-dd"),
                firstProductionModelManufactured = tot?.FirstProductionModelManufactured,
                firstProductionModelManufacturedOn = tot?.FirstProductionModelManufacturedOn?.ToString("yyyy-MM-dd")
            },
            card = ProjectTotPresentation.BuildCard(
                tot?.Status,
                tot?.StartedOn,
                tot?.StartDatePrecision ?? PartialDatePrecision.None,
                tot?.CompletedOn,
                tot?.CompletionDatePrecision ?? PartialDatePrecision.None,
                pending?.ProposedStatus,
                canManage),
            summary = BuildTotSummaryPayload(tot),
            latestRemark = await LoadLatestTotRemarkPayloadAsync(id, ct)
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
            .SingleOrDefaultAsync(item => item.Id == id && !item.IsDeleted, ct);

        if (project is null)
        {
            return new JsonResult(new { error = "Project not found." })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }

        if (ProjectTotApplicabilityPolicy.GetIneligibilityReason(project) is { } applicabilityError)
        {
            return new JsonResult(new { error = applicabilityError })
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
        var result = await service.SaveFromProjectAsync(
            id,
            request!,
            actorUserId,
            ApprovalAuthorization.CanApproveProjectChanges(User),
            ct);
        if (result.Status == ProjectTotUpdateStatus.NotFound)
        {
            return new JsonResult(new { error = "Project not found." })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }

        if (result.Status == ProjectTotUpdateStatus.Conflict)
        {
            return new JsonResult(new { error = result.ErrorMessage })
            {
                StatusCode = StatusCodes.Status409Conflict
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
            "Project ToT {Action} from overview. ProjectId={ProjectId}, UserId={UserId}, Status={Status}",
            result.SubmittedForApproval ? "submitted for approval" : "updated",
            id,
            actorUserId,
            input.Status);

        return new JsonResult(new
        {
            success = true,
            submittedForApproval = result.SubmittedForApproval,
            message = result.SubmittedForApproval
                ? "Transfer of Technology update sent to HoD for approval."
                : "Transfer of Technology details updated.",
            card = ProjectTotPresentation.BuildCard(
                input.Status,
                request!.StartedOn,
                request.StartDatePrecision,
                request.CompletedOn,
                request.CompletionDatePrecision,
                result.SubmittedForApproval ? input.Status : null,
                canManage: true)
        });
    }

    public async Task<IActionResult> OnPostTotRemarkAsync(
        int id,
        [FromForm] int projectId,
        [FromForm] string? body,
        CancellationToken ct)
    {
        if (id <= 0 || projectId != id)
        {
            return new JsonResult(new { error = "The ToT remark form is not valid for this project." })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && !item.IsDeleted, ct);

        if (project is null)
        {
            return new JsonResult(new { error = "Project not found." })
            {
                StatusCode = StatusCodes.Status404NotFound
            };
        }

        if (ProjectTotApplicabilityPolicy.GetIneligibilityReason(project) is { } remarkApplicabilityError)
        {
            return new JsonResult(new { error = remarkApplicabilityError })
            {
                StatusCode = StatusCodes.Status409Conflict
            };
        }

        if (!await CanManageTotFromOverviewAsync(id, project.LeadPoUserId, project.IsArchived, ct))
        {
            return new JsonResult(new { error = "You are not authorised to add a ToT remark for this project." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        var normalized = body?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 4 || normalized.Length > 2000)
        {
            return new JsonResult(new { error = "Remarks must contain between 4 and 2000 characters." })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        var userId = _users.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();

        var role = User.IsInRole("Admin")
            ? RemarkActorRole.Administrator
            : User.IsInRole("HoD")
                ? RemarkActorRole.HeadOfDepartment
                : RemarkActorRole.ProjectOfficer;

        var remark = new Remark
        {
            ProjectId = id,
            AuthorUserId = userId,
            AuthorRole = role,
            Type = RemarkType.Internal,
            Scope = RemarkScope.TransferOfTechnology,
            Body = normalized,
            EventDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, TimeZoneHelper.GetIst())),
            CreatedAtUtc = _clock.UtcNow.UtcDateTime,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        _db.Remarks.Add(remark);
        await _db.SaveChangesAsync(ct);

        return new JsonResult(new { success = true, message = "Transfer of Technology remark added." });
    }

    private async Task<object?> LoadLatestTotRemarkPayloadAsync(int projectId, CancellationToken ct)
    {
        var latest = await _db.Remarks
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId && !item.IsDeleted && item.Scope == RemarkScope.TransferOfTechnology)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Select(item => new { item.Body, item.Type, item.AuthorUserId, item.CreatedAtUtc })
            .FirstOrDefaultAsync(ct);
        if (latest is null) return null;
        var author = await _db.Users.AsNoTracking()
            .Where(item => item.Id == latest.AuthorUserId)
            .Select(item => item.FullName ?? item.UserName ?? item.Email ?? item.Id)
            .FirstOrDefaultAsync(ct) ?? "Unknown";
        return new
        {
            body = latest.Body,
            meta = $"{(latest.Type == RemarkType.External ? "External" : "Internal")} · {author}"
        };
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
        var dates = ProjectTotDateInput.Resolve(input.Status, input.StartPartial(), input.CompletionPartial());
        if (!dates.IsValid)
        {
            request = null;
            error = dates.Errors[0].Message;
            return false;
        }

        request = new ProjectTotUpdateRequest(
            input.Status,
            dates.StartedOn,
            dates.StartDatePrecision,
            dates.CompletedOn,
            dates.CompletionDatePrecision,
            input.MetDetails,
            input.MetCompletedOn,
            input.FirstProductionModelManufactured,
            input.FirstProductionModelManufacturedOn);
        error = null;
        return true;
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
        if (tot.StartedOn.HasValue) facts.Add(new { label = "Started on", value = FormatPartialDate(tot.StartedOn.Value, tot.StartDatePrecision) });
        if (tot.CompletedOn.HasValue) facts.Add(new { label = "Completed on", value = FormatPartialDate(tot.CompletedOn.Value, tot.CompletionDatePrecision) });
        if (!string.IsNullOrWhiteSpace(tot.MetDetails)) facts.Add(new { label = "MET details", value = tot.MetDetails });
        if (tot.MetCompletedOn.HasValue) facts.Add(new { label = "MET completed on", value = tot.MetCompletedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) });
        if (tot.FirstProductionModelManufactured.HasValue) facts.Add(new { label = "First production model", value = tot.FirstProductionModelManufactured.Value ? "Manufactured" : "Not manufactured" });
        if (tot.FirstProductionModelManufacturedOn.HasValue) facts.Add(new { label = "Manufactured on", value = tot.FirstProductionModelManufacturedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) });

        var summary = tot.Status switch
        {
            ProjectTotStatus.NotRequired => "Transfer of Technology is not required for this project.",
            ProjectTotStatus.NotStarted => "Transfer of Technology has not started.",
            ProjectTotStatus.InProgress => "Transfer of Technology is in progress.",
            ProjectTotStatus.Completed => "Transfer of Technology is completed.",
            ProjectTotStatus.NotKnown => "It is not known whether Transfer of Technology took place.",
            _ => "Transfer of Technology details are unavailable."
        };
        return new { hasRecord = true, statusLabel = TotStatusLabel(tot.Status), summary, facts };
    }

    private static string FormatPartialDate(DateOnly value, PartialDatePrecision precision)
        => ProjectTotPresentation.FormatDate(value, precision);

    private static PartialDatePrecision EffectivePrecision(PartialDatePrecision? precision)
        => precision is null or PartialDatePrecision.None ? PartialDatePrecision.Day : precision.Value;

    private static string TotStatusLabel(ProjectTotStatus status) => ProjectTotPresentation.StatusLabel(status);
}
