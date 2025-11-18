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

    public MapTableDetailedModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public sealed record RowDto(
        string CountryIso3,
        string CountryName,
        string ProjectName,
        bool IsLinked,
        int Quantity,
        string Bucket,
        string LatestStage,
        string ExternalRemark
    );

    public async Task<IActionResult> OnGetDataAsync(CancellationToken cancellationToken)
    {
        var rows = await LoadAsync(cancellationToken);
        return new JsonResult(rows);
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        var rows = await LoadAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("FFC Projects (Detailed)");

        worksheet.Cell(1, 1).Value = "Country";
        worksheet.Cell(1, 2).Value = "ISO3";
        worksheet.Cell(1, 3).Value = "Project";
        worksheet.Cell(1, 4).Value = "Linked?";
        worksheet.Cell(1, 5).Value = "Quantity";
        worksheet.Cell(1, 6).Value = "Bucket";
        worksheet.Cell(1, 7).Value = "Latest stage";
        worksheet.Cell(1, 8).Value = "External remark";

        var rowIndex = 2;
        foreach (var row in rows)
        {
            worksheet.Cell(rowIndex, 1).Value = row.CountryName;
            worksheet.Cell(rowIndex, 2).Value = row.CountryIso3;
            worksheet.Cell(rowIndex, 3).Value = row.ProjectName;
            worksheet.Cell(rowIndex, 4).Value = row.IsLinked ? "Yes" : "No";
            worksheet.Cell(rowIndex, 5).Value = row.Quantity;
            worksheet.Cell(rowIndex, 6).Value = row.Bucket;
            worksheet.Cell(rowIndex, 7).Value = row.LatestStage;
            worksheet.Cell(rowIndex, 8).Value = row.ExternalRemark;
            rowIndex++;
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var fileName = $"FFC_Projects_Detailed_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private async Task<List<RowDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var projects = await _db.FfcProjects
            .AsNoTracking()
            .Where(project => !project.Record.IsDeleted && project.Record.Country.IsActive)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.Remarks,
                project.Quantity,
                project.IsDelivered,
                project.IsInstalled,
                project.LinkedProjectId,
                LinkedProjectName = project.LinkedProject != null ? project.LinkedProject.Name : null,
                CountryIso3 = project.Record.Country.IsoCode,
                CountryName = project.Record.Country.Name
            })
            .ToListAsync(cancellationToken);

        var linkedProjectIds = projects
            .Where(row => row.LinkedProjectId.HasValue)
            .Select(row => row.LinkedProjectId!.Value)
            .Distinct()
            .ToArray();

        var projectNameMap = new Dictionary<int, string>(linkedProjectIds.Length);
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
            ? new Dictionary<int, string>()
            : await LoadRemarkSummariesAsync(linkedProjectIds, cancellationToken);

        var rows = projects
            .Select(row =>
            {
                var (bucket, quantity) = FfcProjectBucketHelper.Classify(row.IsInstalled, row.IsDelivered, row.Quantity);
                var bucketLabel = FfcProjectBucketHelper.GetBucketLabel(bucket);
                var iso = (row.CountryIso3 ?? string.Empty).ToUpperInvariant();
                var name = row.CountryName ?? string.Empty;
                var isLinked = row.LinkedProjectId.HasValue;
                var projectName = row.Name ?? string.Empty;
                var stageSummary = string.Empty;
                var remark = string.Empty;

                if (isLinked)
                {
                    var linkedId = row.LinkedProjectId!.Value;
                    if (projectNameMap.TryGetValue(linkedId, out var linkedName) && !string.IsNullOrWhiteSpace(linkedName))
                    {
                        projectName = linkedName;
                    }
                    else if (!string.IsNullOrWhiteSpace(row.LinkedProjectName))
                    {
                        projectName = row.LinkedProjectName!;
                    }

                    stageSummary = stageSummaryMap.TryGetValue(linkedId, out var summary) ? summary ?? string.Empty : string.Empty;
                    remark = remarkMap.TryGetValue(linkedId, out var text) ? text : string.Empty;
                }
                else
                {
                    remark = FormatRemark(row.Remarks);
                }

                return new RowDto(
                    iso,
                    name,
                    projectName,
                    isLinked,
                    quantity,
                    bucketLabel,
                    stageSummary,
                    remark);
            })
            .OrderBy(row => BucketOrder(row.Bucket))
            .ThenBy(row => row.CountryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return rows;
    }

    private static int BucketOrder(string bucket) => bucket switch
    {
        "Installed" => 0,
        "Delivered (not installed)" => 1,
        "Planned" => 2,
        _ => 4
    };

    private static string FormatRemark(string? remark)
    {
        if (string.IsNullOrWhiteSpace(remark))
        {
            return string.Empty;
        }

        var text = remark.Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        const int limit = 120;
        return text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit), "…");
    }

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

    private async Task<Dictionary<int, string>> LoadRemarkSummariesAsync(int[] projectIds, CancellationToken cancellationToken)
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
