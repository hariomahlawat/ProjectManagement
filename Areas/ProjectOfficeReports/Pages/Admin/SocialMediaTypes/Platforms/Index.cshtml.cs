using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Configuration;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Admin.SocialMediaTypes.Platforms;

// On the shared list page, inside Administration (docs/ux/admin-review-3-fixes.md, PR 12).
[Authorize(Policy = AdminPolicies.SocialMediaListsManage)]
public class IndexModel : SocialMediaListPageModel
{
    private static readonly AdminReferenceListPresentation Shown = new(
        AdminNavigationKeys.SocialMediaPlatforms, "bi-share", "Add platform", "events", "/Admin/SocialMediaTypes/Platforms",
        Area: "ProjectOfficeReports", CreatePageName: "New");

    private readonly SocialMediaPlatformService _service;

    public IndexModel(IReferenceListService lists, IListOrderNoticeService order, SocialMediaPlatformService service, UserManager<ApplicationUser> userManager)
        : base(lists, order, userManager)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    protected override ReferenceListDefinition Definition => ReferenceListCatalog.SocialMediaPlatforms;

    protected override AdminReferenceListPresentation Presentation => Shown;

    protected override async Task<(bool Found, string? Name, bool IsActive)> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await _service.FindAsync(id, cancellationToken) is { } item ? (true, item.Name, item.IsActive) : (false, null, false);

    protected override async Task<string?> SetActiveAsync(Guid id, bool isActive, byte[] rowVersion, CancellationToken cancellationToken)
    {
        var result = await _service.ToggleAsync(id, isActive, rowVersion, UserId, cancellationToken);
        return result.Outcome switch
        {
            SocialMediaPlatformMutationOutcome.Success => null,
            SocialMediaPlatformMutationOutcome.ConcurrencyConflict => "This record was changed by another administrator. Reload the page and try again.",
            _ => result.Errors.FirstOrDefault() ?? "The platform could not be changed."
        };
    }
}
