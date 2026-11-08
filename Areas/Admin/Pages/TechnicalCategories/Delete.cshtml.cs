using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Areas.Admin.Pages.Shared;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Admin.MasterData.ReferenceLists;

namespace ProjectManagement.Areas.Admin.Pages.TechnicalCategories;

// Kept so old links work; it follows the shared list page's rule (PR 11).
[Authorize(Policy = AdminPolicies.MasterDataManage)]
public sealed class DeleteModel : AdminReferenceListDeletePageModel
{
    public DeleteModel(IReferenceListService lists) : base(lists)
    {
    }

    protected override ReferenceListDefinition Definition => ReferenceListCatalog.TechnicalCategories;
}
