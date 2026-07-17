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
            ConvertToIstDate(dto.FiledAtUtc),
            ConvertToIstDate(dto.GrantedAtUtc),
            attachments,
            dto.AttachmentCount);
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

    private IReadOnlyList<string> BuildActiveFilterChips()
    {
        if (!HasAnyFilter)
        {
            return Array.Empty<string>();
        }

        var chips = new List<string>();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            chips.Add($"Search: \"{Query}\"");
        }

        foreach (var type in Types)
        {
            chips.Add($"Type: {GetTypeLabel(type)}");
        }

        foreach (var status in Statuses)
        {
            chips.Add($"Status: {GetStatusLabel(status)}");
        }

        if (ProjectId.HasValue)
        {
            var projectValue = ProjectId.Value.ToString(CultureInfo.InvariantCulture);
            var projectLabel = ProjectOptions.FirstOrDefault(option => option.Value == projectValue)?.Text;
            if (!string.IsNullOrWhiteSpace(projectLabel) && !string.Equals(projectLabel, "All projects", StringComparison.Ordinal))
            {
                chips.Add($"Project: {projectLabel}");
            }
        }

        if (Year.HasValue)
        {
            chips.Add($"Filed year: {Year.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return chips;
    }

    private async Task LoadPageAsync(CancellationToken cancellationToken, bool loadRecordInput)
    {
        NormalizePaging();
        await LoadRegisterOverviewAsync(cancellationToken);

        switch (Tab)
        {
            case "project":
                await LoadProjectGroupsAsync(cancellationToken);
                break;
            case "followup":
                await LoadAttentionAsync(cancellationToken);
                break;
            case "analytics":
                await LoadAnalyticsAsync(cancellationToken);
                break;
            default:
                await LoadRecordsAsync(cancellationToken);
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
        var result = await _readService.SearchAsync(BuildFilter(), cancellationToken);
        Records = result.Items.Select(CreateRowViewModel).ToList();
        TotalCount = result.Total;
        PageNumber = result.Page;
        PageSize = result.PageSize;
        TotalPages = PageSize > 0
            ? (int)Math.Ceiling(result.Total / (double)PageSize)
            : 0;
    }

    private async Task LoadRegisterOverviewAsync(CancellationToken cancellationToken)
    {
        var snapshot = await BuildPublicRegisterQuery()
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
        Kpis = new IprKpis(snapshot.Count, 0, awaiting, granted, 0, 0);

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

        TypeBreakdown = new[] { IprType.Patent, IprType.Copyright }
            .Select(type =>
            {
                var filed = snapshot.Count(item => item.Type == type);
                var protectedByType = snapshot.Count(item => item.Type == type && item.Status == IprStatus.Granted);
                var pendingByType = snapshot.Count(item => item.Type == type && item.Status == IprStatus.Filed);
                return new TypeBreakdownRow(type, GetTypeLabel(type), filed, protectedByType, pendingByType);
            })
            .ToList();
    }

    private async Task LoadProjectGroupsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await BuildPublicRegisterQuery()
            .Select(record => new
            {
                record.Id,
                record.Title,
                record.Type,
                Status = record.Status == IprStatus.FilingUnderProcess ? IprStatus.Filed : record.Status,
                record.FiledAtUtc,
                record.GrantedAtUtc,
                record.ProjectId,
                ProjectName = record.Project != null ? record.Project.Name : "Unassigned project",
                ProjectLifecycle = record.Project != null ? (ProjectLifecycleStatus?)record.Project.LifecycleStatus : null,
                ProjectArchived = record.Project != null && record.Project.IsArchived,
                AttachmentCount = record.Attachments.Count(attachment => !attachment.IsArchived)
            })
            .ToListAsync(cancellationToken);

        ProjectIprGroups = snapshot
            .GroupBy(item => new
            {
                item.ProjectId,
                item.ProjectName,
                item.ProjectLifecycle,
                item.ProjectArchived
            })
            .Select(group =>
            {
                var items = group
                    .OrderByDescending(item => item.FiledAtUtc ?? DateTimeOffset.MinValue)
                    .ThenBy(item => item.Title)
                    .Select(item => new ProjectIprItem(
                        item.Id,
                        string.IsNullOrWhiteSpace(item.Title) ? "Untitled IPR record" : item.Title!,
                        GetTypeLabel(item.Type),
                        GetStatusLabel(item.Status, item.Type),
                        ConvertToIstDate(item.FiledAtUtc),
                        ConvertToIstDate(item.GrantedAtUtc),
                        item.AttachmentCount))
                    .ToList();

                var lifecycle = group.Key.ProjectLifecycle.HasValue
                    ? GetProjectLifecycleLabel(group.Key.ProjectLifecycle.Value, group.Key.ProjectArchived)
                    : "Needs assignment";

                return new ProjectIprGroup(
                    group.Key.ProjectId,
                    group.Key.ProjectName,
                    lifecycle,
                    group.Count(),
                    group.Count(item => item.Status == IprStatus.Granted),
                    group.Count(item => item.Status == IprStatus.Filed),
                    group.Count(item => item.Type == IprType.Patent),
                    group.Count(item => item.Type == IprType.Copyright),
                    items
                        .Where(item => item.FiledOn.HasValue)
                        .Select(item => item.FiledOn)
                        .OrderByDescending(date => date)
                        .FirstOrDefault(),
                    !group.Key.ProjectId.HasValue,
                    items);
            })
            .OrderByDescending(group => group.IsUnassigned)
            .ThenByDescending(group => group.Awaiting)
            .ThenBy(group => group.ProjectName)
            .ToList();
    }

    private async Task LoadAttentionAsync(CancellationToken cancellationToken)
    {
        var snapshot = await BuildPublicRegisterQuery()
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
            var category = string.Empty;
            var severity = "info";

            if (item.Status == IprStatus.Filed && filedOn.HasValue && filedOn.Value.Date <= longPendingCutoff)
            {
                reasons.Add($"Pending for {FormatAge(waitingDays ?? 0)}");
                category = "overdue";
                severity = "critical";
            }

            if (item.Status == IprStatus.Granted && !item.GrantedAtUtc.HasValue)
            {
                reasons.Add(item.Type == IprType.Copyright
                    ? "Copyright is marked registered but has no registration date"
                    : "Patent is marked granted but has no grant date");
                category = "data";
                severity = "critical";
            }

            if (string.IsNullOrWhiteSpace(item.IprFilingNumber))
            {
                reasons.Add("Filing number is missing");
                category = string.IsNullOrEmpty(category) ? "data" : category;
                severity = severity == "critical" ? severity : "warning";
            }

            if (!filedOn.HasValue)
            {
                reasons.Add("Filing date is missing");
                category = string.IsNullOrEmpty(category) ? "data" : category;
                severity = severity == "critical" ? severity : "warning";
            }

            if (!item.ProjectId.HasValue)
            {
                reasons.Add("No project is linked");
                category = string.IsNullOrEmpty(category) ? "linkage" : category;
                severity = severity == "critical" ? severity : "warning";
            }

            if (item.AttachmentCount == 0)
            {
                reasons.Add("No supporting attachment is available");
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
                reasons));
        }

        AttentionGroups = new[]
        {
            CreateAttentionGroup(
                "overdue",
                "Long-pending filings",
                $"Pending for more than {LongPendingYears} years.",
                "critical",
                attentionItems),
            CreateAttentionGroup(
                "data",
                "Data inconsistencies",
                "Status or mandatory filing information requires correction.",
                "critical",
                attentionItems),
            CreateAttentionGroup(
                "linkage",
                "Project linkage",
                "Records not yet connected to a project.",
                "warning",
                attentionItems),
            CreateAttentionGroup(
                "evidence",
                "Supporting evidence",
                "Records without an uploaded filing or protection document.",
                "warning",
                attentionItems)
        }
        .Where(group => group.Items.Count > 0)
        .ToList();
    }

    private static AttentionGroup CreateAttentionGroup(
        string key,
        string title,
        string description,
        string tone,
        IEnumerable<AttentionItem> items)
    {
        return new AttentionGroup(
            key,
            title,
            description,
            tone,
            items
                .Where(item => string.Equals(item.Category, key, StringComparison.Ordinal))
                .OrderByDescending(item => item.WaitingDays ?? -1)
                .ThenBy(item => item.Title)
                .ToList());
    }

    private async Task LoadAnalyticsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await BuildPublicRegisterQuery()
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

        OldestAwaitingGrant = waitingRows.Take(8).ToList();

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

        AnalyticsSummary = new AnalyticsSummaryModel(
            GrantRatePercent,
            Median(grantDurations),
            Median(waitingDurations),
            overThreeYears,
            snapshot.Count(item => !item.ProjectId.HasValue),
            snapshot.Count(item => item.AttachmentCount == 0));

        AwaitingAgeBands = BuildAgeBands(waitingRows);

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

    private static IReadOnlyList<AgeBandRow> BuildAgeBands(IReadOnlyCollection<AwaitingGrantRow> rows)
    {
        var total = rows.Count;
        var bands = new[]
        {
            (Label: "Under 1 year", Count: rows.Count(item => item.WaitingDays < 365), Tone: "fresh"),
            (Label: "1–3 years", Count: rows.Count(item => item.WaitingDays >= 365 && item.WaitingDays < 1095), Tone: "watch"),
            (Label: "3–5 years", Count: rows.Count(item => item.WaitingDays >= 1095 && item.WaitingDays < 1825), Tone: "late"),
            (Label: "Over 5 years", Count: rows.Count(item => item.WaitingDays >= 1825), Tone: "critical")
        };

        return bands
            .Select(band => new AgeBandRow(
                band.Label,
                band.Count,
                total == 0 ? 0 : (int)Math.Round(band.Count * 100d / total, MidpointRounding.AwayFromZero),
                band.Tone))
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
    {
        return _db.IprRecords
            .AsNoTracking()
            .Where(record =>
                record.Status == IprStatus.FilingUnderProcess ||
                record.Status == IprStatus.Filed ||
                record.Status == IprStatus.Granted);
    }

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
