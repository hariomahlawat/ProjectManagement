using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.Admin.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Areas.Admin.Pages.BriefingDecks;

/// <summary>
/// Restore deleted items › Briefing decks (docs/ux/admin-review-3-fixes.md, PR 15). Lists the
/// decks deleted in Briefing Studio that can still be restored, and restores one through the
/// briefing-deck service, so the window, the name check and the audit entry are the service's own.
/// An administrator restores anyone's deck, as on the other Restore deleted items pages, so the
/// page and its handler use the Restore deleted items policy rather than Studio's.
/// </summary>
[Authorize(Policy = AdminPolicies.RecoveryManage)]
public sealed class DeletedModel : PageModel
{
    private readonly IProjectBriefingDeckService _decks;
    private readonly IAdminTimeService _time;

    public DeletedModel(IProjectBriefingDeckService decks, IAdminTimeService time)
    {
        _decks = decks ?? throw new ArgumentNullException(nameof(decks));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public AdminPageHeaderModel Header { get; private set; } = new();

    public IReadOnlyList<ProjectBriefingDeletedDeckVm> Decks { get; private set; } = Array.Empty<ProjectBriefingDeletedDeckVm>();

    public int RetentionDays => ProjectBriefingDeckService.DeletedRetentionDays;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = UserId();
        if (userId is null)
        {
            return Challenge();
        }

        Decks = await _decks.ListDeletedAsync(userId, cancellationToken);
        Header = new AdminPageHeaderModel
        {
            Title = "Restore deleted items",
            Description = $"Briefing decks deleted in Briefing Studio. Restore brings a deck back with its projects, notes and choices. Each deck can be restored for {RetentionDays} days after it is deleted.",
            Icon = "bi-arrow-counterclockwise"
        };
        return Page();
    }

    public async Task<IActionResult> OnPostRestoreAsync(long deckId, CancellationToken cancellationToken)
    {
        var userId = UserId();
        if (userId is null)
        {
            return Challenge();
        }

        try
        {
            var restored = await _decks.RestoreAsync(deckId, userId, cancellationToken);
            TempData[FlashMessageKeys.AdminRecoverySuccess] = restored.Renamed
                ? $"“{restored.Name}” is back in Briefing Studio. It has a new name because another deck now uses the old one."
                : $"“{restored.Name}” is back in Briefing Studio.";
        }
        catch (KeyNotFoundException)
        {
            TempData[FlashMessageKeys.AdminRecoveryError] =
                $"That deck is no longer in the list. It may have been restored already, or it was deleted more than {RetentionDays} days ago.";
        }
        catch (DbUpdateException)
        {
            TempData[FlashMessageKeys.AdminRecoveryError] = "The deck could not be restored. Reload the page and try again.";
        }

        return RedirectToPage();
    }

    public string FormatDate(DateTimeOffset utc) => _time.ToIst(utc).ToString("dd MMM yyyy");

    public string FormatTime(DateTimeOffset utc) => _time.FormatIst(utc);

    private string? UserId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}
