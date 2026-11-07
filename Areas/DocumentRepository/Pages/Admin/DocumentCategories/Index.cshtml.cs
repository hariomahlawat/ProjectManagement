using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin.MasterData.Integrity;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Admin.DocumentCategories;

[Authorize(Policy = Policies.Documents.ManageCategories)]
public class IndexModel : DocRepoListPageModel
{
    public IndexModel(IDocRepoListService lists, IListOrderNoticeService order)
        : base(lists, order)
    {
    }

    protected override DocRepoListKind Kind => DocRepoListKind.DocumentType;

    protected override string OrderKey => AdminMasterDataIntegrityService.DocumentCategoryOrderKey;

    public override string Noun => "type";

    public override string LibraryFilter => "documentCategoryId";
}
