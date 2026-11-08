using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Infrastructure;
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

    // SECTION: Constructor
    public IndexModel(DocRepoRestoredHiddenService service)
    {
        _service = service;
    }

    // SECTION: View data
    public DocRepoRestoredHiddenList Result { get; private set; } = new(Array.Empty<DocRepoRestoredHiddenRow>(), 0);

    public IReadOnlyList<DocRepoRestoredHiddenRow> Items => Result.Items;

    public AdminPageHeaderModel Header { get; } = new()
    {
        Title = "Restored but still hidden",
        Description = "Repository documents brought back from the trash before restore was fixed. They are out of the trash but still inactive, so readers and search cannot see them. Some may have been made inactive on purpose, so nothing changes until you choose Reactivate.",
        Icon = "bi-eye-slash"
    };

    /// <summary>"7 Nov 2026, 14:05 IST".</summary>
    public static string FormatIst(DateTimeOffset utc)
        => IstClock.ToIst(utc).ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " IST";

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
                TempData[FlashMessageKeys.AdminRecoverySuccess] = "Document reactivated. Readers and search can see it again.";
                break;
            default:
                TempData[FlashMessageKeys.AdminRecoveryError] = "That document is no longer on this list. Someone may have reactivated, deactivated or deleted it.";
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
            var message = result.Reactivated == 1 ? "1 document reactivated." : $"{result.Reactivated} documents reactivated.";
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
