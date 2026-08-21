using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Infrastructure;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationSummaryReadService : IProliferationSummaryReadService
{
    private const string YearlyRecordedAction = "ProjectOfficeReports.Proliferation.YearlyRecorded";
    private const string GranularRecordedAction = "ProjectOfficeReports.Proliferation.GranularRecorded";

    private static readonly string[] MaintenanceAuditActions =
    {
        YearlyRecordedAction,
        GranularRecordedAction,
        "ProjectOfficeReports.ProliferationYearlySubmitted",
        "ProjectOfficeReports.ProliferationYearlyDecided",
        "ProjectOfficeReports.ProliferationGranularDecided",
        "ProjectOfficeReports.Proliferation.DataQualityCorrected",
        "ProjectOfficeReports.Proliferation.PreferenceChanged"
    };

    private static readonly string[] RecordEntryAuditActions =
    {
        YearlyRecordedAction,
        GranularRecordedAction
    };

    private static readonly TimeSpan ActivityCollapseWindow = TimeSpan.FromMinutes(10);

    private readonly ProliferationAggregateReadService _aggregateReadService;
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;

    public ProliferationSummaryReadService(
        ProliferationAggregateReadService aggregateReadService,
        ApplicationDbContext db,
        IClock clock)
    {
        _aggregateReadService = aggregateReadService ?? throw new ArgumentNullException(nameof(aggregateReadService));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ProliferationSummaryViewModel> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var aggregates = await _aggregateReadService.GetApprovedAggregatesAsync(
            projectId: null,
            cancellationToken);

        var active = aggregates
            .Where(x => x.ReportedTotal > 0)
            .ToList();

        if (active.Count == 0)
        {
            return ProliferationSummaryViewModel.Empty;
        }

        var byProject = active
            .GroupBy(x => new { x.ProjectId, x.ProjectName, x.ProjectCode })
            .Select(group => new ProliferationSummaryProjectRow(
                group.Key.ProjectId,
                group.Key.ProjectName,
                group.Key.ProjectCode,
                BuildSourceTotals(group)))
            .OrderByDescending(x => x.Totals.Total)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ProjectCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var maximumChronologicalYear = DateTime.UtcNow.Year + 1;
        var chronological = active
            .Where(x => x.Year is >= 2000 && x.Year <= maximumChronologicalYear)
            .ToList();

        var byYear = chronological
            .GroupBy(x => x.Year)
            .Select(group => new ProliferationSummaryYearRow(
                group.Key,
                BuildSourceTotals(group)))
            .OrderByDescending(x => x.Year)
            .ToList();

        var byProjectYear = chronological
            .GroupBy(x => new { x.ProjectId, x.ProjectName, x.ProjectCode, x.Year })
            .Select(group => new ProliferationSummaryProjectYearRow(
                group.Key.ProjectId,
                group.Key.ProjectName,
                group.Key.ProjectCode,
                group.Key.Year,
                BuildSourceTotals(group)))
            .OrderByDescending(x => x.Year)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ProjectCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProliferationSummaryViewModel(byProject, byYear, byProjectYear);
    }


    public async Task<ProliferationOperationalSnapshot> GetOperationalSnapshotAsync(
        int recentProliferationLimit,
        int recentActivityLimit,
        CancellationToken cancellationToken)
    {
        var proliferationLimit = Math.Clamp(recentProliferationLimit, 1, 25);
        var activityLimit = Math.Clamp(recentActivityLimit, 1, 20);
        var todayIst = DateOnly.FromDateTime(IstClock.ToIst(_clock.UtcNow).DateTime);

        // The overview presents business events, not raw detail rows. Multiple unit-level
        // entries for the same project/source/date are therefore collapsed into one event.
        var recentGranularGroups = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .Where(x =>
                x.ApprovalStatus == ApprovalStatus.Approved &&
                x.Quantity > 0 &&
                x.ProliferationDate <= todayIst)
            .GroupBy(x => new
            {
                x.ProjectId,
                x.Source,
                x.ProliferationDate
            })
            .Select(group => new
            {
                group.Key.ProjectId,
                group.Key.Source,
                group.Key.ProliferationDate,
                Quantity = group.Sum(x => x.Quantity),
                RecordCount = group.Count(),
                ReceivingUnitCount = group
                    .Where(x => x.UnitName != null && x.UnitName != string.Empty)
                    .Select(x => x.UnitName)
                    .Distinct()
                    .Count(),
                UnitName = group
                    .Where(x => x.UnitName != null && x.UnitName != string.Empty)
                    .Select(x => x.UnitName)
                    .Min(),
                CreatedOnUtc = group.Min(x => x.CreatedOnUtc),
                LastUpdatedOnUtc = group.Max(x => x.LastUpdatedOnUtc)
            })
            .OrderByDescending(x => x.ProliferationDate)
            .ThenByDescending(x => x.LastUpdatedOnUtc)
            .Take(proliferationLimit * 2)
            .ToListAsync(cancellationToken);

        var recentGranular = recentGranularGroups
            .Select(x => new RecentRecordProjection(
                ProliferationRecordKind.Granular,
                x.ProjectId,
                x.Source,
                x.ProliferationDate,
                x.ProliferationDate.Year,
                x.UnitName,
                x.Quantity,
                x.RecordCount,
                x.ReceivingUnitCount,
                x.CreatedOnUtc,
                x.LastUpdatedOnUtc))
            .ToList();

        var recentYearly = await _db.ProliferationYearlies
            .AsNoTracking()
            .Where(x =>
                x.ApprovalStatus == ApprovalStatus.Approved &&
                x.TotalQuantity > 0 &&
                x.Year >= 2000 &&
                x.Year <= todayIst.Year)
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.LastUpdatedOnUtc)
            .Take(proliferationLimit * 2)
            .Select(x => new RecentRecordProjection(
                ProliferationRecordKind.Yearly,
                x.ProjectId,
                x.Source,
                null,
                x.Year,
                null,
                x.TotalQuantity,
                1,
                0,
                x.CreatedOnUtc,
                x.LastUpdatedOnUtc))
            .ToListAsync(cancellationToken);

        var recentRecords = recentGranular
            .Concat(recentYearly)
            .OrderByDescending(BusinessSortKey)
            .ThenByDescending(x => x.Kind == ProliferationRecordKind.Granular)
            .ThenByDescending(x => x.LastUpdatedOnUtc)
            .Take(proliferationLimit)
            .ToList();

        // Fetch more than the display limit because maintenance bursts are condensed for
        // presentation (for example, several deletions by one user in the same project).
        var recentAudits = await _db.AuditLogs
            .AsNoTracking()
            .Where(x => MaintenanceAuditActions.Contains(x.Action))
            .OrderByDescending(x => x.TimeUtc)
            .Take(Math.Max(activityLimit * 12, 60))
            .ToListAsync(cancellationToken);

        var thirtyDayCutoffUtc = _clock.UtcNow.UtcDateTime.AddDays(-30);
        var actionsLast30Days = await _db.AuditLogs
            .AsNoTracking()
            .CountAsync(
                x => x.TimeUtc >= thirtyDayCutoffUtc && MaintenanceAuditActions.Contains(x.Action),
                cancellationToken);

        var activeStaffLast30Days = await _db.AuditLogs
            .AsNoTracking()
            .Where(x =>
                x.TimeUtc >= thirtyDayCutoffUtc &&
                x.UserId != null &&
                MaintenanceAuditActions.Contains(x.Action))
            .Select(x => x.UserId!)
            .Distinct()
            .CountAsync(cancellationToken);

        // This is deliberately narrower than "register activity": approvals, deletes,
        // counting-rule changes and exports do not make the underlying proliferation data
        // look freshly entered.
        var latestDataEntryUtc = await _db.AuditLogs
            .AsNoTracking()
            .Where(x =>
                RecordEntryAuditActions.Contains(x.Action) &&
                x.DataJson != null &&
                (x.DataJson.Contains("\"Action\":\"Create\"") ||
                 x.DataJson.Contains("\"Action\":\"Update\"")))
            .OrderByDescending(x => x.TimeUtc)
            .Select(x => (DateTime?)x.TimeUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var activityDrafts = recentAudits
            .Select(ParseActivity)
            .Where(x => x is not null)
            .Cast<ActivityDraft>()
            .ToList();

        var groupedActivityDrafts = CollapseActivityDrafts(activityDrafts, activityLimit);

        var projectIds = recentRecords
            .Select(x => x.ProjectId)
            .Concat(groupedActivityDrafts.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId!.Value))
            .Distinct()
            .ToArray();

        var projectLookup = projectIds.Length == 0
            ? new Dictionary<int, ProjectIdentity>()
            : await _db.Projects
                .AsNoTracking()
                .Where(x => projectIds.Contains(x.Id) && !x.IsDeleted && !x.IsArchived)
                .Select(x => new ProjectIdentity(x.Id, x.Name, x.CaseFileNumber))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var userIds = recentAudits
            .Where(x => !string.IsNullOrWhiteSpace(x.UserId))
            .Select(x => x.UserId!)
            .Distinct()
            .ToArray();

        var userRows = userIds.Length == 0
            ? new List<UserIdentity>()
            : await _db.Users
                .AsNoTracking()
                .Where(x => userIds.Contains(x.Id))
                .Select(x => new UserIdentity(x.Id, x.FullName, x.UserName))
                .ToListAsync(cancellationToken);

        var userLookup = userRows.ToDictionary(
            x => x.Id,
            x => !string.IsNullOrWhiteSpace(x.FullName)
                ? x.FullName!
                : x.UserName ?? x.Id,
            StringComparer.OrdinalIgnoreCase);

        var recentProliferation = recentRecords
            .Where(x => projectLookup.ContainsKey(x.ProjectId))
            .Select(x =>
            {
                var project = projectLookup[x.ProjectId];
                return new RecentProliferationRow(
                    x.Kind,
                    x.ProjectId,
                    project.Name,
                    project.Code,
                    x.Source,
                    x.ProliferationDate,
                    x.Year,
                    x.UnitName,
                    x.Quantity,
                    x.RecordCount,
                    x.ReceivingUnitCount,
                    x.CreatedOnUtc,
                    x.LastUpdatedOnUtc,
                    CalculateEntryDelayDays(x));
            })
            .ToList();

        var auditLookup = recentAudits.ToDictionary(x => x.Id);
        var recentActivity = groupedActivityDrafts
            .Select(x =>
            {
                ProjectIdentity? project = null;
                if (x.ProjectId.HasValue)
                {
                    projectLookup.TryGetValue(x.ProjectId.Value, out project);
                }

                auditLookup.TryGetValue(x.AuditId, out var audit);
                var actor = audit is null ? null : ResolveActorDisplayName(audit, userLookup);

                return new ProliferationStaffActivityRow(
                    x.AuditId,
                    x.TimeUtc,
                    BuildCollapsedActionLabel(x.ActionLabel, x.ActionCount),
                    x.ActionCount,
                    actor,
                    x.ProjectId,
                    project?.Name,
                    project?.Code,
                    x.SourceLabel,
                    x.ActionCount > 1 ? x.SourceLabel : x.RecordReference);
            })
            .ToList();

        return new ProliferationOperationalSnapshot(
            recentProliferation,
            new ProliferationStaffActivitySummary(
                recentAudits.FirstOrDefault()?.TimeUtc,
                latestDataEntryUtc,
                actionsLast30Days,
                activeStaffLast30Days,
                recentActivity));
    }

    private static IReadOnlyList<GroupedActivityDraft> CollapseActivityDrafts(
        IReadOnlyList<ActivityDraft> drafts,
        int limit)
    {
        var groups = new List<ActivityAccumulator>();

        foreach (var draft in drafts.OrderByDescending(x => x.TimeUtc))
        {
            var existing = groups.FirstOrDefault(group =>
                group.Matches(draft) &&
                group.LatestTimeUtc - draft.TimeUtc <= ActivityCollapseWindow);

            if (existing is null)
            {
                groups.Add(new ActivityAccumulator(draft));
                continue;
            }

            existing.Add(draft);
        }

        return groups
            .OrderByDescending(x => x.LatestTimeUtc)
            .Take(limit)
            .Select(x => x.ToDraft())
            .ToList();
    }

    private static string BuildCollapsedActionLabel(string actionLabel, int count)
    {
        if (count <= 1)
        {
            return actionLabel;
        }

        return actionLabel switch
        {
            "Added detailed entry" => $"Added {count:N0} detailed entries",
            "Updated detailed entry" => $"Updated {count:N0} detailed entries",
            "Deleted detailed entry" => $"Deleted {count:N0} detailed entries",
            "Maintained detailed entry" => $"Maintained {count:N0} detailed entries",
            "Added annual quantity" => $"Added {count:N0} annual quantities",
            "Updated annual quantity" => $"Updated {count:N0} annual quantities",
            "Deleted annual quantity" => $"Deleted {count:N0} annual quantities",
            "Maintained annual quantity" => $"Maintained {count:N0} annual quantities",
            "Submitted annual quantity" => $"Submitted {count:N0} annual quantities",
            "Approved annual quantity" => $"Approved {count:N0} annual quantities",
            "Rejected annual quantity" => $"Rejected {count:N0} annual quantities",
            "Approved detailed entry" => $"Approved {count:N0} detailed entries",
            "Rejected detailed entry" => $"Rejected {count:N0} detailed entries",
            "Corrected proliferation data" => $"Corrected proliferation data ({count:N0} actions)",
            "Changed counting rule" => $"Changed {count:N0} counting rules",
            _ => $"{actionLabel} ({count:N0} actions)"
        };
    }

    private static long BusinessSortKey(RecentRecordProjection row)
    {
        if (row.ProliferationDate.HasValue)
        {
            var date = row.ProliferationDate.Value;
            return (date.Year * 10_000L) + (date.Month * 100L) + date.Day;
        }

        return (row.Year * 10_000L) + 101L;
    }

    private static int? CalculateEntryDelayDays(RecentRecordProjection row)
    {
        if (!row.ProliferationDate.HasValue)
        {
            return null;
        }

        var createdIst = DateOnly.FromDateTime(IstClock.ToIst(row.CreatedOnUtc));
        var delay = createdIst.DayNumber - row.ProliferationDate.Value.DayNumber;
        return delay >= 30 ? delay : null;
    }

    private static string? ResolveActorDisplayName(
        AuditLog audit,
        IReadOnlyDictionary<string, string> userLookup)
    {
        if (!string.IsNullOrWhiteSpace(audit.UserId) &&
            userLookup.TryGetValue(audit.UserId, out var displayName))
        {
            return displayName;
        }

        return string.IsNullOrWhiteSpace(audit.UserName) ? null : audit.UserName;
    }

    private static ActivityDraft? ParseActivity(AuditLog audit)
    {
        var data = ParseAuditData(audit.DataJson);
        var projectId = ParseInt(Get(data, "ProjectId"));
        var source = Get(data, "Source");
        var reference = BuildRecordReference(audit.Action, data);
        var actionLabel = BuildActionLabel(audit.Action, data);

        if (string.IsNullOrWhiteSpace(actionLabel))
        {
            return null;
        }

        var actorKey = !string.IsNullOrWhiteSpace(audit.UserId)
            ? $"id:{audit.UserId}"
            : $"name:{audit.UserName ?? "system"}";

        return new ActivityDraft(
            audit.Id,
            audit.TimeUtc,
            actionLabel,
            actorKey,
            projectId,
            source,
            reference);
    }

    private static string BuildActionLabel(
        string action,
        IReadOnlyDictionary<string, string?> data)
    {
        if (action == YearlyRecordedAction)
        {
            return Get(data, "Action")?.ToLowerInvariant() switch
            {
                "create" => "Added annual quantity",
                "update" => "Updated annual quantity",
                "delete" => "Deleted annual quantity",
                _ => "Maintained annual quantity"
            };
        }

        if (action == GranularRecordedAction)
        {
            return Get(data, "Action")?.ToLowerInvariant() switch
            {
                "create" => "Added detailed entry",
                "update" => "Updated detailed entry",
                "delete" => "Deleted detailed entry",
                _ => "Maintained detailed entry"
            };
        }

        if (action == "ProjectOfficeReports.ProliferationYearlySubmitted")
        {
            return "Submitted annual quantity";
        }

        if (action == "ProjectOfficeReports.ProliferationYearlyDecided")
        {
            return IsTrue(Get(data, "Approved"))
                ? "Approved annual quantity"
                : "Rejected annual quantity";
        }

        if (action == "ProjectOfficeReports.ProliferationGranularDecided")
        {
            return IsTrue(Get(data, "Approved"))
                ? "Approved detailed entry"
                : "Rejected detailed entry";
        }

        if (action == "ProjectOfficeReports.Proliferation.DataQualityCorrected")
        {
            return "Corrected proliferation data";
        }

        if (action == "ProjectOfficeReports.Proliferation.PreferenceChanged")
        {
            return "Changed counting rule";
        }

        return string.Empty;
    }

    private static string? BuildRecordReference(
        string action,
        IReadOnlyDictionary<string, string?> data)
    {
        var source = Get(data, "Source");
        var year = Get(data, "Year");
        var date = Get(data, "ProliferationDate");
        var unit = Get(data, "UnitName");
        var parts = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(source))
        {
            parts.Add(source);
        }

        if (!string.IsNullOrWhiteSpace(date))
        {
            if (DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                parts.Add(parsedDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
            }
            else
            {
                parts.Add(date);
            }
        }
        else if (!string.IsNullOrWhiteSpace(year))
        {
            parts.Add(year);
        }

        if (!string.IsNullOrWhiteSpace(unit) &&
            action.Contains("Granular", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(unit);
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static IReadOnlyDictionary<string, string?> ParseAuditData(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return EmptyAuditData.Instance;
        }

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return EmptyAuditData.Instance;
            }

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => property.Value.GetString(),
                    _ => property.Value.ToString()
                };
            }

            return values;
        }
        catch (JsonException)
        {
            return EmptyAuditData.Instance;
        }
    }

    private static string? Get(
        IReadOnlyDictionary<string, string?> data,
        string key) =>
        data.TryGetValue(key, out var value) ? value : null;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static ProliferationSummarySourceTotals BuildSourceTotals(
        IEnumerable<ProliferationAggregateRow> rows)
    {
        var sdd = 0;
        var abw = 0;

        foreach (var row in rows)
        {
            if (row.Source == ProliferationSource.Sdd)
            {
                sdd = checked(sdd + row.ReportedTotal);
            }
            else if (row.Source == ProliferationSource.Abw515)
            {
                abw = checked(abw + row.ReportedTotal);
            }
        }

        return new ProliferationSummarySourceTotals(
            checked(sdd + abw),
            sdd,
            abw);
    }
    private sealed record RecentRecordProjection(
        ProliferationRecordKind Kind,
        int ProjectId,
        ProliferationSource Source,
        DateOnly? ProliferationDate,
        int Year,
        string? UnitName,
        int Quantity,
        int RecordCount,
        int ReceivingUnitCount,
        DateTime CreatedOnUtc,
        DateTime LastUpdatedOnUtc);

    private sealed record ProjectIdentity(
        int Id,
        string Name,
        string? Code);

    private sealed record UserIdentity(
        string Id,
        string? FullName,
        string? UserName);

    private sealed record ActivityDraft(
        long AuditId,
        DateTime TimeUtc,
        string ActionLabel,
        string ActorKey,
        int? ProjectId,
        string? SourceLabel,
        string? RecordReference);

    private sealed record GroupedActivityDraft(
        long AuditId,
        DateTime TimeUtc,
        string ActionLabel,
        int ActionCount,
        int? ProjectId,
        string? SourceLabel,
        string? RecordReference);

    private sealed class ActivityAccumulator
    {
        private readonly string _actionLabel;
        private readonly string _actorKey;
        private readonly int? _projectId;
        private readonly string? _sourceLabel;
        private string? _recordReference;
        private int _count;

        public ActivityAccumulator(ActivityDraft first)
        {
            AuditId = first.AuditId;
            LatestTimeUtc = first.TimeUtc;
            _actionLabel = first.ActionLabel;
            _actorKey = first.ActorKey;
            _projectId = first.ProjectId;
            _sourceLabel = first.SourceLabel;
            _recordReference = first.RecordReference;
            _count = 1;
        }

        public long AuditId { get; }
        public DateTime LatestTimeUtc { get; }

        public bool Matches(ActivityDraft draft) =>
            string.Equals(_actionLabel, draft.ActionLabel, StringComparison.Ordinal) &&
            string.Equals(_actorKey, draft.ActorKey, StringComparison.OrdinalIgnoreCase) &&
            _projectId == draft.ProjectId &&
            string.Equals(_sourceLabel, draft.SourceLabel, StringComparison.OrdinalIgnoreCase);

        public void Add(ActivityDraft draft)
        {
            _count++;
            if (!string.Equals(_recordReference, draft.RecordReference, StringComparison.OrdinalIgnoreCase))
            {
                _recordReference = _sourceLabel;
            }
        }

        public GroupedActivityDraft ToDraft() => new(
            AuditId,
            LatestTimeUtc,
            _actionLabel,
            _count,
            _projectId,
            _sourceLabel,
            _recordReference);
    }

    private sealed class EmptyAuditData : Dictionary<string, string?>
    {
        public static EmptyAuditData Instance { get; } = new();

        private EmptyAuditData()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }
    }

}
