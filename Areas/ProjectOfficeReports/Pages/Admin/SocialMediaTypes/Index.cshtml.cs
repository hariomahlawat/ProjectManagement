using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes;

// On the shared list page, inside Administration (docs/ux/admin-review-3-fixes.md, PR 12).
[Authorize(Policy = AdminPolicies.SocialMediaListsManage)]
public class IndexModel : SocialMediaListPageModel
{
    private static readonly AdminReferenceListPresentation Shown = new(
        AdminNavigationKeys.SocialMediaTypes, "bi-megaphone", "Add event type", "events", "/Admin/SocialMediaTypes",
        Area: "ProjectOfficeReports", CreatePageName: "New");

    private readonly SocialMediaEventTypeService _service;
    private readonly SocialMediaPlatformService _platformService;

    public IndexModel(
        IReferenceListService lists,
        IListOrderNoticeService order,
        SocialMediaEventTypeService service,
        SocialMediaPlatformService platformService,
        UserManager<ApplicationUser> userManager)
        : base(lists, order, userManager)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
    }

    protected override ReferenceListDefinition Definition => ReferenceListCatalog.SocialMediaEventTypes;

    protected override AdminReferenceListPresentation Presentation => Shown;

    protected override async Task<(bool Found, string? Name, bool IsActive)> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await _service.FindAsync(id, cancellationToken) is { } item ? (true, item.Name, item.IsActive) : (false, null, false);

    protected override async Task<string?> SetActiveAsync(Guid id, bool isActive, byte[] rowVersion, CancellationToken cancellationToken)
    {
        var existing = await _service.FindAsync(id, cancellationToken);
        if (existing is null)
        {
            return "That event type is no longer in the list.";
        }

        var result = await _service.UpdateAsync(id, existing.Name, existing.Description, isActive, rowVersion, UserId, cancellationToken);
        return result.Outcome switch
        {
            SocialMediaEventTypeMutationOutcome.Success => null,
            SocialMediaEventTypeMutationOutcome.ConcurrencyConflict => "This record was changed by another administrator. Reload the page and try again.",
            _ => result.Errors.FirstOrDefault() ?? "The event type could not be changed."
        };
    }

    // Kept for platform forms opened from this page before Platforms had their own page.
    public async Task<IActionResult> OnPostTogglePlatformAsync(Guid id, bool enable, string? rowVersion, CancellationToken cancellationToken)
    {
        var result = await _platformService.ToggleAsync(id, enable, DecodeRowVersion(rowVersion), UserId, cancellationToken);
        Flash(result.Outcome == SocialMediaPlatformMutationOutcome.Success
            ? AdminOperationResult.Success(enable ? "Social media platform activated." : "Social media platform deactivated.")
            : AdminOperationResult.Failure(result.Errors.FirstOrDefault() ?? "The platform could not be changed.", result.Outcome.ToString()));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeletePlatformAsync(Guid id, string? rowVersion, CancellationToken cancellationToken)
    {
        Flash(await Lists.DeleteAsync(ReferenceListKeys.SocialMediaPlatforms, id, DecodeRowVersion(rowVersion), cancellationToken));
        return RedirectToPage();
    }
}
