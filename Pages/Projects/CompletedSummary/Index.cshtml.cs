using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Projects;
using ProjectManagement.Services;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Pages.Projects.CompletedSummary;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly CompletedProjectsSummaryService _summaryService;
    private readonly ICompletedProjectsSummaryExcelBuilder _excelBuilder;
    private readonly IClock _clock;
    private readonly ApplicationDbContext _db;

    public IndexModel(
        CompletedProjectsSummaryService summaryService,
        ICompletedProjectsSummaryExcelBuilder excelBuilder,
        IClock clock,
        ApplicationDbContext db)
    {
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
        _excelBuilder = excelBuilder ?? throw new ArgumentNullException(nameof(excelBuilder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // SECTION: Filter inputs
    [BindProperty(SupportsGet = true)]
    public int? TechnicalCategoryId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TechStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool? AvailableForProliferation { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool? TotCompleted { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? CompletedYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Build { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PortfolioStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? WorkspaceView { get; set; }

    // SECTION: Sorting inputs
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    // SECTION: Filter option lists
    public IReadOnlyList<SelectListItem> TechnicalCategoryOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> TechStatusOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> TotStatusOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> PortfolioStatusOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<SelectListItem> CompletedYearOptions { get; private set; } = Array.Empty<SelectListItem>();
    public IReadOnlyList<ActiveCompletedProjectFilter> ActiveFilters { get; private set; } = Array.Empty<ActiveCompletedProjectFilter>();

    public IReadOnlyList<SelectListItem> AvailabilityOptions { get; } = new[]
    {
        new SelectListItem("All", string.Empty),
        new SelectListItem("Yes", "true"),
        new SelectListItem("No", "false")
    };

    // SECTION: Page read models
    public IReadOnlyList<CompletedProjectSummaryDto> Items { get; private set; } = Array.Empty<CompletedProjectSummaryDto>();
    public CompletedProjectsPortfolioOverview Overview { get; private set; } = CompletedProjectsPortfolioOverview.Empty;

    // SECTION: Filter state metadata
    public int ActiveFilterCount { get; private set; }
    public bool HasActiveFilters => ActiveFilterCount > 0;
    public bool CanEdit { get; private set; }

    private enum BuildFilter
    {
        New,
        Rebuild
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadTechnicalCategoriesAsync(cancellationToken);
        var completionYears = await _summaryService.GetCompletionYearsAsync(cancellationToken);

        NormaliseFilters();
        NormaliseSorting();
        UpdateActiveFilterCount();
        BuildOptionLists(completionYears);
        BuildActiveFilters();

        Items = await LoadItemsAsync(cancellationToken);
        var currentYear = TimeZoneInfo.ConvertTime(_clock.UtcNow, TimeZoneHelper.GetIst()).Year;
        Overview = CompletedProjectsPortfolioOverview.Build(Items, currentYear);

        CanEdit = User.IsInRole("Admin")
                  || User.IsInRole("HoD")
                  || User.IsInRole("Project Office");
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
    {
        NormaliseFilters();
        NormaliseSorting();

        var items = await LoadItemsAsync(cancellationToken);
        var generatedAtUtc = _clock.UtcNow;
        var technicalCategoryName = await ResolveTechnicalCategoryNameAsync(cancellationToken);

        var workbook = _excelBuilder.Build(
            new CompletedProjectsSummaryExportContext(
                items,
                generatedAtUtc,
                technicalCategoryName,
                TechStatus,
                AvailableForProliferation,
                TotCompleted,
                CompletedYear,
                Search,
                Build,
                PortfolioStatus));

        var fileName = $"completed-projects-summary-{generatedAtUtc:yyyyMMddHHmmss}.xlsx";
        return File(
            workbook,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    public string NextSortDirection(string sortKey)
    {
        if (string.Equals(Sort, sortKey, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(Dir, "asc", StringComparison.OrdinalIgnoreCase)
                ? "desc"
                : "asc";
        }

        return sortKey is "year" or "rd" or "prod" or "lpp" or "quality"
            ? "desc"
            : "asc";
    }

    public string GetSortIndicator(string sortKey)
    {
        if (!string.Equals(Sort, sortKey, StringComparison.OrdinalIgnoreCase))
        {
            return "↕";
        }

        return string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase) ? "▼" : "▲";
    }

    public string GetSortAria(string sortKey)
    {
        if (!string.Equals(Sort, sortKey, StringComparison.OrdinalIgnoreCase))
        {
            return "none";
        }

        return string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase)
            ? "descending"
            : "ascending";
    }

    private Task<IReadOnlyList<CompletedProjectSummaryDto>> LoadItemsAsync(CancellationToken cancellationToken) =>
        _summaryService.GetAsync(
            TechnicalCategoryId,
            TechStatus,
            AvailableForProliferation,
            TotCompleted,
            CompletedYear,
            Search,
            Build,
            PortfolioStatus,
            Sort!,
            Dir!,
            cancellationToken);

    private void NormaliseFilters()
    {
        if (!string.IsNullOrWhiteSpace(TechStatus))
        {
            TechStatus = ProjectTechStatusCodes.All.FirstOrDefault(status =>
                string.Equals(status, TechStatus.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        Build = ParseBuildFilter(Build)?.ToString();
        PortfolioStatus = CompletedProjectPortfolioStatusCodes.Normalise(PortfolioStatus);
        WorkspaceView = NormaliseWorkspaceView(WorkspaceView);

        var totCompletedRaw = Request.Query[nameof(TotCompleted)].ToString();
        if (!string.IsNullOrWhiteSpace(totCompletedRaw)
            && !bool.TryParse(totCompletedRaw, out _))
        {
            TotCompleted = null;
        }

        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
    }

    private void NormaliseSorting()
    {
        var sort = (Sort ?? string.Empty).Trim().ToLowerInvariant();
        var dir = (Dir ?? string.Empty).Trim().ToLowerInvariant();

        Sort = sort is "name" or "category" or "build" or "rd" or "prod" or "lpp" or "tech" or "avail" or "tot" or "year" or "quality"
            ? sort
            : "name";

        Dir = dir is "asc" or "desc" ? dir : "asc";
    }

    private void UpdateActiveFilterCount()
    {
        ActiveFilterCount = 0;

        if (TechnicalCategoryId.HasValue) ActiveFilterCount++;
        if (!string.IsNullOrWhiteSpace(TechStatus)) ActiveFilterCount++;
        if (!string.IsNullOrWhiteSpace(Build)) ActiveFilterCount++;
        if (AvailableForProliferation.HasValue) ActiveFilterCount++;
        if (TotCompleted.HasValue) ActiveFilterCount++;
        if (CompletedYear.HasValue) ActiveFilterCount++;
        if (!string.IsNullOrWhiteSpace(Search)) ActiveFilterCount++;
        if (!string.IsNullOrWhiteSpace(PortfolioStatus)) ActiveFilterCount++;
    }

    private void BuildOptionLists(IReadOnlyList<int> completionYears)
    {
        TechStatusOptions = BuildTechStatusOptions(TechStatus);
        TotStatusOptions = BuildTotStatusOptions(TotCompleted);
        PortfolioStatusOptions = BuildPortfolioStatusOptions(PortfolioStatus);
        CompletedYearOptions = BuildCompletedYearOptions(completionYears, CompletedYear);
    }

    private void BuildActiveFilters()
    {
        var filters = new List<ActiveCompletedProjectFilter>();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            filters.Add(new ActiveCompletedProjectFilter(nameof(Search), "Search", Search));
        }

        if (!string.IsNullOrWhiteSpace(PortfolioStatus))
        {
            filters.Add(new ActiveCompletedProjectFilter(
                nameof(PortfolioStatus),
                "Portfolio position",
                CompletedProjectPortfolioStatusCodes.GetLabel(PortfolioStatus)));
        }

        if (TechnicalCategoryId.HasValue)
        {
            var categoryName = TechnicalCategoryOptions
                .FirstOrDefault(x => string.Equals(
                    x.Value,
                    TechnicalCategoryId.Value.ToString(),
                    StringComparison.Ordinal))?.Text
                ?? $"Category {TechnicalCategoryId.Value}";

            filters.Add(new ActiveCompletedProjectFilter(
                nameof(TechnicalCategoryId),
                "Technical category",
                categoryName));
        }

        if (!string.IsNullOrWhiteSpace(TechStatus))
        {
            filters.Add(new ActiveCompletedProjectFilter(nameof(TechStatus), "Technology", TechStatus));
        }

        if (CompletedYear.HasValue)
        {
            filters.Add(new ActiveCompletedProjectFilter(
                nameof(CompletedYear),
                "Completion year",
                CompletedYear.Value.ToString()));
        }

        if (AvailableForProliferation.HasValue)
        {
            filters.Add(new ActiveCompletedProjectFilter(
                nameof(AvailableForProliferation),
                "Proliferation",
                AvailableForProliferation.Value ? "Available" : "Not available"));
        }

        if (TotCompleted.HasValue)
        {
            filters.Add(new ActiveCompletedProjectFilter(
                nameof(TotCompleted),
                "ToT",
                TotCompleted.Value ? "Completed" : "Not completed"));
        }

        if (!string.IsNullOrWhiteSpace(Build))
        {
            filters.Add(new ActiveCompletedProjectFilter(nameof(Build), "Build type", Build));
        }

        ActiveFilters = filters;
    }

    public Dictionary<string, string> GetRoutesWithoutFilter(string filterKey) =>
        BuildRouteValues(filterKey);

    public Dictionary<string, string> GetClearFilterRoutes() =>
        BuildRouteValues(
            nameof(Search),
            nameof(PortfolioStatus),
            nameof(TechnicalCategoryId),
            nameof(TechStatus),
            nameof(CompletedYear),
            nameof(AvailableForProliferation),
            nameof(TotCompleted),
            nameof(Build));

    public Dictionary<string, string> GetRoutesForPortfolioStatus(
        string portfolioStatus,
        string? sort = null,
        string? direction = null)
    {
        var normalisedStatus = CompletedProjectPortfolioStatusCodes.Normalise(portfolioStatus);
        var excludedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(PortfolioStatus)
        };

        // A headline position filter owns the direct filter dimensions used to
        // calculate it. Clear only those dimensions so the card cannot create a
        // contradictory route while unrelated scope filters remain intact.
        switch (normalisedStatus)
        {
            case CompletedProjectPortfolioStatusCodes.FullyReady:
            case CompletedProjectPortfolioStatusCodes.AvailableBlocked:
                excludedKeys.Add(nameof(AvailableForProliferation));
                excludedKeys.Add(nameof(TechStatus));
                excludedKeys.Add(nameof(TotCompleted));
                break;

            case CompletedProjectPortfolioStatusCodes.ProliferationAssessmentPending:
                excludedKeys.Add(nameof(AvailableForProliferation));
                break;

            case CompletedProjectPortfolioStatusCodes.TechnologyAction:
            case CompletedProjectPortfolioStatusCodes.TechnologyAssessmentPending:
                excludedKeys.Add(nameof(TechStatus));
                break;

            case CompletedProjectPortfolioStatusCodes.TotAction:
                excludedKeys.Add(nameof(TotCompleted));
                break;
        }

        var values = new Dictionary<string, string>(BuildRouteValues(excludedKeys.ToArray()))
        {
            [nameof(PortfolioStatus)] = normalisedStatus ?? portfolioStatus,
            [nameof(WorkspaceView)] = "register"
        };

        if (!string.IsNullOrWhiteSpace(sort))
        {
            values[nameof(Sort)] = sort;
            values[nameof(Dir)] = string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase)
                ? "asc"
                : "desc";
        }

        return values;
    }

    public Dictionary<string, string> GetRoutesForAvailability(bool available)
    {
        var values = new Dictionary<string, string>(
            BuildRouteValues(nameof(PortfolioStatus), nameof(AvailableForProliferation)))
        {
            [nameof(AvailableForProliferation)] = available.ToString().ToLowerInvariant(),
            [nameof(WorkspaceView)] = "register"
        };

        return values;
    }

    public Dictionary<string, string> GetRoutesForSort(string sortKey, string direction)
    {
        var values = new Dictionary<string, string>(BuildRouteValues(nameof(Sort), nameof(Dir)))
        {
            [nameof(Sort)] = sortKey,
            [nameof(Dir)] = direction,
            [nameof(WorkspaceView)] = "register"
        };

        return values;
    }

    private Dictionary<string, string> BuildRouteValues(params string[] excludedKeys)
    {
        var excluded = excludedKeys.ToHashSet(StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        Add(nameof(TechnicalCategoryId), TechnicalCategoryId?.ToString());
        Add(nameof(TechStatus), TechStatus);
        Add(nameof(AvailableForProliferation), AvailableForProliferation?.ToString().ToLowerInvariant());
        Add(nameof(TotCompleted), TotCompleted?.ToString().ToLowerInvariant());
        Add(nameof(CompletedYear), CompletedYear?.ToString());
        Add(nameof(Search), Search);
        Add(nameof(Build), Build);
        Add(nameof(PortfolioStatus), PortfolioStatus);
        Add(nameof(Sort), Sort);
        Add(nameof(Dir), Dir);
        Add(nameof(WorkspaceView), WorkspaceView);

        return values;

        void Add(string key, string? value)
        {
            if (excluded.Contains(key) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            values[key] = value;
        }
    }

    private static string? NormaliseWorkspaceView(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalised = value.Trim().ToLowerInvariant();
        return normalised is "register" or "overview" or "quality" ? normalised : null;
    }

    private static BuildFilter? ParseBuildFilter(string? buildValue)
    {
        if (string.IsNullOrWhiteSpace(buildValue)) return null;
        if (string.Equals(buildValue, "Rebuild", StringComparison.OrdinalIgnoreCase)) return BuildFilter.Rebuild;
        if (string.Equals(buildValue, "New", StringComparison.OrdinalIgnoreCase)) return BuildFilter.New;
        return null;
    }

    private async Task LoadTechnicalCategoriesAsync(CancellationToken cancellationToken)
    {
        var categories = await _db.TechnicalCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var items = new List<SelectListItem>
        {
            new("All", string.Empty)
        };

        foreach (var category in categories)
        {
            items.Add(new SelectListItem(
                category.Name,
                category.Id.ToString(),
                TechnicalCategoryId == category.Id));
        }

        TechnicalCategoryOptions = items;
    }

    private async Task<string?> ResolveTechnicalCategoryNameAsync(CancellationToken cancellationToken)
    {
        if (!TechnicalCategoryId.HasValue) return null;

        return await _db.TechnicalCategories
            .AsNoTracking()
            .Where(x => x.Id == TechnicalCategoryId.Value)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static IReadOnlyList<SelectListItem> BuildTechStatusOptions(string? selected)
    {
        var items = new List<SelectListItem>
        {
            new("All", string.Empty)
        };

        foreach (var status in ProjectTechStatusCodes.All)
        {
            items.Add(new SelectListItem(
                status,
                status,
                string.Equals(status, selected, StringComparison.OrdinalIgnoreCase)));
        }

        return items;
    }

    private static IReadOnlyList<SelectListItem> BuildTotStatusOptions(bool? selected)
    {
        var items = new List<SelectListItem>
        {
            new("All", string.Empty),
            new("Completed", "true"),
            new("Not completed", "false")
        };

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Value))
            {
                item.Selected = selected is null;
            }
            else if (bool.TryParse(item.Value, out var value))
            {
                item.Selected = selected == value;
            }
        }

        return items;
    }

    private static IReadOnlyList<SelectListItem> BuildCompletedYearOptions(
        IReadOnlyList<int> completionYears,
        int? selected)
    {
        var items = new List<SelectListItem>
        {
            new("All years", string.Empty, selected is null)
        };

        foreach (var year in completionYears)
        {
            items.Add(new SelectListItem(
                year.ToString(),
                year.ToString(),
                selected == year));
        }

        if (selected.HasValue && completionYears.All(year => year != selected.Value))
        {
            items.Add(new SelectListItem(
                selected.Value.ToString(),
                selected.Value.ToString(),
                true));
        }

        return items;
    }

    private static IReadOnlyList<SelectListItem> BuildPortfolioStatusOptions(string? selected)
    {
        var options = new (string Value, string Label)[]
        {
            (string.Empty, "All"),
            (CompletedProjectPortfolioStatusCodes.ProliferationAssessmentPending, "Proliferation assessment pending"),
            (CompletedProjectPortfolioStatusCodes.AvailableBlocked, "Available but blocked"),
            (CompletedProjectPortfolioStatusCodes.FullyReady, "Fully ready"),
            (CompletedProjectPortfolioStatusCodes.TechnologyAction, "Technology review required"),
            (CompletedProjectPortfolioStatusCodes.TotAction, "ToT action pending"),
            (CompletedProjectPortfolioStatusCodes.CriticalIncomplete, "Records with critical gaps"),
            (CompletedProjectPortfolioStatusCodes.TechnologyAssessmentPending, "Technology assessment pending")
        };

        return options
            .Select(x => new SelectListItem(
                x.Label,
                x.Value,
                string.Equals(x.Value, selected ?? string.Empty, StringComparison.Ordinal)))
            .ToList();
    }
}


public sealed record ActiveCompletedProjectFilter(
    string Key,
    string Label,
    string Value);
