using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.Lookups.LineDirectorates;

// On the shared list page (docs/ux/admin-review-3-fixes.md, owner decision 1, PR 10), switched
// off and on in place (docs/ux/admin-review-4-fixes.md, PR 4).
[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class IndexModel : AdminReferenceListPageModel
{
    private static readonly AdminReferenceListPresentation Shown = new(
        AdminNavigationKeys.LineDirectorates, "bi-diagram-2", "Add line directorate", "projects", "/Lookups/LineDirectorates");

    private readonly IAdminMasterDataCommandService _commands;

    public IndexModel(IReferenceListService lists, IAdminMasterDataCommandService commands, IListOrderNoticeService order)
        : base(lists, order)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    protected override ReferenceListDefinition Definition => ReferenceListCatalog.LineDirectorates;
    protected override string? OrderCheckKey => AdminMasterDataIntegrityService.LineDirectorateOrderKey;
    protected override AdminReferenceListPresentation Presentation => Shown;
    protected override Func<int, int, CancellationToken, Task<AdminOperationResult>>? Move => _commands.MoveLineDirectorateAsync;

    // Switched off and on in place, like the other lists (fourth review, PR 4): the state asked
    // for and the row version shown, never a flip. The old Deactivate page redirects here.
    protected override Func<int, bool?, byte[], CancellationToken, Task<AdminOperationResult>>? Toggle =>
        LookupLists.Toggle(LookupLists.LineDirectorates, _commands);
}
