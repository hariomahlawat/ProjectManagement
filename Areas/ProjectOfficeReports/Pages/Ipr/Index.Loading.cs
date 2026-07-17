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
        if (dto is null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

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
            GetStatusLabel(dto.Status),
            GetStatusChipClass(dto.Status),
            string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes,
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

        var converted = TimeZoneInfo.ConvertTimeFromUtc(value.Value.UtcDateTime, IstTimeZone);
        return converted;
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
                await LoadProjectLinksAsync(cancellationToken);
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
        var query = BuildPublicRegisterQuery();

        var grouped = await query
            .GroupBy(record => new
            {
                record.Type,
                Status = record.Status == IprStatus.FilingUnderProcess
                    ? IprStatus.Filed
                    : record.Status
            })
            .Select(group => new
            {
                group.Key.Type,
                group.Key.Status,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var granted = grouped
            .Where(item => item.Status == IprStatus.Granted)
            .Sum(item => item.Count);
        var awaiting = grouped
            .Where(item => item.Status == IprStatus.Filed)
            .Sum(item => item.Count);
        Kpis = new IprKpis(awaiting + granted, 0, awaiting, granted, 0, 0);

        ProjectsWithIpr = await query
            .Where(record => record.ProjectId.HasValue)
            .Select(record => record.ProjectId!.Value)
            .Distinct()
            .CountAsync(cancellationToken);

        TypeBreakdown = new[] { IprType.Patent, IprType.Copyright }
            .Select(type =>
            {
                var filed = grouped
                    .Where(item => item.Type == type)
                    .Sum(item => item.Count);
                var grantedByType = grouped
                    .Where(item => item.Type == type && item.Status == IprStatus.Granted)
                    .Sum(item => item.Count);

                return new TypeBreakdownRow(
                    GetTypeLabel(type),
                    filed,
                    grantedByType,
                    filed - grantedByType);
            })
            .ToList();
    }

    private async Task LoadProjectLinksAsync(CancellationToken cancellationToken)
    {
        var snapshot = await BuildPublicRegisterQuery()
            .Select(record => new
            {
                record.Id,
                record.Title,
                record.Type,
                Status = record.Status == IprStatus.FilingUnderProcess
                    ? IprStatus.Filed
                    : record.Status,
                record.FiledAtUtc,
                record.GrantedAtUtc,
                record.ProjectId,
                ProjectName = record.Project != null
                    ? record.Project.Name
                    : "Unassigned project"
            })
            .ToListAsync(cancellationToken);

        var projectCounts = snapshot
            .GroupBy(item => item.ProjectId ?? 0)
            .ToDictionary(group => group.Key, group => group.Count());

        ProjectIprLinks = snapshot
            .OrderBy(item => item.ProjectName)
            .ThenBy(item => item.Type)
            .ThenBy(item => item.Title)
            .Select(item => new ProjectIprLinkRow(
                item.Id,
                item.ProjectId,
                item.ProjectName,
                string.IsNullOrWhiteSpace(item.Title) ? "Untitled IPR record" : item.Title!,
                GetTypeLabel(item.Type),
                item.Status == IprStatus.Granted ? "Granted" : "Awaiting grant",
                ConvertToIstDate(item.FiledAtUtc),
                ConvertToIstDate(item.GrantedAtUtc),
                projectCounts.TryGetValue(item.ProjectId ?? 0, out var count) ? count : 1))
            .ToList();
    }

    private async Task LoadAnalyticsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await BuildPublicRegisterQuery()
            .Select(record => new
            {
                record.Id,
                record.Title,
                record.Type,
                Status = record.Status == IprStatus.FilingUnderProcess
                    ? IprStatus.Filed
                    : record.Status,
                record.FiledAtUtc,
                record.GrantedAtUtc,
                FiledYear = record.FiledAtUtc.HasValue
                    ? (int?)record.FiledAtUtc.Value.Year
                    : null,
                GrantedYear = record.GrantedAtUtc.HasValue
                    ? (int?)record.GrantedAtUtc.Value.Year
                    : null,
                record.ProjectId,
                ProjectName = record.Project != null
                    ? record.Project.Name
                    : "Unassigned project"
            })
            .ToListAsync(cancellationToken);

        var todayIst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IstTimeZone).Date;
        OldestAwaitingGrant = snapshot
            .Where(item => item.Status == IprStatus.Filed)
            .OrderBy(item => item.FiledAtUtc ?? DateTimeOffset.MaxValue)
            .Take(5)
            .Select(item =>
            {
                var filedOn = ConvertToIstDate(item.FiledAtUtc);
                var waitingDays = filedOn.HasValue
                    ? Math.Max(0, (todayIst - filedOn.Value.Date).Days)
                    : 0;

                return new AwaitingGrantRow(
                    item.Id,
                    item.ProjectId,
                    item.ProjectName,
                    string.IsNullOrWhiteSpace(item.Title) ? "Untitled IPR record" : item.Title!,
                    GetTypeLabel(item.Type),
                    filedOn,
                    waitingDays);
            })
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
                Granted = snapshot.Count(item =>
                    item.Status == IprStatus.Granted &&
                    item.GrantedYear == year)
            })
            .ToList();
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
            // Preserve the user's submitted values, but refresh the concurrency token so
            // the form can be reviewed and submitted again after a validation/conflict response.
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
            .Where(a => !a.IsArchived)
            .OrderByDescending(a => a.UploadedAtUtc)
            .Select(a => new AttachmentViewModel(
                a.Id,
                a.OriginalFileName,
                a.FileSize,
                FormatUserDisplay(a.UploadedByUser, a.UploadedByUserId),
                a.UploadedAtUtc,
                Convert.ToBase64String(a.RowVersion)))
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
