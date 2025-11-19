using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public class MapTableDetailedModel : PageModel
{
    private readonly ApplicationDbContext _db;

    // SECTION: Construction
    public MapTableDetailedModel(ApplicationDbContext db)
    {
        _db = db;
    }

    // SECTION: DTO contracts
    public sealed record FfcProjectDetailRowDto(
        int SerialNumber,
        string ProjectName,
        bool IsLinked,
        decimal? CostInCr,
        int Quantity,
        string Bucket,
        string? ProgressRemark
    );

    public sealed record FfcRecordDetailGroupDto(
        string CountryName,
        string CountryIso3,
        int Year,
        string? OverallRemarks,
        IReadOnlyList<FfcProjectDetailRowDto> Projects
    );

    // SECTION: Internal projections
    private sealed record FfcProjectProjection(
        long Id,
        string? Name,
        string? Remarks,
        int Quantity,
        bool IsDelivered,
        bool IsInstalled,
        DateOnly? DeliveredOn,
        DateOnly? InstalledOn,
        int? LinkedProjectId,
        string? LinkedProjectName,
        long RecordId,
        int Year,
        string? OverallRemarks,
        string? CountryIso3,
        string? CountryName
    );

    // SECTION: Request handlers
    public async Task<IActionResult> OnGetDataAsync(CancellationToken cancellationToken)
    {
        var groups = await LoadAsync(cancellationToken);
        return new JsonResult(groups);
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        var groups = await LoadAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("FFC Projects (Detailed)");

        // SECTION: Header row
        var columnIndex = 1;
        worksheet.Cell(1, columnIndex++).Value = "Country";
        worksheet.Cell(1, columnIndex++).Value = "ISO3";
        worksheet.Cell(1, columnIndex++).Value = "Year";
        worksheet.Cell(1, columnIndex++).Value = "S. No.";
        worksheet.Cell(1, columnIndex++).Value = "Project";
        worksheet.Cell(1, columnIndex++).Value = "Cost (₹ Cr)";
        worksheet.Cell(1, columnIndex++).Value = "Quantity";
        worksheet.Cell(1, columnIndex++).Value = "Status";
        worksheet.Cell(1, columnIndex++).Value = "Progress / present status";
        worksheet.Cell(1, columnIndex++).Value = "Overall remarks";

        var rowIndex = 2;
        foreach (var group in groups
            .OrderByDescending(g => g.Year)
            .ThenBy(g => g.CountryName, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Projects is null || group.Projects.Count == 0)
            {
                continue;
            }

            foreach (var project in group.Projects)
            {
                columnIndex = 1;

                worksheet.Cell(rowIndex, columnIndex++).Value = group.CountryName;
                worksheet.Cell(rowIndex, columnIndex++).Value = group.CountryIso3;
                worksheet.Cell(rowIndex, columnIndex++).Value = group.Year;
                worksheet.Cell(rowIndex, columnIndex++).Value = project.SerialNumber;
                worksheet.Cell(rowIndex, columnIndex++).Value = project.ProjectName;

                if (project.CostInCr.HasValue)
                {
                    worksheet.Cell(rowIndex, columnIndex).Value = project.CostInCr.Value;
                    worksheet.Cell(rowIndex, columnIndex).Style.NumberFormat.Format = "0.00";
                }
                columnIndex++;

                worksheet.Cell(rowIndex, columnIndex++).Value = project.Quantity;
                worksheet.Cell(rowIndex, columnIndex++).Value = project.Bucket;
                worksheet.Cell(rowIndex, columnIndex++).Value = project.ProgressRemark ?? string.Empty;
                worksheet.Cell(rowIndex, columnIndex++).Value = group.OverallRemarks ?? string.Empty;

                rowIndex++;
            }

            rowIndex++;
        }

        // SECTION: Styling
        var headerRange = worksheet.Range(1, 1, 1, 10);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f3f4f6");
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var fileName = $"FFC_Projects_Detailed_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
        return File(
            fileContents: stream.ToArray(),
            contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileDownloadName: fileName);
    }

    // SECTION: Data shaping
    private async Task<List<FfcRecordDetailGroupDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var projects = await _db.FfcProjects
            .AsNoTracking()
            .Where(project => !project.Record.IsDeleted && project.Record.Country.IsActive)
            .Select(project => new FfcProjectProjection(
                project.Id,
                project.Name,
                project.Remarks,
                project.Quantity,
                project.IsDelivered,
                project.IsInstalled,
                project.DeliveredOn,
                project.InstalledOn,
                project.LinkedProjectId,
                project.LinkedProject != null ? project.LinkedProject.Name : null,
                project.Record.Id,
                project.Record.Year,
                project.Record.OverallRemarks,
                project.Record.Country.IsoCode,
                project.Record.Country.Name))
            .ToListAsync(cancellationToken);

        if (projects.Count == 0)
        {
            return new List<FfcRecordDetailGroupDto>();
        }

        var linkedProjectIds = projects
            .Where(row => row.LinkedProjectId.HasValue)
            .Select(row => row.LinkedProjectId!.Value)
            .Distinct()
            .ToArray();

        var projectNameMap = new Dictionary<int, string>(linkedProjectIds.Length);
        var projectCostMap = new Dictionary<int, decimal?>(linkedProjectIds.Length);
        var stageSummaryMap = new Dictionary<int, string?>(linkedProjectIds.Length);

        if (linkedProjectIds.Length > 0)
        {
            var projectSnapshots = await _db.Projects
                .AsNoTracking()
                .Where(project => linkedProjectIds.Contains(project.Id))
                .Select(project => new
                {
                    project.Id,
                    project.Name,
                    project.CostLakhs,
                    Stages = project.ProjectStages
                        .Select(stage => new
                        {
                            stage.StageCode,
                            stage.SortOrder,
                            stage.Status,
                            stage.CompletedOn
                        })
                        .ToList()
                })
                .ToListAsync(cancellationToken);

            projectNameMap = projectSnapshots
                .ToDictionary(x => x.Id, x => x.Name ?? string.Empty);

            projectCostMap = projectSnapshots
                .ToDictionary(x => x.Id, x => ConvertLakhsToCr(x.CostLakhs));

            stageSummaryMap = projectSnapshots
                .ToDictionary(
                    x => x.Id,
                    x => BuildStageSummary(x.Stages.Select(stage => new ProjectStage
                    {
                        StageCode = stage.StageCode,
                        SortOrder = stage.SortOrder,
                        Status = stage.Status,
                        CompletedOn = stage.CompletedOn
                    })));
        }

        var remarkMap = linkedProjectIds.Length == 0
            ? new Dictionary<int, string?>()
            : await LoadRemarkSummariesAsync(linkedProjectIds, cancellationToken);

        var groups = projects
            .GroupBy(project => new
            {
                project.RecordId,
                project.Year,
                project.CountryName,
                project.CountryIso3,
                project.OverallRemarks
            })
            .OrderByDescending(group => group.Key.Year)
            .ThenBy(group => group.Key.CountryName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<FfcRecordDetailGroupDto>(groups.Count);

        foreach (var group in groups)
        {
            var projectRows = group
                .OrderBy(project => project.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select((project, index) =>
                {
                    var (bucket, quantity) = FfcProjectBucketHelper.Classify(project.IsInstalled, project.IsDelivered, project.Quantity);
                    var bucketLabel = FfcProjectBucketHelper.GetBucketLabel(bucket);
                    var effectiveName = ResolveProjectName(project, projectNameMap);
                    var costInCr = ResolveProjectCost(project.LinkedProjectId, projectCostMap);
                    var progressRemark = BuildProgressRemark(project, bucket, stageSummaryMap, remarkMap);

                    return new FfcProjectDetailRowDto(
                        SerialNumber: index + 1,
                        ProjectName: effectiveName,
                        IsLinked: project.LinkedProjectId.HasValue,
                        CostInCr: costInCr,
                        Quantity: quantity,
                        Bucket: bucketLabel,
                        ProgressRemark: progressRemark
                    );
                })
                .ToList();

            if (projectRows.Count == 0)
            {
                continue;
            }

            result.Add(new FfcRecordDetailGroupDto(
                CountryName: group.Key.CountryName ?? string.Empty,
                CountryIso3: (group.Key.CountryIso3 ?? string.Empty).ToUpperInvariant(),
                Year: group.Key.Year,
                OverallRemarks: FormatRemark(group.Key.OverallRemarks),
                Projects: projectRows
            ));
        }

        return result;
    }

    // SECTION: Projection helpers
    private static string ResolveProjectName(FfcProjectProjection project, IReadOnlyDictionary<int, string> projectNameMap)
    {
        var displayName = project.Name ?? string.Empty;
        if (project.LinkedProjectId is int linkedId)
        {
            if (projectNameMap.TryGetValue(linkedId, out var linkedName) && !string.IsNullOrWhiteSpace(linkedName))
            {
                return linkedName;
            }

            if (!string.IsNullOrWhiteSpace(project.LinkedProjectName))
            {
                return project.LinkedProjectName;
            }
        }

        return displayName;
    }

    private static decimal? ResolveProjectCost(int? linkedProjectId, IReadOnlyDictionary<int, decimal?> costMap)
    {
        if (linkedProjectId is not int id)
        {
            return null;
        }

        return costMap.TryGetValue(id, out var cost) ? cost : null;
    }

    private static string? BuildProgressRemark(
        FfcProjectProjection project,
        FfcDeliveryBucket bucket,
        IReadOnlyDictionary<int, string?> stageSummaryMap,
        IReadOnlyDictionary<int, string?> remarkMap)
    {
        string? remarkFromFfc = FormatRemark(project.Remarks);

        if (bucket == FfcDeliveryBucket.Planned)
        {
            if (project.LinkedProjectId is int linkedId && TryGetNonEmpty(remarkMap, linkedId, out var externalRemark))
            {
                return externalRemark;
            }

            return remarkFromFfc;
        }

        if (project.LinkedProjectId is int deliveredId)
        {
            if (TryGetNonEmpty(stageSummaryMap, deliveredId, out var stageSummary))
            {
                return stageSummary;
            }

            if (TryGetNonEmpty(remarkMap, deliveredId, out var externalRemark))
            {
                return externalRemark;
            }
        }

        if (bucket == FfcDeliveryBucket.Installed && project.InstalledOn is DateOnly installedOn)
        {
            return $"Installed on {FormatDate(installedOn)}";
        }

        if (bucket == FfcDeliveryBucket.DeliveredNotInstalled && project.DeliveredOn is DateOnly deliveredOn)
        {
            return $"Delivered on {FormatDate(deliveredOn)}";
        }

        return remarkFromFfc;
    }

    private static bool TryGetNonEmpty(IReadOnlyDictionary<int, string?> source, int key, out string value)
    {
        if (source.TryGetValue(key, out var raw) && raw is not null)
        {
            var text = raw;
            if (!string.IsNullOrWhiteSpace(text))
            {
                value = text;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static decimal? ConvertLakhsToCr(decimal? costLakhs)
    {
        if (!costLakhs.HasValue)
        {
            return null;
        }

        return decimal.Divide(costLakhs.Value, 100m);
    }

    private static string FormatRemark(string? remark)
    {
        if (string.IsNullOrWhiteSpace(remark))
        {
            return string.Empty;
        }

        var text = remark.Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        const int limit = 200;
        return text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit), "…");
    }

    private static string FormatDate(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string? BuildStageSummary(IEnumerable<ProjectStage> projectStages)
    {
        static string FmtDate(DateOnly? value) => value?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? string.Empty;

        var stages = projectStages?
            .Where(s => !StageCodes.IsTot(s.StageCode))
            .OrderBy(s => s.SortOrder)
            .ToList() ?? new List<ProjectStage>();

        if (stages.Count == 0)
        {
            return null;
        }

        var paymentStage = stages.FirstOrDefault(s => StageCodes.IsPayment(s.StageCode));
        if (paymentStage is not null)
        {
            var cutoff = paymentStage.SortOrder;
            stages = stages.Where(s => s.SortOrder <= cutoff).ToList();
        }

        var topCompleted = stages
            .Where(s => s.Status == StageStatus.Completed)
            .OrderByDescending(s => s.SortOrder)
            .ThenByDescending(s => s.CompletedOn ?? DateOnly.MinValue)
            .FirstOrDefault();

        var started = stages.FirstOrDefault(s => s.Status is StageStatus.InProgress or StageStatus.Blocked);

        var missed = topCompleted is null
            ? Array.Empty<string>()
            : stages
                .Where(s => s.SortOrder < topCompleted.SortOrder && s.Status != StageStatus.Completed)
                .Select(s => StageCodes.DisplayNameOf(s.StageCode))
                .ToArray();

        if (started is not null)
        {
            var previous = stages.LastOrDefault(s => s.SortOrder < started.SortOrder && s.Status == StageStatus.Completed);
            var previousLabel = previous is null ? null : StageCodes.DisplayNameOf(previous.StageCode);
            var previousDate = previous is null ? string.Empty : FmtDate(previous.CompletedOn);
            var nowLabel = StageCodes.DisplayNameOf(started.StageCode);
            var nowState = started.Status == StageStatus.Blocked ? "Blocked" : "In progress";
            var missedPart = missed.Length > 0 ? $" — missed: {string.Join(", ", missed)}" : string.Empty;

            if (!string.IsNullOrWhiteSpace(previousLabel))
            {
                return $"Last: {previousLabel} ({previousDate}) → {nowLabel} ({nowState}){missedPart}";
            }

            return $"Now: {nowLabel} ({nowState}){missedPart}";
        }

        if (topCompleted is null)
        {
            return null;
        }

        var topLabel = StageCodes.DisplayNameOf(topCompleted.StageCode);
        var topDate = FmtDate(topCompleted.CompletedOn);
        var trailing = missed.Length > 0 ? $" — pending: {string.Join(", ", missed)}" : string.Empty;

        return $"Completed: {topLabel} ({topDate}){trailing}";
    }

    // SECTION: External remarks
    private async Task<Dictionary<int, string?>> LoadRemarkSummariesAsync(int[] projectIds, CancellationToken cancellationToken)
    {
        var remarks = await _db.Remarks
            .AsNoTracking()
            .Where(remark => projectIds.Contains(remark.ProjectId)
                && !remark.IsDeleted
                && remark.Type == RemarkType.External)
            .Select(remark => new
            {
                remark.ProjectId,
                remark.Id,
                remark.CreatedAtUtc,
                remark.Body
            })
            .ToListAsync(cancellationToken);

        return remarks
            .GroupBy(remark => remark.ProjectId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group
                        .OrderByDescending(item => item.CreatedAtUtc)
                        .ThenByDescending(item => item.Id)
                        .FirstOrDefault();

                    return latest is null ? string.Empty : FormatRemark(latest.Body);
                });
    }
}
