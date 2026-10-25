using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Areas.ProjectOfficeReports.ViewModels;
using ProjectManagement.Infrastructure.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Ipr;

public sealed partial class IndexModel
{
    private const int LongPendingYears = 3;

    public string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double size = bytes / 1024d;
        string[] units = { "KB", "MB", "GB", "TB", "PB" };
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", size, units[unitIndex]);
    }

    private IprRecordRowViewModel CreateRowViewModel(IprListRowDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var title = string.IsNullOrWhiteSpace(dto.Title) ? "Untitled record" : dto.Title!;
        var project = string.IsNullOrWhiteSpace(dto.ProjectName) ? "Unassigned project" : dto.ProjectName!;
        var applicationNumber = string.IsNullOrWhiteSpace(dto.FilingNumber) ? "—" : dto.FilingNumber;
        var attachments = dto.Attachments
            .Select(CreateAttachmentViewModel)
            .ToList();
        var status = IprStatusPolicy.Normalize(dto.Status);
        var filedOn = ConvertToIstDate(dto.FiledAtUtc);
        var grantedOn = ConvertToIstDate(dto.GrantedAtUtc);
        var isPending = status == IprStatus.Filed;
        var todayIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IstTimeZone).Date;
        int? pendingDays = isPending && filedOn.HasValue
            ? Math.Max(0, (todayIst - filedOn.Value.Date).Days)
            : null;
        var pendingAge = pendingDays.HasValue ? FormatAge(pendingDays.Value) : null;

        return new IprRecordRowViewModel(
            dto.Id,
            dto.ProjectId,
            title,
            project,
            GetTypeLabel(dto.Type),
            applicationNumber,
            GetStatusLabel(dto.Status, dto.Type),
            GetStatusChipClass(dto.Status),
            string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes,
            string.IsNullOrWhiteSpace(dto.FiledBy) ? "Not recorded" : dto.FiledBy.Trim(),
            filedOn,
            grantedOn,
            attachments,
            dto.AttachmentCount,
            status.ToString(),
            dto.Type.ToString(),
            IprStatusPolicy.IsCounted(status),
            IprStatusPolicy.IsClosed(status),
            isPending,
            pendingAge,
            filedOn?.Year,
            grantedOn?.Year,
            pendingDays);
    }

    private IprRecordAttachmentViewModel CreateAttachmentViewModel(IprListAttachmentDto attachment)
    {
        var uploadedAt = FormatAttachmentTimestamp(attachment.UploadedAtUtc);
        return new IprRecordAttachmentViewModel(
            attachment.Id,
            attachment.FileName,
            FormatFileSize(attachment.FileSize),
            attachment.UploadedBy,
            uploadedAt);
    }

    private static DateTime? ConvertToIstDate(DateTimeOffset? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return TimeZoneInfo.ConvertTimeFromUtc(value.Value.UtcDateTime, IstTimeZone);
    }

    public string FormatAttachmentTimestamp(DateTimeOffset value)
    {
        var converted = TimeZoneInfo.ConvertTimeFromUtc(value.UtcDateTime, IstTimeZone);
        return converted.ToString("dd MMM yyyy 'at' hh:mm tt", CultureInfo.InvariantCulture);
    }

    private static string GetStatusChipClass(IprStatus status)
        => status switch
        {
            IprStatus.Granted => "text-success border-success",
            IprStatus.Rejected => "text-danger border-danger",
            IprStatus.FilingUnderProcess => "text-primary border-primary",
            IprStatus.Withdrawn => "border-secondary text-secondary",
            _ => string.Empty
        };

    private IReadOnlyList<ActiveFilterChip> BuildActiveFilterChips()
    {
        var chips = new List<ActiveFilterChip>();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            chips.Add(new ActiveFilterChip("query", $"Search: “{Query}”"));
        }

        foreach (var type in Types)
        {
            chips.Add(new ActiveFilterChip("type", GetTypeLabel(type), type.ToString()));
        }

        foreach (var status in Statuses)
        {
            chips.Add(new ActiveFilterChip("status", GetStatusLabel(status), status.ToString()));
        }

        if (ProjectId.HasValue)
        {
            var projectValue = ProjectId.Value.ToString(CultureInfo.InvariantCulture);
            var projectLabel = ProjectOptions.FirstOrDefault(option => option.Value == projectValue)?.Text;
            if (!string.IsNullOrWhiteSpace(projectLabel) && !string.Equals(projectLabel, "All projects", StringComparison.Ordinal))
            {
                chips.Add(new ActiveFilterChip("project", projectLabel));
            }
        }

        if (Year.HasValue)
        {
            var basis = DateBasis == IprDateBasis.Protected
                ? "Grant / registration year"
                : "Filed year";
            chips.Add(new ActiveFilterChip("year", $"{basis}: {Year.Value.ToString(CultureInfo.InvariantCulture)}"));
        }

        if (Linkage != IprLinkageFilter.All)
        {
            chips.Add(new ActiveFilterChip(
                "linkage",
                Linkage == IprLinkageFilter.Linked ? "Linked to a project" : "Unassigned"));
        }

        if (Evidence != IprEvidenceFilter.All)
        {
            chips.Add(new ActiveFilterChip(
                "evidence",
                Evidence == IprEvidenceFilter.Available ? "Evidence available" : "Evidence missing"));
        }

        if (PendingAge != IprPendingAge.Any)
        {
            chips.Add(new ActiveFilterChip("pendingAge", GetPendingAgeLabel(PendingAge)));
        }

        return chips;
    }

    private async Task LoadPageAsync(CancellationToken cancellationToken, bool loadRecordInput)
    {
        NormalizePaging();
        await LoadRegisterOverviewAsync(cancellationToken);

        switch (Tab)
        {
            case "followup":
                await LoadAttentionAsync(cancellationToken);
                break;
            case "analytics":
                await LoadAnalyticsAsync(cancellationToken);
                break;
            default:
                await LoadRecordsAsync(cancellationToken);
                ResolveSelectedRecord();
                break;
        }

        if (string.Equals(Mode, "edit", StringComparison.OrdinalIgnoreCase) && Id.HasValue && CanEdit)
        {
            var record = await LoadRecordAsync(Id.Value, cancellationToken, loadRecordInput);
            if (record is null)
            {
                TempData["ToastError"] = "The selected IPR record could not be found.";
                Mode = null;
                Id = null;
                Attachments = Array.Empty<AttachmentViewModel>();
            }
        }
        else
        {
            Attachments = Array.Empty<AttachmentViewModel>();
            if (loadRecordInput && string.Equals(Mode, "create", StringComparison.OrdinalIgnoreCase))
            {
                Input = new RecordInput
                {
                    Type = IprType.Patent,
                    Status = IprStatus.Filed
                };
            }
        }

        await PopulateSelectListsAsync(cancellationToken);
        ActiveFilterChips = BuildActiveFilterChips();
    }

    private async Task LoadRecordsAsync(CancellationToken cancellationToken)
    {
        // The register lists every record; the browser applies the filters instantly.
        var rows = await _readService.ListRegisterAsync(cancellationToken);
        Records = rows.Select(CreateRowViewModel).ToList();
        TotalCount = Records.Count;
        TotalPages = TotalCount == 0 ? 0 : 1;
        PageNumber = 1;
    }

    private void ResolveSelectedRecord()
    {
        if (!SelectedRecordId.HasValue)
        {
            return;
        }

        var selected = Records.FirstOrDefault(record => record.Id == SelectedRecordId.Value);
        if (selected is null)
        {
            SelectedRecordId = null;
            return;
        }

        // A link to a closed record (from search or the editor) arrives without a status filter,
        // and the default view lists only the positive count. Show that record's status instead.
        if (Statuses.Count == 0 &&
            selected.IsClosed &&
            Enum.TryParse<IprStatus>(selected.StatusKey, out var status))
        {
            Statuses = new List<IprStatus> { status };
        }
    }

    // The summary strip always describes the whole register, whatever the view's filters.
    private async Task LoadRegisterOverviewAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _db.IprRecords
            .AsNoTracking()
            .Where(IprStatusPolicy.CountedPredicate)
            .Select(record => new
            {
                record.Id,
                record.Type,
                Status = record.Status == IprStatus.FilingUnderProcess ? IprStatus.Filed : record.Status,
                record.ProjectId,
                record.IprFilingNumber,
                record.FiledAtUtc,
                record.GrantedAtUtc,
                AttachmentCount = record.Attachments.Count(attachment => !attachment.IsArchived)
            })
            .ToListAsync(cancellationToken);

        var granted = snapshot.Count(item => item.Status == IprStatus.Granted);
        var awaiting = snapshot.Count(item => item.Status == IprStatus.Filed);

        var closedCounts = await _db.IprRecords
            .AsNoTracking()
            .Where(IprStatusPolicy.ClosedPredicate)
            .GroupBy(record => record.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var rejected = closedCounts.FirstOrDefault(item => item.Status == IprStatus.Rejected)?.Count ?? 0;
        var withdrawn = closedCounts.FirstOrDefault(item => item.Status == IprStatus.Withdrawn)?.Count ?? 0;

        Kpis = new IprKpis(snapshot.Count, 0, awaiting, granted, rejected, withdrawn);

        ProjectsWithIpr = snapshot
            .Where(item => item.ProjectId.HasValue)
            .Select(item => item.ProjectId!.Value)
            .Distinct()
            .Count();

        GrantRatePercent = snapshot.Count == 0
            ? 0
            : (int)Math.Round(granted * 100d / snapshot.Count, MidpointRounding.AwayFromZero);

        var todayIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IstTimeZone).Date;
        var longPendingCutoff = todayIst.AddYears(-LongPendingYears);

        UnassignedCount = snapshot.Count(item => !item.ProjectId.HasValue);
        MissingAttachmentCount = snapshot.Count(item => item.AttachmentCount == 0);
        OverdueAttentionCount = snapshot.Count(item =>
            item.Status == IprStatus.Filed &&
            ConvertToIstDate(item.FiledAtUtc) is DateTime filedOn &&
            filedOn.Date <= longPendingCutoff);

        AttentionRecordCount = snapshot.Count(item =>
            !item.ProjectId.HasValue ||
            item.AttachmentCount == 0 ||
            string.IsNullOrWhiteSpace(item.IprFilingNumber) ||
            !item.FiledAtUtc.HasValue ||
            (item.Status == IprStatus.Filed &&
             ConvertToIstDate(item.FiledAtUtc) is DateTime filedOn &&
             filedOn.Date <= longPendingCutoff) ||
            (item.Status == IprStatus.Granted && !item.GrantedAtUtc.HasValue));

        TypeBreakdown = BuildTypeBreakdown(snapshot.Select(item => (item.Type, item.Status)).ToList());
    }

    private static IReadOnlyList<TypeBreakdownRow> BuildTypeBreakdown(IReadOnlyList<(IprType Type, IprStatus Status)> records)
        => new[] { IprType.Patent, IprType.Copyright }
            .Select(type => new TypeBreakdownRow(
                type,
                GetTypeLabel(type),
                records.Count(item => item.Type == type),
                records.Count(item => item.Type == type && item.Status == IprStatus.Granted),
                records.Count(item => item.Type == type && item.Status == IprStatus.Filed)))
            .ToList();

    private async Task LoadAttentionAsync(CancellationToken cancellationToken)
    {
        var snapshot = await BuildCountedRegisterQuery()
            .Select(record => new
            {
                record.Id,
                record.Title,
                record.IprFilingNumber,
                record.Type,
                Status = record.Status == IprStatus.FilingUnderProcess ? IprStatus.Filed : record.Status,
                record.FiledAtUtc,
                record.GrantedAtUtc,
                record.ProjectId,
                ProjectName = record.Project != null ? record.Project.Name : "Unassigned project",
                AttachmentCount = record.Attachments.Count(attachment => !attachment.IsArchived)
            })
            .ToListAsync(cancellationToken);

        var todayIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IstTimeZone).Date;
        var longPendingCutoff = todayIst.AddYears(-LongPendingYears);
        var attentionItems = new List<AttentionItem>();

        foreach (var item in snapshot)
        {
            var filedOn = ConvertToIstDate(item.FiledAtUtc);
            var waitingDays = item.Status == IprStatus.Filed && filedOn.HasValue
                ? Math.Max(0, (todayIst - filedOn.Value.Date).Days)
                : (int?)null;
            var reasons = new List<string>();
            var issueKeys = new List<string>();
            var category = string.Empty;
            var severity = "info";

            if (item.Status == IprStatus.Filed && filedOn.HasValue && filedOn.Value.Date <= longPendingCutoff)
            {
                reasons.Add($"Pending for {FormatAge(waitingDays ?? 0)}");
                issueKeys.Add("overdue");
                category = "overdue";
                severity = "critical";
            }

            if (item.Status == IprStatus.Granted && !item.GrantedAtUtc.HasValue)
            {
                reasons.Add(item.Type == IprType.Copyright
                    ? "Copyright is marked registered but has no registration date"
                    : "Patent is marked granted but has no grant date");
                issueKeys.Add("data");
                category = "data";
                severity = "critical";
            }

            if (string.IsNullOrWhiteSpace(item.IprFilingNumber))
            {
                reasons.Add("Filing number is missing");
                issueKeys.Add("data");
                category = string.IsNullOrEmpty(category) ? "data" : category;
                severity = severity == "critical" ? severity : "warning";
            }

            if (!filedOn.HasValue)
            {
                reasons.Add("Filing date is missing");
                issueKeys.Add("data");
                category = string.IsNullOrEmpty(category) ? "data" : category;
                severity = severity == "critical" ? severity : "warning";
            }

            if (!item.ProjectId.HasValue)
            {
                reasons.Add("No project is linked");
                issueKeys.Add("linkage");
                category = string.IsNullOrEmpty(category) ? "linkage" : category;
                severity = severity == "critical" ? severity : "warning";
            }

            if (item.AttachmentCount == 0)
            {
                reasons.Add("No supporting attachment is available");
                issueKeys.Add("evidence");
                category = string.IsNullOrEmpty(category) ? "evidence" : category;
                severity = severity == "critical" ? severity : "warning";
            }

            if (reasons.Count == 0)
            {
                continue;
            }

            attentionItems.Add(new AttentionItem(
                item.Id,
                string.IsNullOrWhiteSpace(item.Title) ? "Untitled IPR record" : item.Title!,
                item.ProjectName,
                GetTypeLabel(item.Type),
                category,
                severity,
                filedOn,
                waitingDays,
                item.AttachmentCount,
                reasons,
                issueKeys.Distinct(StringComparer.Ordinal).ToList()));
        }

        var groupDefinitions = new[]
        {
            (Key: "overdue", Title: "Long-pending filings", Description: $"Pending for more than {LongPendingYears} years. Chase the patent office, then record the outcome.", Tone: "critical"),
            (Key: "data", Title: "Details to correct", Description: "A filing number, filing date or grant date is missing or inconsistent.", Tone: "critical"),
            (Key: "linkage", Title: "Not linked to a project", Description: "Link each filing to the project it protects.", Tone: "warning"),
            (Key: "evidence", Title: "Missing evidence", Description: "No filing receipt or grant certificate uploaded.", Tone: "warning")
        };

        AttentionGroups = groupDefinitions
            .Where(definition => string.IsNullOrWhiteSpace(AttentionIssue)
                || string.Equals(definition.Key, AttentionIssue, StringComparison.Ordinal))
            .Select(definition => CreateAttentionGroup(
                definition.Key,
                definition.Title,
                definition.Description,
                definition.Tone,
                attentionItems,
                includeAnyDetectedIssue: !string.IsNullOrWhiteSpace(AttentionIssue)))
            .Where(group => group.Items.Count > 0)
            .ToList();

        VisibleAttentionRecordCount = AttentionGroups.Sum(group => group.Items.Count);
        FollowUpTotalCount = attentionItems.Count;
        AttentionIssueCounts = groupDefinitions.ToDictionary(
            definition => definition.Key,
            definition => attentionItems.Count(item => item.IssueKeys.Contains(definition.Key, StringComparer.Ordinal)),
            StringComparer.Ordinal);
    }

    private static AttentionGroup CreateAttentionGroup(
        string key,
        string title,
        string description,
        string tone,
        IEnumerable<AttentionItem> items,
        bool includeAnyDetectedIssue = false)
    {
        return new AttentionGroup(
            key,
            title,
            description,
            tone,
            items
                .Where(item => includeAnyDetectedIssue
                    ? item.IssueKeys.Contains(key, StringComparer.Ordinal)
                    : string.Equals(item.Category, key, StringComparison.Ordinal))
                .OrderByDescending(item => item.WaitingDays ?? -1)
                .ThenBy(item => item.Title)
                .ToList());
    }

    private async Task LoadAnalyticsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await BuildCountedRegisterQuery()
            .Select(record => new
            {
                record.Id,
                record.Title,
                record.Type,
                Status = record.Status == IprStatus.FilingUnderProcess ? IprStatus.Filed : record.Status,
                record.FiledAtUtc,
                record.GrantedAtUtc,
                FiledYear = record.FiledAtUtc.HasValue ? (int?)record.FiledAtUtc.Value.Year : null,
                GrantedYear = record.GrantedAtUtc.HasValue ? (int?)record.GrantedAtUtc.Value.Year : null,
                record.ProjectId,
                ProjectName = record.Project != null ? record.Project.Name : "Unassigned project",
                AttachmentCount = record.Attachments.Count(attachment => !attachment.IsArchived)
            })
            .ToListAsync(cancellationToken);

        var todayIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IstTimeZone).Date;
        var waitingRows = snapshot
            .Where(item => item.Status == IprStatus.Filed && item.FiledAtUtc.HasValue)
            .Select(item =>
            {
                var filedOn = ConvertToIstDate(item.FiledAtUtc)!.Value;
                var waitingDays = Math.Max(0, (todayIst - filedOn.Date).Days);
                return new AwaitingGrantRow(
                    item.Id,
                    item.ProjectId,
                    item.ProjectName,
                    string.IsNullOrWhiteSpace(item.Title) ? "Untitled IPR record" : item.Title!,
                    GetTypeLabel(item.Type),
                    filedOn,
                    waitingDays);
            })
            .OrderByDescending(item => item.WaitingDays)
            .ToList();

        var grantDurations = snapshot
            .Where(item => item.Status == IprStatus.Granted && item.FiledAtUtc.HasValue && item.GrantedAtUtc.HasValue)
            .Select(item =>
            {
                var filedOn = ConvertToIstDate(item.FiledAtUtc)!.Value.Date;
                var grantedOn = ConvertToIstDate(item.GrantedAtUtc)!.Value.Date;
                return Math.Max(0, (grantedOn - filedOn).Days);
            })
            .OrderBy(days => days)
            .ToList();

        var waitingDurations = waitingRows.Select(item => item.WaitingDays).OrderBy(days => days).ToList();
        var overThreeYears = waitingRows.Count(item => item.FiledOn.HasValue && item.FiledOn.Value.Date <= todayIst.AddYears(-LongPendingYears));

        // Analytics follows its filter scope; the summary strip above it stays register-wide.
        var scopedGranted = snapshot.Count(item => item.Status == IprStatus.Granted);
        AnalyticsSummary = new AnalyticsSummaryModel(
            snapshot.Count == 0 ? 0 : (int)Math.Round(scopedGranted * 100d / snapshot.Count, MidpointRounding.AwayFromZero),
            Median(grantDurations),
            Median(waitingDurations),
            overThreeYears,
            snapshot.Count(item => !item.ProjectId.HasValue),
            snapshot.Count(item => item.AttachmentCount == 0),
            snapshot.Count,
            BuildTypeBreakdown(snapshot.Select(item => (item.Type, item.Status)).ToList()));

        AwaitingAgeBands = BuildAgeBands(waitingRows);

        FilingCohorts = snapshot
            .Where(item => item.FiledYear.HasValue)
            .GroupBy(item => item.FiledYear!.Value)
            .OrderBy(group => group.Key)
            .Select(group => new FilingCohortRow(group.Key, group.Count(), group.Count(item => item.Status == IprStatus.Granted)))
            .ToList();

        var years = snapshot
            .SelectMany(item => new[] { item.FiledYear, item.GrantedYear })
            .Where(year => year.HasValue)
            .Select(year => year!.Value)
            .Distinct()
            .OrderBy(year => year)
            .ToList();

        YearlyStats = years
            .Select(year => new YearlyRow
            {
                Year = year,
                Filed = snapshot.Count(item => item.FiledYear == year),
                Granted = snapshot.Count(item => item.Status == IprStatus.Granted && item.GrantedYear == year)
            })
            .ToList();
    }

    public static string GetPendingAgeLabel(IprPendingAge band) => band switch
    {
        IprPendingAge.UnderOneYear => "Pending under 1 year",
        IprPendingAge.OneToThreeYears => "Pending 1–3 years",
        IprPendingAge.ThreeToFiveYears => "Pending 3–5 years",
        IprPendingAge.OverFiveYears => "Pending over 5 years",
        _ => "Any age"
    };

    private static IReadOnlyList<AgeBandRow> BuildAgeBands(IReadOnlyCollection<AwaitingGrantRow> rows)
    {
        var total = rows.Count;
        // Same day thresholds as the register's pendingAge filter (IprQueryFilter.PendingAgeRange).
        var bands = new[]
        {
            (Band: IprPendingAge.UnderOneYear, Label: "Under 1 year", Tone: "fresh"),
            (Band: IprPendingAge.OneToThreeYears, Label: "1–3 years", Tone: "watch"),
            (Band: IprPendingAge.ThreeToFiveYears, Label: "3–5 years", Tone: "late"),
            (Band: IprPendingAge.OverFiveYears, Label: "Over 5 years", Tone: "critical")
        };

        return bands
            .Select(band =>
            {
                var (minDays, maxDays) = IprQueryFilter.PendingAgeRange(band.Band);
                var count = rows.Count(item => item.WaitingDays >= minDays && (!maxDays.HasValue || item.WaitingDays < maxDays.Value));
                return new AgeBandRow(
                    band.Label,
                    count,
                    total == 0 ? 0 : (int)Math.Round(count * 100d / total, MidpointRounding.AwayFromZero),
                    band.Tone,
                    band.Band);
            })
            .ToList();
    }

    private static int? Median(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (int)Math.Round((values[middle - 1] + values[middle]) / 2d, MidpointRounding.AwayFromZero);
    }

    private IQueryable<IprRecord> BuildPublicRegisterQuery()
        => IprQueryFilter.Apply(
            _db.IprRecords.AsNoTracking(),
            BuildFilter(includeQuery: false),
            includeSearch: false);

    // Summary figures and the Projects, Follow-up and Analytics views use the positive count
    // only, even when a closed status is selected in the filter.
    private IQueryable<IprRecord> BuildCountedRegisterQuery()
        => BuildPublicRegisterQuery().Where(IprStatusPolicy.CountedPredicate);

    private async Task<IprRecord?> LoadRecordAsync(int id, CancellationToken cancellationToken, bool overwriteInput)
    {
        var record = await _readService.GetAsync(id, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var rowVersion = Convert.ToBase64String(record.RowVersion);

        if (overwriteInput || !Input.Id.HasValue || Input.Id.Value != record.Id)
        {
            Input = new RecordInput
            {
                Id = record.Id,
                FilingNumber = record.IprFilingNumber,
                Title = record.Title,
                Notes = record.Notes,
                Type = record.Type,
                Status = record.Status == IprStatus.FilingUnderProcess ? IprStatus.Filed : record.Status,
                FiledBy = record.FiledBy,
                FiledOn = record.FiledAtUtc.HasValue
                    ? DateOnly.FromDateTime(record.FiledAtUtc.Value.UtcDateTime)
                    : null,
                GrantedOn = record.GrantedAtUtc.HasValue
                    ? DateOnly.FromDateTime(record.GrantedAtUtc.Value.UtcDateTime)
                    : null,
                ProjectId = record.ProjectId,
                RowVersion = rowVersion
            };
        }
        else
        {
            Input.RowVersion = rowVersion;
        }

        DeleteRequest = new DeleteInput
        {
            Id = record.Id,
            RowVersion = rowVersion
        };

        UploadInput = new UploadAttachmentInput
        {
            RecordId = record.Id
        };

        Attachments = record.Attachments
            .Where(attachment => !attachment.IsArchived)
            .OrderByDescending(attachment => attachment.UploadedAtUtc)
            .Select(attachment => new AttachmentViewModel(
                attachment.Id,
                attachment.OriginalFileName,
                attachment.FileSize,
                FormatUserDisplay(attachment.UploadedByUser, attachment.UploadedByUserId),
                attachment.UploadedAtUtc,
                Convert.ToBase64String(attachment.RowVersion)))
            .ToList();

        return record;
    }

    private static string FormatUserDisplay(ApplicationUser? user, string fallback)
    {
        if (user is { FullName: { Length: > 0 } fullName })
        {
            return fullName;
        }

        if (user is { UserName: { Length: > 0 } userName })
        {
            return userName;
        }

        return fallback;
    }
}
