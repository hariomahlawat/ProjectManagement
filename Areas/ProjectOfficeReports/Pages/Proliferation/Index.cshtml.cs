using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Proliferation;

[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ProliferationTrackerReadService _trackerService;
    private readonly IProliferationExportService _exportService;
    private readonly IAuthorizationService _authorizationService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<IndexModel> _logger;

    private string? _searchTerm;
    private string? _simulatorUserId;

    public IndexModel(
        ApplicationDbContext db,
        ProliferationTrackerReadService trackerService,
        IProliferationExportService exportService,
        IAuthorizationService authorizationService,
        UserManager<ApplicationUser> userManager,
        ILogger<IndexModel> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _trackerService = trackerService ?? throw new ArgumentNullException(nameof(trackerService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm
    {
        get => _searchTerm;
        set => _searchTerm = Normalize(value);
    }

    [BindProperty(SupportsGet = true)]
    public ProliferationSource? Source { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SponsoringUnitId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SimulatorUserId
    {
        get => _simulatorUserId;
        set => _simulatorUserId = Normalize(value);
    }

    public IReadOnlyList<ProliferationTrackerRow> Rows { get; private set; } = Array.Empty<ProliferationTrackerRow>();

    public ProliferationTrackerSummary Summary { get; private set; } = ProliferationTrackerSummary.Empty;

    public IReadOnlyList<string> PageWarnings { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<SponsoringUnitOption> SponsoringUnits { get; private set; } = Array.Empty<SponsoringUnitOption>();

    public IReadOnlyList<SimulatorOption> Simulators { get; private set; } = Array.Empty<SimulatorOption>();

    public IReadOnlyList<int> YearOptions { get; private set; } = Array.Empty<int>();

    public ProliferationTrackerRow? SelectedRow { get; private set; }

    public (int ProjectId, ProliferationSource Source, int Year)? SelectedKey { get; private set; }

    public bool ShowContextModal { get; private set; }

    public bool CanExport { get; private set; }

    [BindProperty]
    public ExportRequestInput Export { get; set; } = new();

    [BindProperty]
    public ContextRequestInput Context { get; set; } = new();

    public sealed class ExportRequestInput
    {
        public ProliferationSource? Source { get; set; }

        public int? YearFrom { get; set; }

        public int? YearTo { get; set; }
    }

    public sealed class ContextRequestInput
    {
        [HiddenInput]
        public int ProjectId { get; set; }

        [HiddenInput]
        public ProliferationSource Source { get; set; }

        [HiddenInput]
        public int Year { get; set; }
    }

    public sealed record SponsoringUnitOption(int Id, string Name);

    public sealed record SimulatorOption(string UserId, string? FullName, string? UserName)
    {
        public string DisplayName => !string.IsNullOrWhiteSpace(FullName)
            ? FullName
            : !string.IsNullOrWhiteSpace(UserName)
                ? UserName!
                : UserId;
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PopulateAsync(cancellationToken);
        Export = new ExportRequestInput
        {
            Source = Source,
            YearFrom = Year,
            YearTo = Year
        };
    }

    public async Task<IActionResult> OnPostExportAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        if (!CanExport)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            await PopulateAsync(cancellationToken);
            ViewData["ShowExportModal"] = true;
            return Page();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var request = new ProliferationExportRequest(
            Export.Source,
            Export.YearFrom,
            Export.YearTo,
            SponsoringUnitId,
            SimulatorUserId,
            SearchTerm,
            userId);

        var result = await _exportService.ExportAsync(request, cancellationToken);
        if (!result.Success || result.File is null)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            if (result.Errors.Count > 0)
            {
                TempData["ToastError"] = result.Errors[0];
            }

            _logger.LogWarning(
                "Proliferation export failed for user {UserId}. Errors: {Errors}",
                userId,
                string.Join(" ", result.Errors));

            await PopulateAsync(cancellationToken);
            ViewData["ShowExportModal"] = true;
            return Page();
        }

        return File(result.File.Content, result.File.ContentType, result.File.FileName);
    }

    public async Task<IActionResult> OnPostContextAsync(CancellationToken cancellationToken)
    {
        await PopulateAsync(cancellationToken);

        if (Context is null)
        {
            return Page();
        }

        var match = Rows.FirstOrDefault(r =>
            r.ProjectId == Context.ProjectId &&
            r.Source == Context.Source &&
            r.Year == Context.Year);

        if (match is null)
        {
            TempData["ToastError"] = "We could not find the requested proliferation record.";
            _logger.LogWarning(
                "Proliferation context request not found for project {ProjectId}, source {Source}, year {Year}.",
                Context.ProjectId,
                Context.Source,
                Context.Year);
            return Page();
        }

        SelectedRow = match;
        SelectedKey = (match.ProjectId, match.Source, match.Year);
        ShowContextModal = true;
        return Page();
    }

    private async Task PopulateAsync(CancellationToken cancellationToken)
    {
        await PopulatePermissionsAsync();
        await LoadFilterOptionsAsync(cancellationToken);

        var filter = new ProliferationTrackerFilter
        {
            ProjectSearchTerm = SearchTerm,
            Source = Source,
            Year = Year,
            SponsoringUnitId = SponsoringUnitId,
            SimulatorUserId = SimulatorUserId
        };

        Rows = await _trackerService.GetAsync(filter, cancellationToken);
        Summary = ProliferationTrackerSummary.FromRows(Rows);

        SelectedRow = null;
        SelectedKey = null;
        ShowContextModal = false;

        if (Context is { ProjectId: > 0 } context)
        {
            var selected = Rows.FirstOrDefault(r =>
                r.ProjectId == context.ProjectId &&
                r.Source == context.Source &&
                r.Year == context.Year);

            if (selected is not null)
            {
                SelectedRow = selected;
                SelectedKey = (selected.ProjectId, selected.Source, selected.Year);
            }
        }

        PageWarnings = BuildWarnings(Summary);
    }

    private async Task PopulatePermissionsAsync()
    {
        var exportResult = await _authorizationService.AuthorizeAsync(
            User,
            null,
            ProjectOfficeReportsPolicies.SubmitProliferationTracker);

        CanExport = exportResult.Succeeded;
    }

    private async Task LoadFilterOptionsAsync(CancellationToken cancellationToken)
    {
        var units = await _db.SponsoringUnits
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.SortOrder)
            .ThenBy(u => u.Name)
            .Select(u => new SponsoringUnitOption(u.Id, u.Name))
            .ToListAsync(cancellationToken);

        SponsoringUnits = units;

        var simulatorRows = await _db.Projects
            .AsNoTracking()
            .Where(p => p.LeadPoUserId != null)
            .Select(p => new
            {
                p.LeadPoUserId,
                FullName = p.LeadPoUser != null ? p.LeadPoUser.FullName : null,
                UserName = p.LeadPoUser != null ? p.LeadPoUser.UserName : null
            })
            .ToListAsync(cancellationToken);

        var simulatorOptions = simulatorRows
            .Where(r => !string.IsNullOrWhiteSpace(r.LeadPoUserId))
            .GroupBy(r => r.LeadPoUserId!)
            .Select(g =>
            {
                var first = g.First();
                return new SimulatorOption(g.Key, first.FullName, first.UserName);
            })
            .OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Simulators = simulatorOptions;

        var yearSet = new HashSet<int>();

        var yearlyYears = await _db.ProliferationYearlies
            .AsNoTracking()
            .Select(y => y.Year)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var y in yearlyYears)
        {
            yearSet.Add(y);
        }

        var granularYears = await _db.ProliferationGranularYearlyView
            .AsNoTracking()
            .Select(y => y.Year)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var y in granularYears)
        {
            yearSet.Add(y);
        }

        YearOptions = yearSet
            .OrderByDescending(y => y)
            .ToList();
    }

    private static IReadOnlyList<string> BuildWarnings(ProliferationTrackerSummary summary)
    {
        var warnings = new List<string>();

        if (summary.RowsMissingEffective > 0)
        {
            warnings.Add(summary.RowsMissingEffective == 1
                ? "1 record does not have effective proliferation metrics. Update the yearly or granular data to calculate effectiveness."
                : $"{summary.RowsMissingEffective} records do not have effective proliferation metrics. Update the yearly or granular data to calculate effectiveness.");
        }

        if (summary.PreferenceMismatches > 0)
        {
            warnings.Add(summary.PreferenceMismatches == 1
                ? "1 record uses an outdated preferred year. Review the preference to make sure the selected year is available."
                : $"{summary.PreferenceMismatches} records use an outdated preferred year. Review the preferences to make sure the selected years are available.");
        }

        return warnings;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public sealed class ProliferationTrackerSummary
    {
        public static ProliferationTrackerSummary Empty { get; } = new();

        public int TotalRows { get; init; }

        public int DistinctProjects { get; init; }

        public int DistinctYears { get; init; }

        public int InternalRows { get; init; }

        public int ExternalRows { get; init; }

        public int AutoModeCount { get; init; }

        public int YearlyModeCount { get; init; }

        public int GranularModeCount { get; init; }

        public int EffectiveDirectTotal { get; init; }

        public int EffectiveIndirectTotal { get; init; }

        public decimal EffectiveInvestmentTotal { get; init; }

        public int RowsMissingEffective { get; init; }

        public int PreferenceMismatches { get; init; }

        public static ProliferationTrackerSummary FromRows(IReadOnlyList<ProliferationTrackerRow>? rows)
        {
            var items = rows ?? Array.Empty<ProliferationTrackerRow>();

            var distinctProjects = items
                .Select(r => r.ProjectId)
                .Distinct()
                .Count();

            var distinctYears = items
                .Select(r => r.Year)
                .Distinct()
                .Count();

            var effectiveDirect = 0;
            var effectiveIndirect = 0;
            decimal effectiveInvestment = 0m;

            foreach (var row in items)
            {
                if (row.Effective?.DirectBeneficiaries is int direct)
                {
                    effectiveDirect += direct;
                }

                if (row.Effective?.IndirectBeneficiaries is int indirect)
                {
                    effectiveIndirect += indirect;
                }

                if (row.Effective?.InvestmentValue is decimal investment)
                {
                    effectiveInvestment += investment;
                }
            }

            var internalRows = items.Count(r => r.Source == ProliferationSource.Internal);
            var externalRows = items.Count(r => r.Source == ProliferationSource.External);
            var autoMode = items.Count(r => r.Preference.Mode == ProliferationPreferenceMode.Auto);
            var yearlyMode = items.Count(r => r.Preference.Mode == ProliferationPreferenceMode.UseYearly);
            var granularMode = items.Count(r => r.Preference.Mode == ProliferationPreferenceMode.UseGranular);
            var missingEffective = items.Count(r => r.Effective is null);
            var preferenceMismatch = items.Count(r => r.Preference.HasPreference && !r.Preference.PreferredYearMatches);

            return new ProliferationTrackerSummary
            {
                TotalRows = items.Count,
                DistinctProjects = distinctProjects,
                DistinctYears = distinctYears,
                InternalRows = internalRows,
                ExternalRows = externalRows,
                AutoModeCount = autoMode,
                YearlyModeCount = yearlyMode,
                GranularModeCount = granularMode,
                EffectiveDirectTotal = effectiveDirect,
                EffectiveIndirectTotal = effectiveIndirect,
                EffectiveInvestmentTotal = effectiveInvestment,
                RowsMissingEffective = missingEffective,
                PreferenceMismatches = preferenceMismatch
            };
        }
    }
}
