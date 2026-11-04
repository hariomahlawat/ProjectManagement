using Microsoft.AspNetCore.Authorization;
using ProjectManagement.Configuration;
using ProjectManagement.Services.IndustryPartners;

namespace ProjectManagement.Pages.IndustryPartners;

// SECTION: Full organisation page (/IndustryPartners/{id})
// A shareable, printable page for one organisation. It reuses every action of the directory
// page (contacts, files, project links, edit, delete) with the same authorization; only the
// layout differs, and actions return here instead of to the list's side drawer.
[Authorize(Policy = Policies.IndustryPartners.View)]
public sealed class OrganisationModel : IndexModel
{
    public OrganisationModel(
        IIndustryPartnerService service,
        IIndustryPartnerAttachmentManager attachmentManager,
        IAuthorizationService authorizationService)
        : base(service, attachmentManager, authorizationService)
    {
    }

    protected override bool LoadsDirectoryList => false;

    protected override string OrganisationViewPage => "./Organisation";

    public override async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await base.OnGetAsync(cancellationToken);
        if (SelectedPartner is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}
