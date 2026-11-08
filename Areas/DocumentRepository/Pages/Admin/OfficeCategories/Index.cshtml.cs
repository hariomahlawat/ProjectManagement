using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Navigation.ModuleNav;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin.OfficeCategories;

// On the shared list page, inside Administration (docs/ux/admin-review-3-fixes.md, PR 12).
[Authorize(Policy = Policies.Documents.ManageCategories)]
public class IndexModel : DocRepoListPageModel
{
    private static readonly AdminReferenceListPresentation Shown = new(
        AdminNavigationKeys.DocumentOffices, "bi-building-gear", "Add office", "documents", "/Admin/OfficeCategories", Area: "DocumentRepository");

    public IndexModel(IDocRepoListService lists, IReferenceListService referenceLists, IListOrderNoticeService order)
        : base(lists, referenceLists, order)
    {
    }

    protected override DocRepoListKind Kind => DocRepoListKind.Office;

    protected override ReferenceListDefinition Definition => ReferenceListCatalog.DocumentOffices;

    protected override string? OrderCheckKey => AdminMasterDataIntegrityService.OfficeCategoryOrderKey;

    protected override AdminReferenceListPresentation Presentation => Shown;

    public override string Noun => "office";
}
