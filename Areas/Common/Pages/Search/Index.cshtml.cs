using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Query;

namespace ProjectManagement.Areas.Common.Pages.Search;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly ISearchGateway _search;

    public IndexModel(ISearchGateway search)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
    }

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

        if (DateFrom.HasValue && DateTo.HasValue && DateFrom.Value > DateTo.Value)
        {
            (DateFrom, DateTo) = (DateTo, DateFrom);
        }

        Search = await _search.SearchAsync(
            new SearchRequest(
                Query: Q,
                Categories: Category?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                Sources: Source?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                Cursor: Cursor,
                ProjectIds: Project?.Where(value => value > 0).Distinct().ToArray(),
                Statuses: Status?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                FileTypes: FileType?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                Stages: Stage?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
                DateFrom: DateFrom,
                DateTo: DateTo),
            User,
            cancellationToken);
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
}
