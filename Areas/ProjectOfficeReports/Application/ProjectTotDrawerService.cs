using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Services.Remarks;
using ProjectManagement.Utilities;
using ProjectManagement.Utilities.PartialDates;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

/// <summary>
/// What the current user may do in the ToT drawer. Each page that hosts the drawer decides this
/// with its own rules (the project page: HoD, Admin, the project's PO; the ToT register: the
/// register's submit rules); the drawer's work is the same either way.
/// </summary>
public sealed record ProjectTotDrawerAccess(
    string? UserId,
    Func<Project, CancellationToken, Task<bool>> CanManageAsync,
    bool CanApprove,
    bool IsAdmin,
    bool IsHoD);

/// <summary>A drawer response: an HTTP status and the JSON body the drawer script reads.</summary>
public sealed record ProjectTotDrawerResponse(int StatusCode, object Body)
{
    public static ProjectTotDrawerResponse Ok(object body) => new(StatusCodes.Status200OK, body);

    public static ProjectTotDrawerResponse Error(int statusCode, string error) => new(statusCode, new { error });
}

/// <summary>The drawer's form, as posted.</summary>
public sealed class ProjectTotDrawerInput
{
    public int ProjectId { get; set; }

    public ProjectTotStatus Status { get; set; } = ProjectTotStatus.NotStarted;

    [System.ComponentModel.DataAnnotations.Range(1900, 2100)]
    public int? StartYear { get; set; }

    [System.ComponentModel.DataAnnotations.Range(1, 12)]
    public int? StartMonth { get; set; }

    [System.ComponentModel.DataAnnotations.Range(1, 31)]
    public int? StartDay { get; set; }

    [System.ComponentModel.DataAnnotations.Range(1900, 2100)]
    public int? CompletionYear { get; set; }

    [System.ComponentModel.DataAnnotations.Range(1, 12)]
    public int? CompletionMonth { get; set; }

    [System.ComponentModel.DataAnnotations.Range(1, 31)]
    public int? CompletionDay { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(2000)]
    public string? MetDetails { get; set; }

    public DateOnly? MetCompletedOn { get; set; }

    public bool? FirstProductionModelManufactured { get; set; }

    public DateOnly? FirstProductionModelManufacturedOn { get; set; }

    public PartialDateInput StartPartial() => new() { Year = StartYear, Month = StartMonth, Day = StartDay };

    public PartialDateInput CompletionPartial() => new() { Year = CompletionYear, Month = CompletionMonth, Day = CompletionDay };
}

