using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.MasterData;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.Admin.Pages.TechnicalCategories;

// On the shared list page (docs/ux/admin-review-3-fixes.md, owner decision 1, PR 11): a tree,
// with sub-categories indented under their parent and moved among their siblings.
[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class IndexModel : AdminReferenceListPageModel
{
    private static readonly AdminReferenceListPresentation Shown = new(
        AdminNavigationKeys.TechnicalCategories, "bi-cpu", "Add technical category", "projects and sub-categories", "/TechnicalCategories");

    private readonly IAdminMasterDataCommandService _commands;

    public IndexModel(IReferenceListService lists, IAdminMasterDataCommandService commands, IListOrderNoticeService order)
        : base(lists, order)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    protected override ReferenceListDefinition Definition => ReferenceListCatalog.TechnicalCategories;
    protected override string? OrderCheckKey => null;
    protected override AdminReferenceListPresentation Presentation => Shown;
    protected override Func<int, int, CancellationToken, Task<AdminOperationResult>>? Move => _commands.MoveTechnicalCategoryAsync;

    // The form sends the state the administrator chose and the row version it showed, so a
    // resubmitted form or another administrator's change cannot flip the item the wrong way.
    protected override Func<int, bool?, byte[], CancellationToken, Task<AdminOperationResult>>? Toggle =>
        (id, isActive, rowVersion, cancellationToken) => isActive is bool target
            ? _commands.SetTechnicalCategoryActiveAsync(id, target, rowVersion, cancellationToken)
            : Task.FromResult(AdminOperationResult.Failure("Reload the page and try again.", "BadRequest"));
}
