using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.SearchV2;
using ProjectManagement.Services.SearchV2.Indexing;
using ProjectManagement.Services.SearchV2.Models;

namespace ProjectManagement.Areas.Admin.Pages.Diagnostics;

[Authorize(Policy = AdminPolicies.SecurityView)]
[ResponseCache(NoStore = true)]
public sealed class SearchIndexModel : PageModel
{
    private readonly ISearchIndexStore _store;
    private readonly IAuthorizationService _authorization;
    private readonly SearchV2Options _options;

    public SearchIndexModel(
        ISearchIndexStore store,
        IAuthorizationService authorization,
        IOptions<SearchV2Options> options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public SearchIndexHealth Health { get; private set; } = new(false, 0, 0, 0, 0, 0, null, null, null);
    public IReadOnlyList<SearchFailedIndexWorkItem> FailedItems { get; private set; } = Array.Empty<SearchFailedIndexWorkItem>();
    public int ConfiguredProjectionVersion => _options.ProjectionVersion;
    public int SearchSchemaVersion => _options.IndexVersion;
    public bool CanMaintain { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRebuildAsync(CancellationToken cancellationToken)
    {
        if (!await CanMaintainAsync()) return Forbid();
        await _store.RequestFullRebuildAsync(User.Identity?.Name ?? "administrator", cancellationToken);
        StatusMessage = "A full Search V2 rebuild has been queued. The current active generation will continue serving users until the replacement generation is complete.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryAllAsync(CancellationToken cancellationToken)
    {
        if (!await CanMaintainAsync()) return Forbid();
        await _store.RetryFailedAsync(null, cancellationToken);
        StatusMessage = "Failed Search V2 indexing jobs have been returned to the retry queue.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryAsync(long id, CancellationToken cancellationToken)
    {
        if (!await CanMaintainAsync()) return Forbid();
        if (id > 0) await _store.RetryFailedAsync(id, cancellationToken);
        StatusMessage = "The selected Search V2 indexing job has been returned to the retry queue.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Health = await _store.GetHealthAsync(cancellationToken);
        FailedItems = await _store.GetFailedItemsAsync(25, cancellationToken);
        CanMaintain = await CanMaintainAsync();
    }

    private async Task<bool> CanMaintainAsync() =>
        (await _authorization.AuthorizeAsync(User, AdminPolicies.IngestionManage)).Succeeded;
}
