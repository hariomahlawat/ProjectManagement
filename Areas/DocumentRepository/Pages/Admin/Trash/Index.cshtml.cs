using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin.Trash;

[Authorize(Policy = "DocRepo.Purge")]
public sealed class IndexModel : PageModel
{
    private readonly DocRepoTrashService _trash;
    private readonly DocRepoTrashPurgeService _purge;

    // SECTION: Constructor
    public IndexModel(DocRepoTrashService trash, DocRepoTrashPurgeService purge)
    {
        _trash = trash;
        _purge = purge;
    }

    /// <summary>Days before a trashed document is purged automatically; 0 when that is off.</summary>
    public int RetentionDays => _purge.RetentionDays;

    public DateTime? PurgeDueUtc(DateTime? deletedAtUtc) => _purge.PurgeDueUtc(deletedAtUtc);

    // SECTION: View data
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = DocRepoTrashService.DefaultPageSize;

    public IReadOnlyList<int> PageSizes => DocRepoTrashService.PageSizes;

    public DocRepoTrashPage Result { get; private set; } = new(
        Array.Empty<DocRepoTrashRow>(), 0, 0, 0, 1, DocRepoTrashService.DefaultPageSize, new Dictionary<string, string>());

    public IReadOnlyList<DocRepoTrashRow> Items => Result.Items;

    /// <summary>Everything in the trash, before the search narrows it.</summary>
    public int TotalCount => Result.TrashCount;

    public long TotalBytes => Result.TrashBytes;

    /// <summary>The Administration page header: the same title as every "Restore deleted items" page.</summary>
    public AdminPageHeaderModel Header => new()
    {
        Title = "Restore deleted items",
        Description = "Document Repository files whose deletion was approved. Restore puts one back in the library; Delete for good removes it and its file. "
            + (RetentionDays > 0
                ? $"Anything left here is deleted for good {RetentionDays} days after it was deleted."
                : "Automatic clean-up is off, so documents stay here until someone deletes them."),
        Icon = "bi-arrow-counterclockwise"
    };

    public string NameOf(string? userId)
        => string.IsNullOrWhiteSpace(userId) ? "Unknown" : Result.UserNames.TryGetValue(userId, out var name) ? name : userId;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Query = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim();
        Result = await _trash.ListAsync(User, new DocRepoTrashQuery(Query, PageNumber, PageSize), cancellationToken);
        PageNumber = Result.Page;
        PageSize = Result.PageSize;
    }

    /// <summary>The list with the current search and page size, on another page.</summary>
    public string? PageUrl(int page) => Url.Page(null, new
    {
        q = Query,
        pageNumber = page > 1 ? page : (int?)null,
        pageSize = PageSize != DocRepoTrashService.DefaultPageSize ? PageSize : (int?)null
    });

    /// <summary>The list from its first page with another number of rows per page.</summary>
    public string? SizeUrl(int pageSize) => Url.Page(null, new
    {
        q = Query,
        pageSize = pageSize != DocRepoTrashService.DefaultPageSize ? pageSize : (int?)null
    });

    // SECTION: Restore handler
    public async Task<IActionResult> OnPostRestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        switch (await _trash.RestoreAsync(User, id, cancellationToken))
        {
            case DocRepoRestoreOutcome.Restored:
                StatusMessage = "Document restored. It is back in the library and in search.";
                break;
            case DocRepoRestoreOutcome.Forbidden:
                return Forbid();
            default:
                ErrorMessage = "Document not found or already restored.";
                break;
        }

        return RedirectToPage(ListRouteValues());
    }

    // SECTION: Purge handler
    public async Task<IActionResult> OnPostPurgeAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _purge.PurgeAsync(id, GetActorUserId(), cancellationToken: cancellationToken);
        switch (result.Outcome)
        {
            case DocRepoPurgeOutcome.NotFound:
                ErrorMessage = "Document not found or already purged.";
                break;
            case DocRepoPurgeOutcome.FileError:
                ErrorMessage = $"Failed to delete file: {result.Error}";
                break;
            default:
                StatusMessage = "Document permanently deleted.";
                break;
        }

        return RedirectToPage(ListRouteValues());
    }

    // SECTION: Helpers
    private string GetActorUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";

    // After an action, return to the same search, page and page size; the list clamps the page
    // if the last row on it has gone.
    private object ListRouteValues() => new
    {
        q = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim(),
        pageNumber = PageNumber > 1 ? PageNumber : (int?)null,
        pageSize = PageSize != DocRepoTrashService.DefaultPageSize ? PageSize : (int?)null
    };
}
