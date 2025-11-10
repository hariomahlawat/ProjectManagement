using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.Search;

namespace ProjectManagement.Areas.Common.Pages.Search;

public class IndexModel : PageModel
{
    // SECTION: Dependencies
    private readonly IGlobalSearchService _globalSearchService;

    public IndexModel(IGlobalSearchService globalSearchService)
    {
        _globalSearchService = globalSearchService ?? throw new ArgumentNullException(nameof(globalSearchService));
    }

    // SECTION: Query parameters
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string[]? Source { get; set; }

    // SECTION: Page data
    public IReadOnlyList<GlobalSearchHit> Results { get; private set; } = Array.Empty<GlobalSearchHit>();

    public IReadOnlyList<string> AvailableSources { get; private set; } = Array.Empty<string>();

    // SECTION: Handlers
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Q))
        {
            return;
        }

        var rawResults = await _globalSearchService.SearchAsync(Q, cancellationToken);
        AvailableSources = rawResults
            .Select(hit => hit.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source)
            .ToList();

        var filteredResults = rawResults;
        if (Source is { Length: > 0 })
        {
            var filter = new HashSet<string>(Source, StringComparer.OrdinalIgnoreCase);
            filteredResults = filteredResults
                .Where(hit => filter.Contains(hit.Source))
                .ToList();
        }

        Results = filteredResults
            .GroupBy(hit => hit.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(hit => hit.Score)
                .ThenByDescending(hit => hit.Date)
                .First())
            .ToList();
    }
}