/// <summary>
/// The ToT drawer's reads and writes, shared by every page that hosts it. Business rules stay in
/// <see cref="ProjectTotService"/>; this class loads, checks access and shapes the drawer's JSON.
/// </summary>
public sealed class ProjectTotDrawerService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public ProjectTotDrawerService(ApplicationDbContext db, IClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProjectTotDrawerResponse> GetDetailsAsync(int projectId, ProjectTotDrawerAccess access, CancellationToken ct)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .Include(item => item.Tot)
            .SingleOrDefaultAsync(item => item.Id == projectId && !item.IsDeleted, ct);

        if (project is null)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status404NotFound, "Project not found.");
        }

        var applicabilityError = ProjectTotApplicabilityPolicy.GetIneligibilityReason(project);
        if (project.IsBuild)
        {
            return ProjectTotDrawerResponse.Ok(new
            {
                success = true,
                projectId,
                projectName = project.Name,
                canManage = false,
                pendingApproval = false,
                pending = (object?)null,
                history = Array.Empty<object>(),
                input = new { projectId },
                card = new { title = "Not applicable", summary = "Repeat Build", tone = "neutral" },
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
            return ProjectTotDrawerResponse.Error(StatusCodes.Status409Conflict, applicabilityError);
        }

        var pending = await _db.ProjectTotRequests
            .AsNoTracking()
            .Where(request => request.ProjectId == projectId && request.DecisionState == ProjectTotRequestDecisionState.Pending)
            .SingleOrDefaultAsync(ct);
        var submittedBy = pending is null ? null : await DisplayNameAsync(pending.SubmittedByUserId, ct);

        var tot = project.Tot;
        var status = tot?.Status ?? ProjectTotStatus.NotStarted;
        var canManage = await access.CanManageAsync(project, ct);
        var history = await new ProjectTotTrackerReadService(_db).GetHistoryAsync(projectId, 10, ct);
        // Rows saved before precision was tracked carry a full date; show all of it.
        var startPrecision = EffectivePrecision(tot?.StartDatePrecision);
        var completionPrecision = EffectivePrecision(tot?.CompletionDatePrecision);

        return ProjectTotDrawerResponse.Ok(new
        {
            success = true,
            projectId,
            projectName = project.Name,
            canManage,
            pendingApproval = pending is not null,
            pending = pending is null ? null : BuildPendingPayload(tot, pending, submittedBy, access.CanApprove),
            history = history.Select(BuildHistoryPayload).ToArray(),
            input = new
            {
                projectId,
                status = status.ToString(),
                startYear = tot?.StartedOn?.Year,
                startMonth = startPrecision >= PartialDatePrecision.Month ? tot?.StartedOn?.Month : null,
                startDay = startPrecision == PartialDatePrecision.Day ? tot?.StartedOn?.Day : null,
                completionYear = tot?.CompletedOn?.Year,
                completionMonth = completionPrecision >= PartialDatePrecision.Month ? tot?.CompletedOn?.Month : null,
                completionDay = completionPrecision == PartialDatePrecision.Day ? tot?.CompletedOn?.Day : null,
                metDetails = tot?.MetDetails,
                metCompletedOn = tot?.MetCompletedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                firstProductionModelManufactured = tot?.FirstProductionModelManufactured,
                firstProductionModelManufacturedOn = tot?.FirstProductionModelManufacturedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            },
            card = ProjectTotPresentation.BuildCard(
                tot?.Status,
                tot?.StartedOn,
                tot?.StartDatePrecision ?? PartialDatePrecision.None,
                tot?.CompletedOn,
                tot?.CompletionDatePrecision ?? PartialDatePrecision.None,
                pending?.ProposedStatus,
                canManage),
            summary = BuildSummaryPayload(tot),
            latestRemark = await LoadLatestRemarkPayloadAsync(projectId, ct)
        });
    }

    public async Task<ProjectTotDrawerResponse> SaveAsync(
        int projectId,
        ProjectTotDrawerInput? input,
        ProjectTotDrawerAccess access,
        CancellationToken ct)
    {
        if (input is null || input.ProjectId != projectId || projectId <= 0)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status400BadRequest, "The Transfer of Technology form is not valid for this project.");
        }

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == projectId && !item.IsDeleted, ct);
        if (project is null)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status404NotFound, "Project not found.");
        }

        if (ProjectTotApplicabilityPolicy.GetIneligibilityReason(project) is { } applicabilityError)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status409Conflict, applicabilityError);
        }

        if (!await access.CanManageAsync(project, ct))
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status403Forbidden, "You are not authorised to update Transfer of Technology for this project.");
        }

        var dates = ProjectTotDateInput.Resolve(input.Status, input.StartPartial(), input.CompletionPartial());
        if (!dates.IsValid)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status400BadRequest, dates.Errors[0].Message);
        }

        if (string.IsNullOrWhiteSpace(access.UserId))
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status403Forbidden, "Sign in again to update Transfer of Technology.");
        }

        var request = new ProjectTotUpdateRequest(
            input.Status,
            dates.StartedOn,
            dates.StartDatePrecision,
            dates.CompletedOn,
            dates.CompletionDatePrecision,
            input.MetDetails,
            input.MetCompletedOn,
            input.FirstProductionModelManufactured,
            input.FirstProductionModelManufacturedOn);

        var result = await new ProjectTotService(_db, _clock)
            .SaveFromProjectAsync(projectId, request, access.UserId, access.CanApprove, ct);
        if (result.Status == ProjectTotUpdateStatus.NotFound)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status404NotFound, "Project not found.");
        }

        if (result.Status == ProjectTotUpdateStatus.Conflict)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status409Conflict, result.ErrorMessage ?? "A request is waiting for a decision.");
        }

        if (!result.IsSuccess)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status400BadRequest, result.ErrorMessage ?? "Unable to update Transfer of Technology details.");
        }

        return ProjectTotDrawerResponse.Ok(new
        {
            success = true,
            submittedForApproval = result.SubmittedForApproval,
            message = result.SubmittedForApproval
                ? "Transfer of Technology update sent to HoD for approval."
                : "Transfer of Technology details updated.",
            card = ProjectTotPresentation.BuildCard(
                input.Status,
                request.StartedOn,
                request.StartDatePrecision,
                request.CompletedOn,
                request.CompletionDatePrecision,
                result.SubmittedForApproval ? input.Status : null,
                canManage: true)
        });
    }

    /// <summary>
    /// The HoD's decision: the register's rules (approver roles, the request's current version
    /// token, a reason to reject), applied by <see cref="ProjectTotService.DecideRequestAsync"/>.
    /// </summary>
    public async Task<ProjectTotDrawerResponse> DecideAsync(
        int projectId,
        bool approve,
        string? rowVersion,
        string? reason,
        ProjectTotDrawerAccess access,
        CancellationToken ct)
    {
        var exists = await _db.Projects.AsNoTracking().AnyAsync(item => item.Id == projectId && !item.IsDeleted, ct);
        if (!exists)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status404NotFound, "Project not found.");
        }

        if (!access.CanApprove)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status403Forbidden, "Only the HoD or an administrator can decide Transfer of Technology updates.");
        }

        if (string.IsNullOrWhiteSpace(access.UserId))
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status403Forbidden, "Sign in again to record the decision.");
        }

        byte[]? expectedVersion = null;
        if (!string.IsNullOrWhiteSpace(rowVersion))
        {
            try
            {
                expectedVersion = Convert.FromBase64String(rowVersion);
            }
            catch (FormatException)
            {
                expectedVersion = null;
            }
        }

        if (expectedVersion is null)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status400BadRequest, "This request could not be identified. Close the panel and open it again.");
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is { Length: > ProjectTotAuditActions.MaxReasonLength })
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status400BadRequest, $"The reason must be {ProjectTotAuditActions.MaxReasonLength} characters or fewer.");
        }

        var result = await new ProjectTotService(_db, _clock).DecideRequestAsync(
            projectId,
            approve,
            access.UserId,
            access.IsAdmin,
            access.IsHoD,
            expectedVersion,
            trimmedReason,
            ct);

        if (!result.IsSuccess)
        {
            var statusCode = result.Status switch
            {
                ProjectTotRequestActionStatus.NotFound => StatusCodes.Status404NotFound,
                ProjectTotRequestActionStatus.Forbidden => StatusCodes.Status403Forbidden,
                ProjectTotRequestActionStatus.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            var error = result.Status == ProjectTotRequestActionStatus.NotFound
                ? "No Transfer of Technology request is waiting for a decision on this project."
                : result.ErrorMessage ?? "Unable to record the decision.";
            return ProjectTotDrawerResponse.Error(statusCode, error);
        }

        var tot = await _db.ProjectTots.AsNoTracking().SingleOrDefaultAsync(item => item.ProjectId == projectId, ct);
        return ProjectTotDrawerResponse.Ok(new
        {
            success = true,
            message = approve ? "Transfer of Technology update approved." : "Transfer of Technology update rejected.",
            card = ProjectTotPresentation.BuildCard(
                tot?.Status,
                tot?.StartedOn,
                tot?.StartDatePrecision ?? PartialDatePrecision.None,
                tot?.CompletedOn,
                tot?.CompletionDatePrecision ?? PartialDatePrecision.None,
                null,
                canManage: true)
        });
    }

    /// <summary>
    /// Adds a ToT remark through <see cref="IRemarkService"/>, so it gets the remark's creation
    /// audit and notifications. The author's remark role follows the user's roles: the lead
    /// Project Officer counts as Project Officer for this project only; Project Office remarks
    /// are external, everyone else's internal.
    /// </summary>
    public async Task<ProjectTotDrawerResponse> AddRemarkAsync(
        int projectId,
        int postedProjectId,
        string? body,
        IEnumerable<string> userRoleNames,
        IRemarkService remarks,
        ProjectTotDrawerAccess access,
        CancellationToken ct)
    {
        if (projectId <= 0 || postedProjectId != projectId)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status400BadRequest, "The ToT remark form is not valid for this project.");
        }

        var project = await _db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == projectId && !item.IsDeleted, ct);
        if (project is null)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status404NotFound, "Project not found.");
        }

        if (ProjectTotApplicabilityPolicy.GetIneligibilityReason(project) is { } applicabilityError)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status409Conflict, applicabilityError);
        }

        if (!await access.CanManageAsync(project, ct))
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status403Forbidden, "You are not authorised to add a ToT remark for this project.");
        }

        var normalized = body?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 4 || normalized.Length > 2000)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status400BadRequest, "Remarks must contain between 4 and 2000 characters.");
        }

        if (string.IsNullOrWhiteSpace(access.UserId))
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status403Forbidden, "Sign in again to add a remark.");
        }

        var actor = ResolveRemarkActor(access.UserId, userRoleNames, project.LeadPoUserId);
        if (actor is null)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status403Forbidden, "You do not have permission to add remarks.");
        }

        try
        {
            await remarks.CreateRemarkAsync(
                new CreateRemarkRequest(
                    projectId,
                    actor,
                    actor.ActorRole is RemarkActorRole.ProjectOffice or RemarkActorRole.MainOffice ? RemarkType.External : RemarkType.Internal,
                    RemarkScope.TransferOfTechnology,
                    normalized,
                    DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow.UtcDateTime, TimeZoneHelper.GetIst())),
                    null,
                    null,
                    null),
                ct);
        }
        catch (InvalidOperationException ex)
        {
            return ProjectTotDrawerResponse.Error(StatusCodes.Status400BadRequest, ex.Message);
        }

        return ProjectTotDrawerResponse.Ok(new { success = true, message = "Transfer of Technology remark added." });
    }

    internal static RemarkActorContext? ResolveRemarkActor(string userId, IEnumerable<string> userRoleNames, string? leadPoUserId)
    {
        var roles = userRoleNames
            .Select(name => RemarkActorRoleExtensions.TryParse(name, out var parsed) ? parsed : RemarkActorRole.Unknown)
            .Where(role => role != RemarkActorRole.Unknown)
            .ToHashSet();

        if (!string.IsNullOrEmpty(leadPoUserId) && string.Equals(leadPoUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            roles.Add(RemarkActorRole.ProjectOfficer);
        }
        else
        {
            roles.Remove(RemarkActorRole.ProjectOfficer);
        }

        if (roles.Count == 0)
        {
            return null;
        }

        var primary = new[]
            {
                RemarkActorRole.Administrator,
                RemarkActorRole.HeadOfDepartment,
                RemarkActorRole.ProjectOffice,
                RemarkActorRole.MainOffice,
                RemarkActorRole.ProjectOfficer
            }
            .FirstOrDefault(roles.Contains, roles.First());
        return new RemarkActorContext(userId, primary, roles.ToList());
    }

    private static object BuildPendingPayload(ProjectTot? current, ProjectTotRequest pending, string? submittedBy, bool canDecide)
    {
        var change = ProjectTotRegisterFactsBuilder.BuildPendingChange(current, pending, submittedBy);
        return new
        {
            proposedStatus = pending.ProposedStatus.ToString(),
            proposedStatusLabel = ProjectTotPresentation.StatusLabel(pending.ProposedStatus),
            submittedBy = change.SubmittedBy ?? "Unknown",
            submittedOn = ProjectTotPresentation.FormatMoment(change.SubmittedOnUtc),
            changes = change.Changes.Select(item => new { field = item.Field, now = item.Now, proposed = item.Proposed }).ToArray(),
            rowVersion = Convert.ToBase64String(pending.RowVersion ?? Array.Empty<byte>()),
            canDecide
        };
    }

    private static object BuildHistoryPayload(ProjectTotHistoryEntry entry)
    {
        string? values = null;
        if (entry.Values is { } recorded)
        {
            values = recorded.StatusLabel
                + (recorded.Started is { } started ? $" · started {started}" : string.Empty)
                + (recorded.Completed is { } completed ? $" · completed {completed}" : string.Empty);
        }

        return new
        {
            label = entry.ActionLabel,
            by = entry.PerformedBy,
            on = ProjectTotPresentation.FormatMoment(entry.PerformedAt.UtcDateTime),
            values,
            reason = entry.Reason
        };
    }

    private static object BuildSummaryPayload(ProjectTot? tot)
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

        var facts = new List<object>();
        if (tot.StartedOn.HasValue) facts.Add(new { label = "Started on", value = ProjectTotPresentation.FormatDate(tot.StartedOn.Value, tot.StartDatePrecision) });
        if (tot.CompletedOn.HasValue) facts.Add(new { label = "Completed on", value = ProjectTotPresentation.FormatDate(tot.CompletedOn.Value, tot.CompletionDatePrecision) });
        if (!string.IsNullOrWhiteSpace(tot.MetDetails)) facts.Add(new { label = "MET details", value = tot.MetDetails });
        if (tot.MetCompletedOn.HasValue) facts.Add(new { label = "MET completed on", value = ProjectTotPresentation.FormatDay(tot.MetCompletedOn) });
        if (tot.FirstProductionModelManufactured.HasValue) facts.Add(new { label = "First production model", value = tot.FirstProductionModelManufactured.Value ? "Manufactured" : "Not manufactured" });
        if (tot.FirstProductionModelManufacturedOn.HasValue) facts.Add(new { label = "Manufactured on", value = ProjectTotPresentation.FormatDay(tot.FirstProductionModelManufacturedOn) });

        var summary = tot.Status switch
        {
            ProjectTotStatus.NotRequired => "Transfer of Technology is not required for this project.",
            ProjectTotStatus.NotStarted => "Transfer of Technology has not started.",
            ProjectTotStatus.InProgress => "Transfer of Technology is in progress.",
            ProjectTotStatus.Completed => "Transfer of Technology is completed.",
            ProjectTotStatus.NotKnown => "It is not known whether Transfer of Technology took place.",
            _ => "Transfer of Technology details are unavailable."
        };
        return new { hasRecord = true, statusLabel = ProjectTotPresentation.StatusLabel(tot.Status), summary, facts };
    }

    private async Task<object?> LoadLatestRemarkPayloadAsync(int projectId, CancellationToken ct)
    {
        var latest = await _db.Remarks
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId && !item.IsDeleted && item.Scope == RemarkScope.TransferOfTechnology)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Select(item => new { item.Body, item.Type, item.AuthorUserId, item.CreatedAtUtc })
            .FirstOrDefaultAsync(ct);
        if (latest is null)
        {
            return null;
        }

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

    private async Task<string?> DisplayNameAsync(string? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return await _db.Users.AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => item.FullName ?? item.UserName ?? item.Email ?? item.Id)
            .FirstOrDefaultAsync(ct) ?? userId;
    }

    private static PartialDatePrecision EffectivePrecision(PartialDatePrecision? precision)
        => precision is null or PartialDatePrecision.None ? PartialDatePrecision.Day : precision.Value;
}
