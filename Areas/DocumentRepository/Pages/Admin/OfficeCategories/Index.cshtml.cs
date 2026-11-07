using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin.OfficeCategories;

[Authorize(Policy = Policies.Documents.ManageCategories)]
public class IndexModel : DocRepoListPageModel
{
    public IndexModel(IDocRepoListService lists, IListOrderNoticeService order)
        : base(lists, order)
    {
    }

    protected override DocRepoListKind Kind => DocRepoListKind.Office;

    protected override string OrderKey => AdminMasterDataIntegrityService.OfficeCategoryOrderKey;

    public override string Noun => "office";

    public override string LibraryFilter => "officeCategoryId";
}
