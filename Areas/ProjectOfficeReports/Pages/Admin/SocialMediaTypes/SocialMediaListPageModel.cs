using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes;

/// <summary>
/// The social media Event types and Platforms pages, on the shared list page inside
/// Administration (docs/ux/admin-review-3-fixes.md, PR 12). Use and Delete follow the shared
/// rule; Deactivate goes through the list's own service, which audits it. The old page's
/// Toggle field ("enable") still works.
/// </summary>
public abstract class SocialMediaListPageModel : AdminReferenceListPageModel<Guid>
{
    private readonly UserManager<ApplicationUser> _userManager;

    protected SocialMediaListPageModel(IReferenceListService lists, IListOrderNoticeService order, UserManager<ApplicationUser> userManager)
        : base(lists, order)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    /// <summary>The old page's Toggle field (the state wanted).</summary>
    [BindProperty(Name = "enable")]
    public bool? LegacyEnable { get; set; }

    protected override string? OrderCheckKey => null;

    protected override string? Note => "Listed A to Z. An inactive item stays on the events already logged and is no longer offered in the social media form.";

    protected override Func<Guid, bool?, byte[], CancellationToken, Task<AdminOperationResult>>? Toggle => ToggleAsync;

    protected string UserId => _userManager.GetUserId(User) ?? string.Empty;

    /// <summary>The item's name and state, or Found false when it is gone.</summary>
    protected abstract Task<(bool Found, string? Name, bool IsActive)> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Sets the state through the list's own service; returns why it was refused, or null.</summary>
    protected abstract Task<string?> SetActiveAsync(Guid id, bool isActive, byte[] rowVersion, CancellationToken cancellationToken);

    public override Task<IActionResult> OnPostToggleAsync(Guid id, bool? isActive, string? rowVersion, CancellationToken cancellationToken) =>
        base.OnPostToggleAsync(id, isActive ?? LegacyEnable, rowVersion, cancellationToken);

    private async Task<AdminOperationResult> ToggleAsync(Guid id, bool? isActive, byte[] rowVersion, CancellationToken cancellationToken)
    {
        // The state wanted, never a flip: a repeated or late submit cannot undo it.
        if (isActive is not bool target)
        {
            return AdminOperationResult.Failure("This page was out of date. Reload it and try again.", "TargetStateMissing");
        }

        var (found, name, current) = await FindAsync(id, cancellationToken);
        if (!found)
        {
            return AdminOperationResult.Failure($"That {Definition.Label} is no longer in the list.", "NotFound");
        }

        if (current == target)
        {
            return AdminOperationResult.Success(target ? $"'{name}' is already active." : $"'{name}' is already inactive.");
        }

        if (rowVersion.Length == 0)
        {
            return AdminOperationResult.Failure("The record version is missing. Reload the page and try again.", "ConcurrencyTokenMissing");
        }

        var refused = await SetActiveAsync(id, target, rowVersion, cancellationToken);
        return refused is null
            ? AdminOperationResult.Success(target ? $"Reactivated '{name}'." : $"Deactivated '{name}'.")
            : AdminOperationResult.Failure(refused, "Refused");
    }
}
