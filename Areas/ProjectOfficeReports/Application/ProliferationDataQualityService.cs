using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed record ProliferationDataQualityQuery(
    int? ProjectId,
    string? IssueType,
    string? Search,
    int Page,
    int PageSize);

public sealed record ProliferationDataQualityIssue(
    string IssueKey,
    string IssueType,
    string Severity,
    ProliferationRecordKind RecordKind,
    Guid RecordId,
    int ProjectId,
    string ProjectName,
    string? ProjectCode,
    ProliferationSource Source,
    string SourceLabel,
    int? Year,
    DateOnly? ProliferationDate,
    string? UnitName,
    int Quantity,
    ApprovalStatus ApprovalStatus,
    DateTime LastUpdatedOnUtc,
    string RowVersion,
    string Description,
    bool CanCorrect,
    int RelatedRecordCount);

public sealed record ProliferationDataQualityResult(
    int Total,
    int Page,
    int PageSize,
    int InvalidDateOrYearCount,
    int MissingUnitCount,
    int InvalidQuantityCount,
    int PossibleDuplicateCount,
    IReadOnlyList<ProliferationDataQualityIssue> Items);

public sealed record ProliferationDataQualitySummary(
    int CorrectionRequiredCount,
    int PossibleDuplicateCount,
    int InvalidDateOrYearCount,
    int MissingUnitCount,
    int InvalidQuantityCount);

public sealed record ProliferationDataQualityCorrection(
    ProliferationRecordKind RecordKind,
    Guid RecordId,
    string RowVersion,
    int? CorrectedYear,
    DateOnly? CorrectedDate,
    string? CorrectedUnitName,
    int? CorrectedQuantity,
    string Reason);

