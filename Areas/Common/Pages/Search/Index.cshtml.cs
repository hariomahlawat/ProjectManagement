using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Query;

namespace ProjectManagement.Areas.Common.Pages.Search;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly ISearchGateway _search;
    private readonly IWebHostEnvironment _environment;
    private readonly IAuthorizationService _authorization;
    private readonly ApplicationDbContext _db;

    public IndexModel(
        ISearchGateway search,
        IWebHostEnvironment environment,
        IAuthorizationService authorization,
        ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    public bool ShowEngineDiagnostics => _environment.IsDevelopment();

    // Query latency is an operator detail: shown to those who can open Search diagnostics.
    public bool ShowLatency { get; private set; }

    // Names for the project filters in the URL, so the active-filter chips can name a project
    // even when it is not among the returned facets (for example when nothing matches).
    public IReadOnlyDictionary<int, string> SelectedProjectNames { get; private set; } = new Dictionary<int, string>();

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string[]? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public string[]? Source { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Cursor { get; set; }

    [BindProperty(SupportsGet = true)]
    public int[]? Project { get; set; }

    [BindProperty(SupportsGet = true)]
    public string[]? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string[]? FileType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string[]? Stage { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? DateFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? DateTo { get; set; }

    public SearchGatewayResponse Search { get; private set; } = SearchGatewayResponse.Empty(string.Empty);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Q)) return;

        NormalizeDateRange();
        Search = await _search.SearchAsync(
            BuildSearchRequest(includeDetailedFacets: HasAdvancedFilters(), facetsOnly: false),
            User,
            cancellationToken);
        ShowLatency = ShowEngineDiagnostics
            || (await _authorization.AuthorizeAsync(User, AdminPolicies.SecurityView)).Succeeded;
        SelectedProjectNames = await LoadSelectedProjectNamesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnGetFacetsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Q))
        {
            return new JsonResult(new { ok = false, detailedLoaded = false });
        }

        NormalizeDateRange();
        var response = await _search.SearchAsync(
            BuildSearchRequest(includeDetailedFacets: true, facetsOnly: true),
            User,
            cancellationToken);

        if (!response.UsedSearchV2 || !response.Facets.DetailedLoaded)
        {
            return new JsonResult(new { ok = false, detailedLoaded = false });
        }

        static object Values(IReadOnlyList<SearchFacetValue> facets) => facets.Select(facet => new
        {
            facet.Value,
            facet.Count,
            facet.Label
        }).ToArray();

        return new JsonResult(new
        {
            ok = true,
            detailedLoaded = true,
            sources = Values(response.Facets.Sources),
            projects = Values(response.Facets.Projects),
            statuses = Values(response.Facets.Statuses),
            fileTypes = Values(response.Facets.FileTypes),
            stages = Values(response.Facets.Stages)
        });
    }

    public async Task<IActionResult> OnGetSuggestionsAsync(string? q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return new JsonResult(Array.Empty<object>());
        }

        var suggestions = await _search.SuggestAsync(q, User, 6, cancellationToken);
        return new JsonResult(suggestions.Select(suggestion => new
        {
            suggestion.Title,
            suggestion.Subtitle,
            suggestion.Url,
            suggestion.SourceModule,
            suggestion.Category,
            suggestion.Identifier
        }));
    }

    public async Task<IActionResult> OnPostClickAsync(
        string? query,
        string? entityType,
        string? entityKey,
        int rank,
        string? sourceModule,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)
            || string.IsNullOrWhiteSpace(entityType)
            || string.IsNullOrWhiteSpace(entityKey)
            || string.IsNullOrWhiteSpace(sourceModule)
            || rank <= 0)
        {
            return new JsonResult(new { ok = false }) { StatusCode = StatusCodes.Status400BadRequest };
        }

        await _search.LogClickAsync(query, entityType, entityKey, rank, sourceModule, cancellationToken);
        return new JsonResult(new { ok = true });
    }

    private SearchRequest BuildSearchRequest(bool includeDetailedFacets, bool facetsOnly) => new(
        Query: Q ?? string.Empty,
        Categories: Category?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
        Sources: Source?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
        Cursor: facetsOnly ? null : Cursor,
        PageSize: facetsOnly ? 5 : null,
        ProjectIds: Project?.Where(value => value > 0).Distinct().ToArray(),
        Statuses: Status?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
        FileTypes: FileType?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
        Stages: Stage?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
        DateFrom: DateFrom,
        DateTo: DateTo,
        IncludeDetailedFacets: includeDetailedFacets,
        FacetsOnly: facetsOnly);

    /// <summary>
    /// The parent project a non-project result belongs to, read from the indexed match fields
    /// (stored as "Name · Case"). Null for projects themselves and for Legacy results.
    /// </summary>
    public static string? ProjectNameFor(SearchResult hit)
    {
        if (string.Equals(hit.EntityType, "Project", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(hit.MetadataJson))
        {
            return null;
        }

        try
        {
            using var metadata = JsonDocument.Parse(hit.MetadataJson);
            if (metadata.RootElement.ValueKind == JsonValueKind.Object
                && metadata.RootElement.TryGetProperty("matchFields", out var fields)
                && fields.ValueKind == JsonValueKind.Object
                && fields.TryGetProperty("Project", out var project)
                && project.ValueKind == JsonValueKind.String)
            {
                var name = project.GetString()?.Split(" · ", 2)[0].Trim();
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private async Task<IReadOnlyDictionary<int, string>> LoadSelectedProjectNamesAsync(CancellationToken cancellationToken)
    {
        var ids = Project?.Where(value => value > 0).Distinct().Take(50).ToArray() ?? Array.Empty<int>();
        if (ids.Length == 0)
        {
            return new Dictionary<int, string>();
        }

        // Project names are visible to every signed-in user (Projects pages are [Authorize] only).
        return await _db.Projects
            .AsNoTracking()
            .Where(project => ids.Contains(project.Id) && !project.IsDeleted)
            .ToDictionaryAsync(project => project.Id, project => project.Name, cancellationToken);
    }

    public bool HasAdvancedFilters() =>
        (Source?.Any(value => !string.IsNullOrWhiteSpace(value)) ?? false)
        || (Project?.Any(value => value > 0) ?? false)
        || (Status?.Any(value => !string.IsNullOrWhiteSpace(value)) ?? false)
        || (FileType?.Any(value => !string.IsNullOrWhiteSpace(value)) ?? false)
        || (Stage?.Any(value => !string.IsNullOrWhiteSpace(value)) ?? false)
        || DateFrom.HasValue
        || DateTo.HasValue;

    private void NormalizeDateRange()
    {
        if (DateFrom.HasValue && DateTo.HasValue && DateFrom.Value > DateTo.Value)
        {
            (DateFrom, DateTo) = (DateTo, DateFrom);
        }
    }
}
