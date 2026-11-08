using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.Lookups.SponsoringUnits;

// On the shared list page (docs/ux/admin-review-3-fixes.md, owner decision 1, PR 10).
[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class IndexModel : AdminReferenceListPageModel
{
    private static readonly AdminReferenceListPresentation Shown = new(
        AdminNavigationKeys.SponsoringUnits, "bi-building", "Add sponsoring unit", "projects", "/Lookups/SponsoringUnits");

    private readonly IAdminMasterDataCommandService _commands;

    public IndexModel(IReferenceListService lists, IAdminMasterDataCommandService commands, IListOrderNoticeService order)
        : base(lists, order)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    protected override ReferenceListDefinition Definition => ReferenceListCatalog.SponsoringUnits;
    protected override string? OrderCheckKey => AdminMasterDataIntegrityService.SponsoringUnitOrderKey;
    protected override AdminReferenceListPresentation Presentation => Shown;
    protected override Func<int, int, CancellationToken, Task<AdminOperationResult>>? Move => _commands.MoveSponsoringUnitAsync;
}
