using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Services.IndustryPartners;
using ProjectManagement.Services.IndustryPartners.Exceptions;
using ProjectManagement.ViewModels.Projects.IndustryPartners;

namespace ProjectManagement.Pages.Projects.IndustryPartners
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly IIndustryPartnerService _industryPartnerService;

        public IndexModel(IIndustryPartnerService industryPartnerService)
        {
            _industryPartnerService = industryPartnerService;
        }

        // Section: Query parameters
        [BindProperty(SupportsGet = true, Name = "partner")]
        public int? PartnerId { get; set; }

        [BindProperty(SupportsGet = true, Name = "q")]
        public string? Q { get; set; }

        [BindProperty(SupportsGet = true, Name = "type")]
        public string? Type { get; set; }

        [BindProperty(SupportsGet = true, Name = "status")]
        public string? Status { get; set; }

        [BindProperty(SupportsGet = true, Name = "sort")]
        public string? Sort { get; set; }

        // Section: Directory dataset
        public IReadOnlyList<PartnerDetailViewModel> Partners { get; private set; } = Array.Empty<PartnerDetailViewModel>();

        // Section: Selected detail
        public PartnerDetailViewModel? SelectedPartner { get; private set; }

        // Section: Result count
        public int TotalCount { get; private set; }

        // Section: Drawer view model
        public LinkProjectDrawerViewModel LinkProjectDrawer { get; private set; } = new();

        // Section: Create partner request
        [BindProperty]
        public CreatePartnerRequest CreatePartnerRequest { get; set; } = new();

        public async Task OnGetAsync()
        {
            var query = new PartnerSearchQuery
            {
                Query = Q,
                Type = Type,
                Status = Status,
                Sort = Sort
            };

            Partners = await _industryPartnerService.SearchPartnersAsync(query);
            TotalCount = Partners.Count;

            // Section: Selected detail
            SelectedPartner = PartnerId.HasValue
                ? await _industryPartnerService.GetPartnerDetailAsync(PartnerId.Value)
                : null;

            LinkProjectDrawer = await BuildLinkProjectDrawerAsync();
        }

        // Section: Partner commands
        public async Task<IActionResult> OnPostArchivePartnerAsync(int partnerId)
        {
            if (partnerId <= 0)
            {
                return BadRequest();
            }

            await _industryPartnerService.ArchivePartnerAsync(partnerId);
            return Redirect($"/projects/industry-partners/partner-detail?partnerId={partnerId}");
        }

        public async Task<IActionResult> OnPostReactivatePartnerAsync(int partnerId)
        {
            if (partnerId <= 0)
            {
                return BadRequest();
            }

            await _industryPartnerService.ReactivatePartnerAsync(partnerId);
            return Redirect($"/projects/industry-partners/partner-detail?partnerId={partnerId}");
        }

        public async Task<IActionResult> OnPostLinkProjectAsync(LinkProjectRequest request)
        {
            if (request.PartnerId <= 0)
            {
                return BadRequest();
            }

            if (request.ProjectId <= 0)
            {
                return await BuildLinkProjectDrawerErrorAsync(request, "Select a project before saving.");
            }

            if (string.IsNullOrWhiteSpace(request.Role))
            {
                return await BuildLinkProjectDrawerErrorAsync(request, "Select a role before saving.");
            }

            try
            {
                var linked = await _industryPartnerService.LinkProjectAsync(request);
                if (!linked)
                {
                    return await BuildLinkProjectDrawerErrorAsync(request, "Unable to link the project. Try again.");
                }
            }
            catch (IndustryPartnerInactiveException)
            {
                return await BuildLinkProjectDrawerErrorAsync(request, "Partner is inactive. Reactivate to create new associations.");
            }
            catch (DuplicateAssociationException)
            {
                return await BuildLinkProjectDrawerErrorAsync(request, "This partner is already linked to this project in the selected role.");
            }

            TempData["LinkProjectSuccess"] = true;
            return Redirect($"/projects/industry-partners/partner-detail?partnerId={request.PartnerId}");
        }

        public async Task<IActionResult> OnPostDeactivateAssociationAsync(int associationId, int partnerId)
        {
            if (associationId <= 0 || partnerId <= 0)
            {
                return BadRequest();
            }

            await _industryPartnerService.DeactivateAssociationAsync(associationId);
            return Redirect($"/projects/industry-partners/partner-detail?partnerId={partnerId}");
        }

        public async Task<IActionResult> OnPostCreatePartnerAsync()
        {
            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            var partnerId = await _industryPartnerService.CreatePartnerAsync(CreatePartnerRequest);
            if (partnerId <= 0)
            {
                ModelState.AddModelError(string.Empty, "Unable to create the industry partner.");
                await OnGetAsync();
                return Page();
            }

            return Redirect($"/projects/industry-partners?partner={partnerId}");
        }

        // Section: Project search handlers
        public async Task<IActionResult> OnGetProjectSearchPartialAsync(string q)
        {
            var term = (q ?? string.Empty).Trim();
            if (term.Length < 2)
            {
                ViewData["SearchTooShort"] = true;
                return Partial("Projects/IndustryPartners/_Partials/_ProjectSearchResults", Array.Empty<ProjectSearchItemViewModel>());
            }

            var items = await _industryPartnerService.SearchProjectsAsync(term, 20);
            return Partial("Projects/IndustryPartners/_Partials/_ProjectSearchResults", items);
        }

        public async Task<IActionResult> OnGetProjectSelectPartialAsync(int projectId)
        {
            var item = await _industryPartnerService.GetProjectSearchItemAsync(projectId);
            ViewData["ClearProjectResults"] = true;
            return Partial("Projects/IndustryPartners/_Partials/_ProjectSelectedChip", item);
        }

        public IActionResult OnGetProjectClearPartial()
        {
            ViewData["ClearProjectResults"] = true;
            return Partial("Projects/IndustryPartners/_Partials/_ProjectSelectedChip", null);
        }

        // Section: Overview editing
        public async Task<IActionResult> OnGetOverviewEditPartialAsync(int partnerId)
        {
            if (partnerId <= 0)
            {
                return BadRequest();
            }

            var partner = await _industryPartnerService.GetPartnerDetailAsync(partnerId);
            if (partner is null)
            {
                return NotFound();
            }

            var request = new UpdatePartnerOverviewRequest
            {
                PartnerId = partner.Id,
                DisplayName = partner.DisplayName,
                LegalName = partner.LegalName,
                PartnerType = partner.PartnerType,
                RegistrationNumber = partner.RegistrationNumber,
                Address = partner.Address,
                City = partner.City,
                State = partner.State,
                Country = partner.Country,
                Website = partner.Website,
                Email = partner.Email,
                Phone = partner.Phone
            };

            return Partial("Projects/IndustryPartners/_Partials/_PartnerOverviewEdit", request);
        }

        public async Task<IActionResult> OnGetOverviewReadPartialAsync(int partnerId)
        {
            if (partnerId <= 0)
            {
                return BadRequest();
            }

            var partner = await _industryPartnerService.GetPartnerDetailAsync(partnerId);
            if (partner is null)
            {
                return NotFound();
            }

            return Partial("Projects/IndustryPartners/_Partials/_PartnerOverviewReadBody", partner);
        }

        public async Task<IActionResult> OnPostUpdateOverviewAsync(UpdatePartnerOverviewRequest request)
        {
            if (!ModelState.IsValid)
            {
                return Partial("Projects/IndustryPartners/_Partials/_PartnerOverviewEdit", request);
            }

            var updated = await _industryPartnerService.UpdateOverviewAsync(request);
            if (!updated)
            {
                return NotFound();
            }

            var partner = await _industryPartnerService.GetPartnerDetailAsync(request.PartnerId);
            if (partner is null)
            {
                return NotFound();
            }

            return Partial("Projects/IndustryPartners/_Partials/_PartnerOverviewReadBody", partner);
        }

        // Section: Drawer defaults
        private async Task<LinkProjectDrawerViewModel> BuildLinkProjectDrawerAsync()
        {
            return await Task.FromResult(new LinkProjectDrawerViewModel());
        }

        // Section: Drawer error responses
        private async Task<PartialViewResult> BuildLinkProjectDrawerErrorAsync(LinkProjectRequest request, string message)
        {
            Response.Headers["HX-Retarget"] = "#linkProjectDrawerBody";
            Response.Headers["HX-Reswap"] = "innerHTML";

            var selectedProject = await _industryPartnerService.GetProjectSearchItemAsync(request.ProjectId);

            var viewModel = new LinkProjectDrawerViewModel
            {
                LinkProjectError = message,
                SelectedProject = selectedProject,
                SelectedRole = request.Role,
                Notes = request.Notes
            };

            return Partial("Projects/IndustryPartners/_Partials/_LinkProjectDrawerBody", viewModel);
        }
    }
}
