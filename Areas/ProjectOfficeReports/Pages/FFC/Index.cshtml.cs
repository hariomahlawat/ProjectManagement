using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.FFC;

[Authorize]
public class IndexModel : FfcRecordListPageModel
{
    public IndexModel(ApplicationDbContext db)
        : base(db)
    {
    }

    public bool CanManageRecords { get; private set; }

    public IReadOnlyList<SelectListItem> CountryOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> YearOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<MilestoneFilterOption> MilestoneOptions { get; private set; } = Array.Empty<MilestoneFilterOption>();

    public IReadOnlyDictionary<int, ProjectLifecycleStatus> RollupProjectStatus { get; private set; }
        = new Dictionary<int, ProjectLifecycleStatus>();

    public IReadOnlyDictionary<int, string?> RollupProjectExternalRemark { get; private set; }
        = new Dictionary<int, string?>();

    public IReadOnlyDictionary<int, string?> RollupProjectStageSummary { get; private set; }
        = new Dictionary<int, string?>();

    public async Task OnGetAsync()
    {
        CanManageRecords = User.IsInRole("Admin") || User.IsInRole("HoD");
        await LoadFilterOptionsAsync();
        await LoadRecordsAsync();

        await LoadProjectRollupMetadataAsync();
    }

    protected override IQueryable<FfcRecord> ApplyRecordFilters(IQueryable<FfcRecord> queryable)
        => queryable.Where(record => !record.IsDeleted);

    protected override IQueryable<FfcRecord> ApplyOrdering(IQueryable<FfcRecord> queryable)
    {

        return queryable
            .OrderByDescending(record => record.Year)
            .ThenBy(record => record.Country!.Name);
    }

    public Dictionary<string, string?> BuildRouteWithoutSort(
        int? page = null,
        string? query = null,
        short? year = null,
        long? countryId = null,
        MilestoneFilterState? ipa = null,
        MilestoneFilterState? gsl = null,
        MilestoneFilterState? delivery = null,
        MilestoneFilterState? installation = null)
    {
        var values = BuildRoute(
            page: page,
            sort: null,
            dir: null,
            query: query,
            year: year,
            countryId: countryId,
            ipa: ipa,
            gsl: gsl,
            delivery: delivery,
            installation: installation);

        values.Remove("sort");
        values.Remove("dir");

        return values;
    }

    public string GetMilestoneLabel(MilestoneFilterState state)
    {
        var option = MilestoneOptions.FirstOrDefault(item => item.Value == state);
        return option?.Label ?? state.ToString();
    }

