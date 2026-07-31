using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Workspace;

/// <summary>
/// Resolves the project scope for officer conference review without changing the meaning
/// of active workload. Completion precision is respected: exact dates, month/year and
/// year-only values are evaluated as intervals, while genuinely unknown dates use the
/// lifecycle audit timestamp as a bounded fallback.
/// </summary>
public sealed class ConferenceProjectScopeService : IConferenceProjectScopeService
{
    private static readonly string[] CompletionAuditActions =
    {
        "Project.LifecycleCompleted",
        "Project.LifecycleCompletionUpdated",
        "Project.LifecycleCompletionEndorsed"
    };

    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly ConferenceOptions _options;

    public ConferenceProjectScopeService(
        ApplicationDbContext db,
        IClock clock,
        IOptions<ConferenceOptions> options)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public int CompletedProjectRetentionDays => _options.CompletedProjectRetentionDays;

    public async Task<IReadOnlyList<ConferenceProjectCarryover>> GetRecentlyCompletedProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        var officers = await LoadActiveProjectOfficersAsync(cancellationToken);
        if (officers.Count == 0)
        {
            return Array.Empty<ConferenceProjectCarryover>();
        }

        var officerIds = officers.Keys.ToArray();
        var candidates = await _db.Projects
            .AsNoTracking()
            .Where(project =>
                !project.IsDeleted
                && !project.IsArchived
                && project.LifecycleStatus == ProjectLifecycleStatus.Completed
                && project.LeadPoUserId != null
                && officerIds.Contains(project.LeadPoUserId))
            .Select(project => new CompletedProjectCandidate(
                project.Id,
                project.Name,
                project.LeadPoUserId!,
                project.CompletedOn,
                project.CompletedYear,
                project.CompletedMonth))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return Array.Empty<ConferenceProjectCarryover>();
        }

        var today = IstToday();
        var unknownProjectIds = candidates
            .Where(candidate => !candidate.CompletedOn.HasValue && !candidate.CompletedYear.HasValue)
            .Select(candidate => candidate.ProjectId)
            .ToArray();
        var auditDates = await LoadRecentCompletionAuditDatesAsync(
            unknownProjectIds,
            today,
            cancellationToken);

        var result = new List<ConferenceProjectCarryover>();
        foreach (var candidate in candidates)
        {
            DateTime? auditDateUtc = auditDates.TryGetValue(candidate.ProjectId, out var recordedAtUtc)
                ? recordedAtUtc
                : null;
            if (!TryResolveCarryover(
                    candidate.CompletedOn,
                    candidate.CompletedYear,
                    candidate.CompletedMonth,
                    auditDateUtc,
                    today,
                    CompletedProjectRetentionDays,
                    out var sortDate))
            {
                continue;
            }

            var officer = officers[candidate.OfficerUserId];
            result.Add(new ConferenceProjectCarryover(
                candidate.ProjectId,
                candidate.ProjectName,
                candidate.OfficerUserId,
                officer.Name,
                officer.Rank,
                candidate.CompletedOn,
                candidate.CompletedYear,
                candidate.CompletedMonth,
                auditDateUtc,
                sortDate,
                FormatCompletionContext(
                    candidate.CompletedOn,
                    candidate.CompletedYear,
                    candidate.CompletedMonth)));
        }

        return result
            .OrderBy(carryover => carryover.OfficerName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(carryover => carryover.CompletionSortDate)
            .ThenBy(carryover => carryover.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<bool> IsProjectInScopeAsync(
        string officerUserId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(officerUserId) || projectId <= 0)
        {
            return false;
        }

        if (!await IsActiveProjectOfficerAsync(officerUserId, cancellationToken))
        {
            return false;
        }

        var project = await _db.Projects
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == projectId
                && !candidate.IsDeleted
                && !candidate.IsArchived
                && candidate.LeadPoUserId == officerUserId)
            .Select(candidate => new ProjectScopeCandidate(
                candidate.LifecycleStatus,
                candidate.CompletedOn,
                candidate.CompletedYear,
                candidate.CompletedMonth))
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return false;
        }

        if (project.LifecycleStatus == ProjectLifecycleStatus.Active)
        {
            return true;
        }

        if (project.LifecycleStatus != ProjectLifecycleStatus.Completed)
        {
            return false;
        }

        var today = IstToday();
        DateTime? auditDateUtc = null;
        if (!project.CompletedOn.HasValue && !project.CompletedYear.HasValue)
        {
            var auditDates = await LoadRecentCompletionAuditDatesAsync(
                new[] { projectId },
                today,
                cancellationToken);
            if (auditDates.TryGetValue(projectId, out var recordedAtUtc))
            {
                auditDateUtc = recordedAtUtc;
            }
        }

        return TryResolveCarryover(
            project.CompletedOn,
            project.CompletedYear,
            project.CompletedMonth,
            auditDateUtc,
            today,
            CompletedProjectRetentionDays,
            out _);
    }

    internal static bool TryResolveCarryover(
        DateOnly? completedOn,
        int? completedYear,
        short? completedMonth,
        DateTime? completionRecordedAtUtc,
        DateOnly today,
        int retentionDays,
        out DateOnly sortDate)
    {
        sortDate = default;
        if (retentionDays < 1)
        {
            return false;
        }

        var windowStart = today.AddDays(-retentionDays);
        DateOnly intervalStart;
        DateOnly intervalEnd;

        if (completedYear is < 1 or > 9999)
        {
            return false;
        }

        if (completedOn.HasValue)
        {
            intervalStart = completedOn.Value;
            intervalEnd = completedOn.Value;
        }
        else if (completedYear.HasValue && completedMonth is >= 1 and <= 12)
        {
            intervalStart = new DateOnly(completedYear.Value, completedMonth.Value, 1);
            intervalEnd = new DateOnly(
                completedYear.Value,
                completedMonth.Value,
                DateTime.DaysInMonth(completedYear.Value, completedMonth.Value));
        }
        else if (completedYear.HasValue)
        {
            intervalStart = new DateOnly(completedYear.Value, 1, 1);
            intervalEnd = new DateOnly(completedYear.Value, 12, 31);
        }
        else if (completionRecordedAtUtc.HasValue)
        {
            var auditDate = DateOnly.FromDateTime(IstClock.ToIst(
                DateTime.SpecifyKind(completionRecordedAtUtc.Value, DateTimeKind.Utc)));
            intervalStart = auditDate;
            intervalEnd = auditDate;
        }
        else
        {
            return false;
        }

        if (intervalStart > today || intervalEnd < windowStart)
        {
            return false;
        }

        sortDate = intervalEnd > today ? today : intervalEnd;
        return true;
    }

    internal static string FormatCompletionContext(
        DateOnly? completedOn,
        int? completedYear,
        short? completedMonth)
    {
        if (completedOn.HasValue)
        {
            return $"Completed on {completedOn.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}";
        }

        if (completedYear is >= 1 and <= 9999 && completedMonth is >= 1 and <= 12)
        {
            var month = new DateOnly(completedYear.Value, completedMonth.Value, 1);
            return $"Completed in {month.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";
        }

        return completedYear is >= 1 and <= 9999
            ? $"Completed in {completedYear.Value.ToString(CultureInfo.InvariantCulture)}"
            : "Recently completed";
    }

    private DateOnly IstToday()
        => DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow.UtcDateTime));

    private async Task<Dictionary<string, OfficerIdentity>> LoadActiveProjectOfficersAsync(
        CancellationToken cancellationToken)
    {
        var normalizedRoleName = RoleNames.ProjectOfficer.ToUpperInvariant();
        var roleId = await _db.Roles
            .AsNoTracking()
            .Where(role => role.NormalizedName == normalizedRoleName)
            .Select(role => role.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(roleId))
        {
            return new Dictionary<string, OfficerIdentity>(StringComparer.Ordinal);
        }

        var rows = await (
                from userRole in _db.UserRoles.AsNoTracking()
                join user in _db.Users.AsNoTracking() on userRole.UserId equals user.Id
                where userRole.RoleId == roleId
                    && !user.IsDisabled
                    && !user.PendingDeletion
                select new
                {
                    user.Id,
                    user.FullName,
                    user.UserName,
                    user.Rank
                })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.Id,
            row => new OfficerIdentity(
                string.IsNullOrWhiteSpace(row.FullName)
                    ? row.UserName ?? "Project Officer"
                    : row.FullName,
                row.Rank ?? string.Empty),
            StringComparer.Ordinal);
    }

    private async Task<bool> IsActiveProjectOfficerAsync(
        string officerUserId,
        CancellationToken cancellationToken)
    {
        var normalizedRoleName = RoleNames.ProjectOfficer.ToUpperInvariant();
        return await (
                from userRole in _db.UserRoles.AsNoTracking()
                join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                join user in _db.Users.AsNoTracking() on userRole.UserId equals user.Id
                where user.Id == officerUserId
                    && role.NormalizedName == normalizedRoleName
                    && !user.IsDisabled
                    && !user.PendingDeletion
                select user.Id)
            .AnyAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<int, DateTime>> LoadRecentCompletionAuditDatesAsync(
        int[] projectIds,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (projectIds.Length == 0)
        {
            return new Dictionary<int, DateTime>();
        }

        var projectIdSet = projectIds.ToHashSet();
        var windowStart = today.AddDays(-CompletedProjectRetentionDays);
        var windowStartUtc = new DateTime(
            windowStart.Year,
            windowStart.Month,
            windowStart.Day,
            0,
            0,
            0,
            DateTimeKind.Utc).AddHours(-5).AddMinutes(-30);

        var rows = await _db.AuditLogs
            .AsNoTracking()
            .Where(log =>
                CompletionAuditActions.Contains(log.Action)
                && log.TimeUtc >= windowStartUtc
                && log.DataJson != null)
            .Select(log => new { log.Action, log.TimeUtc, log.DataJson })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<int, DateTime>();
        foreach (var row in rows)
        {
            if (!TryReadCompletionAudit(row.Action, row.DataJson!, out var projectId)
                || !projectIdSet.Contains(projectId))
            {
                continue;
            }

            if (!result.TryGetValue(projectId, out var current) || row.TimeUtc > current)
            {
                // The latest transition supports projects that were reactivated and later
                // completed again. Metadata-only edits of an already completed project are
                // filtered by TryReadCompletionAudit and cannot restart the carryover window.
                result[projectId] = DateTime.SpecifyKind(row.TimeUtc, DateTimeKind.Utc);
            }
        }

        return result;
    }

    private static bool TryReadCompletionAudit(
        string action,
        string dataJson,
        out int projectId)
    {
        projectId = 0;
        try
        {
            using var document = JsonDocument.Parse(dataJson);
            if (!document.RootElement.TryGetProperty("ProjectId", out var projectIdElement))
            {
                return false;
            }

            var projectIdText = projectIdElement.ValueKind == JsonValueKind.String
                ? projectIdElement.GetString()
                : projectIdElement.GetRawText();
            if (!int.TryParse(projectIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out projectId))
            {
                return false;
            }

            if (string.Equals(action, "Project.LifecycleCompletionUpdated", StringComparison.Ordinal)
                && document.RootElement.TryGetProperty("PreviousStatus", out var previousStatus)
                && string.Equals(
                    previousStatus.GetString(),
                    ProjectLifecycleStatus.Completed.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                // Editing completion metadata for an already completed project must not
                // restart the conference carryover period.
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record OfficerIdentity(string Name, string Rank);

    private sealed record CompletedProjectCandidate(
        int ProjectId,
        string ProjectName,
        string OfficerUserId,
        DateOnly? CompletedOn,
        int? CompletedYear,
        short? CompletedMonth);

    private sealed record ProjectScopeCandidate(
        ProjectLifecycleStatus LifecycleStatus,
        DateOnly? CompletedOn,
        int? CompletedYear,
        short? CompletedMonth);
}