public sealed class ProliferationDataQualityService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public ProliferationDataQualityService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IClock clock,
        IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<ProliferationDataQualitySummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var result = await GetIssuesAsync(
            new ProliferationDataQualityQuery(null, null, null, 1, 10),
            cancellationToken);

        var correctionRequired = result.InvalidDateOrYearCount
            + result.MissingUnitCount
            + result.InvalidQuantityCount;

        return new ProliferationDataQualitySummary(
            correctionRequired,
            result.PossibleDuplicateCount,
            result.InvalidDateOrYearCount,
            result.MissingUnitCount,
            result.InvalidQuantityCount);
    }

    public async Task<ProliferationDataQualityResult> GetIssuesAsync(
        ProliferationDataQualityQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 25 : request.PageSize, 10, 100);
        var maximumYear = ProliferationYearPolicy.GetMaximumYear(_clock.UtcNow);
        var maximumDate = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime.AddDays(30));
        var minimumDate = new DateOnly(ProliferationYearPolicy.MinimumYear, 1, 1);

        var yearlyRows = await (
                from record in _db.ProliferationYearlies.AsNoTracking()
                join project in _db.Projects.AsNoTracking() on record.ProjectId equals project.Id
                where !project.IsDeleted && !project.IsArchived
                select new
                {
                    record.Id,
                    record.ProjectId,
                    ProjectName = project.Name,
                    ProjectCode = project.CaseFileNumber,
                    record.Source,
                    record.Year,
                    Quantity = record.TotalQuantity,
                    record.ApprovalStatus,
                    record.LastUpdatedOnUtc,
                    record.RowVersion
                })
            .ToListAsync(cancellationToken);

        var granularRows = await (
                from record in _db.ProliferationGranularEntries.AsNoTracking()
                join project in _db.Projects.AsNoTracking() on record.ProjectId equals project.Id
                where !project.IsDeleted && !project.IsArchived
                select new
                {
                    record.Id,
                    record.ProjectId,
                    ProjectName = project.Name,
                    ProjectCode = project.CaseFileNumber,
                    record.Source,
                    record.ProliferationDate,
                    record.UnitName,
                    record.Quantity,
                    record.ApprovalStatus,
                    record.LastUpdatedOnUtc,
                    record.RowVersion
                })
            .ToListAsync(cancellationToken);

        var issues = new List<ProliferationDataQualityIssue>();

        foreach (var row in yearlyRows.Where(x => x.Year < ProliferationYearPolicy.MinimumYear || x.Year > maximumYear))
        {
            issues.Add(new ProliferationDataQualityIssue(
                $"yearly:{row.Id}:invalid-year",
                "invalid_year",
                "high",
                ProliferationRecordKind.Yearly,
                row.Id,
                row.ProjectId,
                row.ProjectName,
                row.ProjectCode,
                row.Source,
                row.Source.ToDisplayName(),
                row.Year,
                null,
                null,
                row.Quantity,
                row.ApprovalStatus,
                row.LastUpdatedOnUtc,
                EncodeRowVersion(row.RowVersion),
                $"Annual quantity is assigned to invalid year {row.Year}.",
                true,
                1));
        }

        foreach (var row in yearlyRows.Where(x => x.Quantity < 0))
        {
            issues.Add(new ProliferationDataQualityIssue(
                $"yearly:{row.Id}:invalid-quantity",
                "invalid_quantity",
                "high",
                ProliferationRecordKind.Yearly,
                row.Id,
                row.ProjectId,
                row.ProjectName,
                row.ProjectCode,
                row.Source,
                row.Source.ToDisplayName(),
                row.Year,
                null,
                null,
                row.Quantity,
                row.ApprovalStatus,
                row.LastUpdatedOnUtc,
                EncodeRowVersion(row.RowVersion),
                $"Annual record has negative quantity {row.Quantity}.",
                true,
                1));
        }

        foreach (var row in granularRows)
        {
            if (row.ProliferationDate < minimumDate || row.ProliferationDate > maximumDate)
            {
                issues.Add(new ProliferationDataQualityIssue(
                    $"granular:{row.Id}:invalid-date",
                    "invalid_date",
                    "high",
                    ProliferationRecordKind.Granular,
                    row.Id,
                    row.ProjectId,
                    row.ProjectName,
                    row.ProjectCode,
                    row.Source,
                    row.Source.ToDisplayName(),
                    row.ProliferationDate.Year,
                    row.ProliferationDate,
                    row.UnitName,
                    row.Quantity,
                    row.ApprovalStatus,
                    row.LastUpdatedOnUtc,
                    EncodeRowVersion(row.RowVersion),
                    $"Detailed entry has invalid date {row.ProliferationDate:dd MMM yyyy}.",
                    true,
                    1));
            }

            if (string.IsNullOrWhiteSpace(row.UnitName))
            {
                issues.Add(new ProliferationDataQualityIssue(
                    $"granular:{row.Id}:missing-unit",
                    "missing_unit",
                    "high",
                    ProliferationRecordKind.Granular,
                    row.Id,
                    row.ProjectId,
                    row.ProjectName,
                    row.ProjectCode,
                    row.Source,
                    row.Source.ToDisplayName(),
                    row.ProliferationDate.Year,
                    row.ProliferationDate,
                    row.UnitName,
                    row.Quantity,
                    row.ApprovalStatus,
                    row.LastUpdatedOnUtc,
                    EncodeRowVersion(row.RowVersion),
                    "Detailed entry does not identify a receiving unit.",
                    true,
                    1));
            }

            if (row.Quantity <= 0)
            {
                issues.Add(new ProliferationDataQualityIssue(
                    $"granular:{row.Id}:invalid-quantity",
                    "invalid_quantity",
                    "high",
                    ProliferationRecordKind.Granular,
                    row.Id,
                    row.ProjectId,
                    row.ProjectName,
                    row.ProjectCode,
                    row.Source,
                    row.Source.ToDisplayName(),
                    row.ProliferationDate.Year,
                    row.ProliferationDate,
                    row.UnitName,
                    row.Quantity,
                    row.ApprovalStatus,
                    row.LastUpdatedOnUtc,
                    EncodeRowVersion(row.RowVersion),
                    $"Detailed entry has non-positive quantity {row.Quantity}.",
                    true,
                    1));
            }
        }

        var yearlyDuplicates = yearlyRows
            .Where(x => x.ApprovalStatus == ApprovalStatus.Approved && x.Year >= ProliferationYearPolicy.MinimumYear)
            .GroupBy(x => new { x.ProjectId, x.Source, x.Year })
            .Where(group => group.Count() > 1);

        foreach (var group in yearlyDuplicates)
        {
            var row = group.OrderByDescending(x => x.LastUpdatedOnUtc).First();
            issues.Add(new ProliferationDataQualityIssue(
                $"yearly:{row.ProjectId}:{(int)row.Source}:{row.Year}:duplicate",
                "possible_duplicate",
                "medium",
                ProliferationRecordKind.Yearly,
                row.Id,
                row.ProjectId,
                row.ProjectName,
                row.ProjectCode,
                row.Source,
                row.Source.ToDisplayName(),
                row.Year,
                null,
                null,
                group.Sum(x => x.Quantity),
                ApprovalStatus.Approved,
                group.Max(x => x.LastUpdatedOnUtc),
                EncodeRowVersion(row.RowVersion),
                $"{group.Count()} approved annual records exist for the same project, source and year.",
                false,
                group.Count()));
        }

        var granularDuplicates = granularRows
            .Where(x => x.ApprovalStatus == ApprovalStatus.Approved)
            .GroupBy(x => new
            {
                x.ProjectId,
                x.Source,
                x.ProliferationDate,
                UnitName = (x.UnitName ?? string.Empty).Trim().ToUpperInvariant(),
                x.Quantity
            })
            .Where(group => group.Count() > 1);

        foreach (var group in granularDuplicates)
        {
            var row = group.OrderByDescending(x => x.LastUpdatedOnUtc).First();
            issues.Add(new ProliferationDataQualityIssue(
                $"granular:{row.ProjectId}:{(int)row.Source}:{row.ProliferationDate:yyyyMMdd}:{row.Quantity}:{group.Key.UnitName}:duplicate",
                "possible_duplicate",
                "medium",
                ProliferationRecordKind.Granular,
                row.Id,
                row.ProjectId,
                row.ProjectName,
                row.ProjectCode,
                row.Source,
                row.Source.ToDisplayName(),
                row.ProliferationDate.Year,
                row.ProliferationDate,
                row.UnitName,
                row.Quantity,
                ApprovalStatus.Approved,
                group.Max(x => x.LastUpdatedOnUtc),
                EncodeRowVersion(row.RowVersion),
                $"{group.Count()} approved detailed entries have the same project, source, date, unit and quantity.",
                false,
                group.Count()));
        }

        IEnumerable<ProliferationDataQualityIssue> scoped = issues;
        if (request.ProjectId is > 0)
        {
            scoped = scoped.Where(x => x.ProjectId == request.ProjectId.Value);
        }
        var scopedIssues = scoped.ToList();
        var invalidDateOrYearCount = scopedIssues.Count(x => x.IssueType is "invalid_year" or "invalid_date");
        var missingUnitCount = scopedIssues.Count(x => x.IssueType == "missing_unit");
        var invalidQuantityCount = scopedIssues.Count(x => x.IssueType == "invalid_quantity");
        var duplicateCount = scopedIssues.Count(x => x.IssueType == "possible_duplicate");

        IEnumerable<ProliferationDataQualityIssue> filtered = scopedIssues;
        if (!string.IsNullOrWhiteSpace(request.IssueType))
        {
            var issueType = request.IssueType.Trim();
            filtered = issueType.Equals("invalid_date_or_year", StringComparison.OrdinalIgnoreCase)
                ? filtered.Where(x => x.IssueType is "invalid_year" or "invalid_date")
                : filtered.Where(x => string.Equals(x.IssueType, issueType, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            filtered = filtered.Where(x =>
                x.ProjectName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(x.ProjectCode) && x.ProjectCode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.UnitName) && x.UnitName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                x.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = filtered
            .OrderBy(x => x.Severity == "high" ? 0 : 1)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.Year ?? 0)
            .ThenBy(x => x.IssueType, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProliferationDataQualityResult(
            ordered.Count,
            page,
            pageSize,
            invalidDateOrYearCount,
            missingUnitCount,
            invalidQuantityCount,
            duplicateCount,
            ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList());
    }

    public async Task<ServiceResult> CorrectAsync(
        ProliferationDataQualityCorrection correction,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (correction is null) throw new ArgumentNullException(nameof(correction));
        if (user is null) throw new ArgumentNullException(nameof(user));

        var actor = await _users.GetUserAsync(user);
        if (actor is null)
        {
            return ServiceResult.Fail("User not found.");
        }

        if (!user.IsInRole("Admin") && !user.IsInRole("HoD"))
        {
            return ServiceResult.Fail("Only Admin or HoD may correct proliferation data-quality issues.");
        }

        var reason = Normalize(correction.Reason, 500);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ServiceResult.Fail("A correction reason is required.");
        }

        if (!TryDecodeRowVersion(correction.RowVersion, out var rowVersion))
        {
            return ServiceResult.Fail("The record is out of date. Refresh and try again.");
        }

        return correction.RecordKind == ProliferationRecordKind.Yearly
            ? await CorrectYearlyAsync(correction, rowVersion, reason, actor, cancellationToken)
            : await CorrectGranularAsync(correction, rowVersion, reason, actor, cancellationToken);
    }

    private async Task<ServiceResult> CorrectYearlyAsync(
        ProliferationDataQualityCorrection correction,
        byte[] rowVersion,
        string reason,
        ApplicationUser actor,
        CancellationToken cancellationToken)
    {
        var entity = await _db.ProliferationYearlies.FirstOrDefaultAsync(x => x.Id == correction.RecordId, cancellationToken);
        if (entity is null)
        {
            return ServiceResult.Fail("Record not found.");
        }

        var maximumYear = ProliferationYearPolicy.GetMaximumYear(_clock.UtcNow);
        var correctedYear = correction.CorrectedYear ?? entity.Year;
        var correctedQuantity = correction.CorrectedQuantity ?? entity.TotalQuantity;
        if (correctedYear < ProliferationYearPolicy.MinimumYear || correctedYear > maximumYear)
        {
            return ServiceResult.Fail($"Corrected year must be between {ProliferationYearPolicy.MinimumYear} and {maximumYear}.");
        }
        if (correctedQuantity < 0)
        {
            return ServiceResult.Fail("Annual quantity cannot be negative.");
        }

        if (entity.ApprovalStatus == ApprovalStatus.Approved)
        {
            var duplicate = await _db.ProliferationYearlies
                .AsNoTracking()
                .AnyAsync(x =>
                    x.Id != entity.Id &&
                    x.ProjectId == entity.ProjectId &&
                    x.Source == entity.Source &&
                    x.Year == correctedYear &&
                    x.ApprovalStatus == ApprovalStatus.Approved,
                    cancellationToken);
            if (duplicate)
            {
                return ServiceResult.Fail("An approved annual record already exists for the corrected project, source and year.");
            }
        }

        var oldYear = entity.Year;
        var oldQuantity = entity.TotalQuantity;
        entity.Year = correctedYear;
        entity.TotalQuantity = correctedQuantity;
        entity.LastUpdatedOnUtc = _clock.UtcNow.UtcDateTime;
        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Fail("The record was modified by another user. Refresh and try again.");
        }
        catch (DbUpdateException)
        {
            return ServiceResult.Fail("The corrected value conflicts with another proliferation record. Refresh and review the related records.");
        }

        await Audit.Events
            .ProliferationDataQualityCorrected(
                entity.Id,
                "yearly",
                entity.ProjectId,
                entity.Source,
                oldYear.ToString(CultureInfo.InvariantCulture),
                entity.Year.ToString(CultureInfo.InvariantCulture),
                oldQuantity.ToString(CultureInfo.InvariantCulture),
                entity.TotalQuantity.ToString(CultureInfo.InvariantCulture),
                reason,
                actor.Id)
            .WriteAsync(_audit, userName: actor.UserName);

        return ServiceResult.Ok(entity.Id);
    }

    private async Task<ServiceResult> CorrectGranularAsync(
        ProliferationDataQualityCorrection correction,
        byte[] rowVersion,
        string reason,
        ApplicationUser actor,
        CancellationToken cancellationToken)
    {
        var entity = await _db.ProliferationGranularEntries.FirstOrDefaultAsync(x => x.Id == correction.RecordId, cancellationToken);
        if (entity is null)
        {
            return ServiceResult.Fail("Record not found.");
        }

        var correctedDate = correction.CorrectedDate ?? entity.ProliferationDate;
        var correctedUnit = Normalize(correction.CorrectedUnitName ?? entity.UnitName, 200);
        var correctedQuantity = correction.CorrectedQuantity ?? entity.Quantity;
        var minimumDate = new DateOnly(ProliferationYearPolicy.MinimumYear, 1, 1);
        var maximumDate = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime.AddDays(30));

        if (correctedDate < minimumDate || correctedDate > maximumDate)
        {
            return ServiceResult.Fail($"Corrected date must be between {minimumDate:dd MMM yyyy} and {maximumDate:dd MMM yyyy}.");
        }
        if (string.IsNullOrWhiteSpace(correctedUnit))
        {
            return ServiceResult.Fail("Receiving unit is required.");
        }
        if (correctedQuantity <= 0)
        {
            return ServiceResult.Fail("Detailed quantity must be greater than zero.");
        }

        var oldValue = $"{entity.ProliferationDate:yyyy-MM-dd}|{entity.UnitName}|{entity.Quantity}";
        entity.ProliferationDate = correctedDate;
        entity.UnitName = correctedUnit;
        entity.Quantity = correctedQuantity;
        entity.LastUpdatedOnUtc = _clock.UtcNow.UtcDateTime;
        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = rowVersion;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Fail("The record was modified by another user. Refresh and try again.");
        }
        catch (DbUpdateException)
        {
            return ServiceResult.Fail("The corrected value conflicts with another proliferation record. Refresh and review the related records.");
        }

        var newValue = $"{entity.ProliferationDate:yyyy-MM-dd}|{entity.UnitName}|{entity.Quantity}";
        await Audit.Events
            .ProliferationDataQualityCorrected(
                entity.Id,
                "granular",
                entity.ProjectId,
                entity.Source,
                oldValue,
                newValue,
                null,
                null,
                reason,
                actor.Id)
            .WriteAsync(_audit, userName: actor.UserName);

        return ServiceResult.Ok(entity.Id);
    }

    private static string EncodeRowVersion(byte[]? rowVersion) =>
        rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : string.Empty;

    private static bool TryDecodeRowVersion(string? encoded, out byte[] rowVersion)
    {
        rowVersion = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        try
        {
            rowVersion = Convert.FromBase64String(encoded);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
