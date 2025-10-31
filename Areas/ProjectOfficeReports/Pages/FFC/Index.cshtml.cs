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

    public async Task OnGetAsync()
    {
        CanManageRecords = User.IsInRole("Admin") || User.IsInRole("HoD");
        await LoadFilterOptionsAsync();
        await LoadRecordsAsync();
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
                Selected = Year.HasValue && Year.Value == year
            })
            .ToList();

        var countries = await Db.FfcCountries
            .AsNoTracking()
            .Where(country => country.Records.Any(record => !record.IsDeleted))
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