    private async Task LoadProjectRollupMetadataAsync()
    {
        var linkedProjectIds = Records
            .SelectMany(record => record.Projects)
            .Where(project => project.LinkedProjectId.HasValue)
            .Select(project => project.LinkedProjectId.Value)
            .Distinct()
            .ToArray();

        if (linkedProjectIds.Length == 0)
        {
            RollupProjectStatus = new Dictionary<int, ProjectLifecycleStatus>();
            RollupProjectExternalRemark = new Dictionary<int, string?>();
            RollupProjectStageSummary = new Dictionary<int, string?>();
            return;
        }

        var projectSnapshots = await Db.Projects
            .AsNoTracking()
            .Where(project => linkedProjectIds.Contains(project.Id))
            .Select(project => new
            {
                project.Id,
                project.LifecycleStatus,
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
            .ToListAsync();

        RollupProjectStatus = projectSnapshots
            .ToDictionary(x => x.Id, x => x.LifecycleStatus);

        RollupProjectStageSummary = projectSnapshots
            .ToDictionary(
                x => x.Id,
                x => BuildStageSummary(x.Stages.Select(stage => new ProjectStage
                {
                    StageCode = stage.StageCode,
                    SortOrder = stage.SortOrder,
                    Status = stage.Status,
                    CompletedOn = stage.CompletedOn
                })));

        var externalRemarks = await Db.Remarks
            .AsNoTracking()
            .Where(remark => linkedProjectIds.Contains(remark.ProjectId)
                             && !remark.IsDeleted
                             && remark.Type == RemarkType.External)
            .Select(remark => new { remark.ProjectId, remark.Id, remark.CreatedAtUtc, remark.Body })
            .ToListAsync();

        RollupProjectExternalRemark = externalRemarks
            .GroupBy(remark => remark.ProjectId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var last = group
                        .OrderByDescending(item => item.CreatedAtUtc)
                        .ThenByDescending(item => item.Id)
                        .First();

                    var body = (last.Body ?? string.Empty).Trim()
                        .Replace("\r", " ", StringComparison.Ordinal)
                        .Replace("\n", " ", StringComparison.Ordinal);

                    const int limit = 120;
                    string? summary = body.Length <= limit
                        ? body
                        : string.Concat(body.AsSpan(0, limit), "…");

                    return summary;
                });
    }

    private static string? BuildStageSummary(IEnumerable<ProjectStage> projectStages)
    {
        static string FmtDate(DateOnly? d) => d.HasValue ? d.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "";

        var stages = projectStages?
            .Where(s => !StageCodes.IsTot(s.StageCode))
            .OrderBy(s => s.SortOrder)
            .ToList() ?? new List<ProjectStage>();

        if (stages.Count == 0)
            return null;

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
            var prev = stages.LastOrDefault(s => s.SortOrder < started.SortOrder && s.Status == StageStatus.Completed);
            var prevLabel = prev is null ? null : StageCodes.DisplayNameOf(prev.StageCode);
            var prevDate = prev is null ? "" : FmtDate(prev.CompletedOn);
            var nowLabel = StageCodes.DisplayNameOf(started.StageCode);
            var nowState = started.Status == StageStatus.Blocked ? "Blocked" : "In progress";
            var missedPart = missed.Length > 0 ? $" — missed: {string.Join(", ", missed)}" : string.Empty;

            if (prevLabel is null)
                return $"Now: {nowLabel} ({nowState}){missedPart}";

            var prevPart = string.IsNullOrEmpty(prevDate) ? prevLabel : $"{prevLabel} ({prevDate})";
            return $"Last completed: {prevPart} · Now: {nowLabel} ({nowState}){missedPart}";
        }

        if (topCompleted is not null)
        {
            var topLabel = StageCodes.DisplayNameOf(topCompleted.StageCode);
            var topDate = FmtDate(topCompleted.CompletedOn);
            var topPart = string.IsNullOrEmpty(topDate) ? topLabel : $"{topLabel} ({topDate})";

            var next = stages.FirstOrDefault(s => s.SortOrder > topCompleted.SortOrder);
            string nextPart = string.Empty;
            if (next is not null)
            {
                var nextLabel = StageCodes.DisplayNameOf(next.StageCode);
                var suffix = next.Status switch
                {
                    StageStatus.InProgress => " (Started)",
                    StageStatus.Blocked => " (Blocked)",
                    _ => " (Not started)"
                };
                nextPart = $" · Next: {nextLabel}{suffix}";
            }

            var missedPart = missed.Length > 0 ? $" — missed: {string.Join(", ", missed)}" : string.Empty;
            return $"Last completed: {topPart}{nextPart}{missedPart}";
        }

        var firstDefined = stages.FirstOrDefault();
        return firstDefined is null ? null : $"Not started · First stage: {StageCodes.DisplayNameOf(firstDefined.StageCode)}";
    }

    private async Task LoadFilterOptionsAsync()
    {
        var activeRecords = Db.FfcRecords
            .AsNoTracking()
            .Where(record => !record.IsDeleted);

        var years = await activeRecords
            .Select(record => record.Year)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync();

        YearOptions = years
            .Select(year => new SelectListItem
            {
                Value = year.ToString(CultureInfo.InvariantCulture),
                Text = year.ToString(CultureInfo.InvariantCulture),
                Selected = Year.HasValue && Year.GetValueOrDefault() == year
            })
            .ToList();

        var countries = await Db.FfcCountries
            .AsNoTracking()
            .Where(country => country.IsActive)
            .OrderBy(country => country.Name)
            .Select(country => new { country.Id, country.Name })
            .ToListAsync();

        CountryOptions = countries
            .Select(country => new SelectListItem
            {
                Value = country.Id.ToString(CultureInfo.InvariantCulture),
                Text = country.Name,
                Selected = CountryId.HasValue && CountryId.Value == country.Id
            })
            .ToList();

        MilestoneOptions = BuildMilestoneOptions();
    }

    private static IReadOnlyList<MilestoneFilterOption> BuildMilestoneOptions()
        => new List<MilestoneFilterOption>
        {
            new(MilestoneFilterState.Any, "Any status"),
            new(MilestoneFilterState.Completed, "Completed"),
            new(MilestoneFilterState.Pending, "Pending")
        };

    public sealed record MilestoneFilterOption(MilestoneFilterState Value, string Label)
    {
        public string QueryValue => Value.ToString().ToLowerInvariant();
    }
}
