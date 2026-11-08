using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin.RestoredHidden;

/// <summary>
/// "Restored but still hidden": repository documents restored from the trash before the
/// restore fix that are still inactive. The rule and the change live in
/// <see cref="DocRepoRestoredHiddenService"/>; this page only lists and asks.
/// </summary>
[Authorize(Policy = "DocRepo.Purge")]
public sealed class IndexModel : PageModel
{
    private readonly DocRepoRestoredHiddenService _service;
    private readonly IAdminTimeService _time;

    // SECTION: Constructor
    public IndexModel(DocRepoRestoredHiddenService service, IAdminTimeService time)
    {
        _service = service;
        _time = time;
    }

    // SECTION: View data
    public DocRepoRestoredHiddenList Result { get; private set; } = new(Array.Empty<DocRepoRestoredHiddenRow>(), 0);

    public IReadOnlyList<DocRepoRestoredHiddenRow> Items => Result.Items;

    public AdminPageHeaderModel Header { get; } = new()
    {
        Title = "Restored but still hidden",
        Description = "Documents restored from Deleted repository documents by an older version of PRISM. They came back hidden, so readers and search cannot find them. Some were hidden on purpose, so nothing changes until you choose Show again.",
        Icon = "bi-eye-slash"
    };

    /// <summary>"7 Nov 2026, 14:05 IST", through the one Administration formatter.</summary>
    public string FormatIst(DateTimeOffset utc) => _time.FormatIst(utc);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await _service.ListAsync(User, cancellationToken);
    }

    // SECTION: Reactivate one
    public async Task<IActionResult> OnPostReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        switch (await _service.ReactivateAsync(User, id, cancellationToken))
        {
            case DocRepoReactivateOutcome.Forbidden:
                return Forbid();
            case DocRepoReactivateOutcome.Reactivated:
                TempData[FlashMessageKeys.AdminRecoverySuccess] = "Document shown again. Readers and search can find it.";
                break;
            default:
                TempData[FlashMessageKeys.AdminRecoveryError] = "That document is no longer on this list. Someone may have shown, hidden or deleted it.";
                break;
        }

        return RedirectToPage();
    }

    // SECTION: Reactivate all shown
    public async Task<IActionResult> OnPostReactivateAllAsync(Guid[]? ids, CancellationToken cancellationToken)
    {
        var chosen = ids ?? Array.Empty<Guid>();
        if (chosen.Length == 0)
        {
            TempData[FlashMessageKeys.AdminRecoveryError] = "No documents were sent. Reload the page and try again.";
            return RedirectToPage();
        }

        var result = await _service.ReactivateManyAsync(User, chosen, cancellationToken);
        if (result.Forbidden)
        {
            return Forbid();
        }

        if (result.Reactivated > 0)
        {
            var message = result.Reactivated == 1 ? "1 document shown again." : $"{result.Reactivated} documents shown again.";
            if (result.NotListed > 0)
            {
                message += result.NotListed == 1
                    ? " 1 other was no longer on the list and was left as it is."
                    : $" {result.NotListed} others were no longer on the list and were left as they are.";
            }

            TempData[FlashMessageKeys.AdminRecoverySuccess] = message;
        }
        else
        {
            TempData[FlashMessageKeys.AdminRecoveryError] = "None of those documents is on this list any more, so nothing changed.";
        }

        return RedirectToPage();
    }
}
