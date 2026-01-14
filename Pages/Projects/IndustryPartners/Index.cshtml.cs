using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.Configuration;
using ProjectManagement.Services.IndustryPartners;
using ProjectManagement.Services.IndustryPartners.Exceptions;
using ProjectManagement.ViewModels.Projects.IndustryPartners;

namespace ProjectManagement.Pages.Projects.IndustryPartners
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly IIndustryPartnerService _industryPartnerService;
        private readonly IAuthorizationService _authorizationService;

        public IndexModel(
            IIndustryPartnerService industryPartnerService,
            IAuthorizationService authorizationService)
        {
            _industryPartnerService = industryPartnerService;
            _authorizationService = authorizationService;
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
        public DirectoryListViewModel DirectoryVm { get; private set; } = new();

        // Section: Selected detail
        public PartnerDetailViewModel? SelectedPartner { get; private set; }

        // Section: Drawer view model
        public LinkProjectDrawerViewModel LinkProjectDrawer { get; private set; } = new();

        // Section: Permission flags
        public bool CanManagePartners { get; private set; }

        // Section: Create partner request
        [BindProperty]
        public CreatePartnerRequest CreatePartnerRequest { get; set; } = new();

        public async Task OnGetAsync()
        {
            CanManagePartners = await CanManagePartnersAsync();
            var query = new PartnerSearchQuery
            {
                Query = Q,
                Type = Type,
                Status = Status,
                Sort = Sort
            };

            var directoryResult = await _industryPartnerService.SearchPartnersAsync(query);
            DirectoryVm = new DirectoryListViewModel
            {
                Partners = directoryResult.Items,
                TotalCount = directoryResult.TotalCount,
                SelectedPartnerId = PartnerId,
                Q = Q,
                Type = Type,
                Status = Status,
                Sort = Sort
            };

            // Section: Selected detail
            SelectedPartner = PartnerId.HasValue
                ? await _industryPartnerService.GetPartnerDetailAsync(PartnerId.Value)
                : null;

            if (SelectedPartner is not null)
            {
                SelectedPartner.CanManage = CanManagePartners;
            }

            LinkProjectDrawer = await BuildLinkProjectDrawerAsync();
        }

        // Section: Directory list partial
        public async Task<IActionResult> OnGetDirectoryListAsync()
        {
            var query = new PartnerSearchQuery
            {
                Query = Q,
                Type = Type,
                Status = Status,
                Sort = Sort
            };

            var directoryResult = await _industryPartnerService.SearchPartnersAsync(query);
            var directoryViewModel = new DirectoryListViewModel
            {
                Partners = directoryResult.Items,
                TotalCount = directoryResult.TotalCount,
                SelectedPartnerId = PartnerId,
                Q = Q,
                Type = Type,
                Status = Status,
                Sort = Sort
            };

            return Partial("Projects/IndustryPartners/_Partials/_DirectoryList", directoryViewModel);
        }

        // Section: Partner commands
        [Authorize(Policy = IndustryPartnerPolicies.Manage)]
        public async Task<IActionResult> OnPostArchivePartnerAsync(int partnerId)
        {
            if (partnerId <= 0)
            {
                return BadRequest();
            }

            var archived = await _industryPartnerService.ArchivePartnerAsync(partnerId);
            if (!archived)
            {
                return BadRequest();
            }

            TempData["ArchivePartnerSuccess"] = true;
            return Redirect($"/projects/industry-partners/partner-detail?partnerId={partnerId}");
        }

        [Authorize(Policy = IndustryPartnerPolicies.Manage)]
        public async Task<IActionResult> OnPostReactivatePartnerAsync(int partnerId)
        {
            if (partnerId <= 0)
            {
                return BadRequest();
            }

            var reactivated = await _industryPartnerService.ReactivatePartnerAsync(partnerId);
            if (!reactivated)
            {
                return BadRequest();
            }

            TempData["ReactivatePartnerSuccess"] = true;
            return Redirect($"/projects/industry-partners/partner-detail?partnerId={partnerId}");
        }

        [Authorize(Policy = IndustryPartnerPolicies.Manage)]
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

            // Section: Role validation
            request.Role = IndustryPartnerAssociationRoles.Normalize(request.Role);
            if (!IndustryPartnerAssociationRoles.IsValid(request.Role))
            {
                return await BuildLinkProjectDrawerErrorAsync(request, "Select a valid role before saving.");
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

        [Authorize(Policy = IndustryPartnerPolicies.Manage)]
        public async Task<IActionResult> OnPostDeactivateAssociationAsync(int associationId, int partnerId)
        {
            if (associationId <= 0 || partnerId <= 0)
            {
                return BadRequest();
            }

            var deactivated = await _industryPartnerService.DeactivateAssociationAsync(associationId);
            if (!deactivated)
            {
                return BadRequest();
            }

            return Redirect($"/projects/industry-partners/partner-detail?partnerId={partnerId}");
        }

        [Authorize(Policy = IndustryPartnerPolicies.Manage)]
        public async Task<IActionResult> OnPostCreatePartnerAsync(CreatePartnerRequest request)
        {
            // Section: Request hydration
            CreatePartnerRequest = request;

            if (!ModelState.IsValid)
            {
                TempData["CreatePartnerFailed"] = true;
                CanManagePartners = await CanManagePartnersAsync();
                await OnGetAsync();
                return Page();
            }

            var partnerId = await _industryPartnerService.CreatePartnerAsync(request);
            if (partnerId <= 0)
            {
                ModelState.AddModelError(string.Empty, "Unable to create the industry partner.");
                TempData["CreatePartnerFailed"] = true;
                CanManagePartners = await CanManagePartnersAsync();
                await OnGetAsync();
                return Page();
            }

            return Redirect($"/projects/industry-partners?partner={partnerId}");
        }

        // Section: Project search handlers
        [Authorize(Policy = IndustryPartnerPolicies.Manage)]
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

        [Authorize(Policy = IndustryPartnerPolicies.Manage)]
        public async Task<IActionResult> OnGetProjectSelectPartialAsync(int projectId)
        {
            var item = await _industryPartnerService.GetProjectSearchItemAsync(projectId);
            ViewData["ClearProjectResults"] = true;
            return Partial("Projects/IndustryPartners/_Partials/_ProjectSelectedChip", item);
        }

        [Authorize(Policy = IndustryPartnerPolicies.Manage)]
        public IActionResult OnGetProjectClearPartial()
        {
            ViewData["ClearProjectResults"] = true;
            return Partial("Projects/IndustryPartners/_Partials/_ProjectSelectedChip", null);
        }

        // Section: Overview editing
        [Authorize(Policy = IndustryPartnerPolicies.Manage)]
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
                RowVersion = Convert.ToBase64String(partner.RowVersion),
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

        [Authorize(Policy = IndustryPartnerPolicies.Manage)]
        public async Task<IActionResult> OnPostUpdateOverviewAsync(UpdatePartnerOverviewRequest request)
        {
            if (!ModelState.IsValid)
            {
                return Partial("Projects/IndustryPartners/_Partials/_PartnerOverviewEdit", request);
            }

            var result = await _industryPartnerService.UpdateOverviewAsync(request);
            if (result.Conflict)
            {
                ModelState.AddModelError(string.Empty, "This record was modified by another user. Reload to continue.");
                return Partial("Projects/IndustryPartners/_Partials/_PartnerOverviewEdit", request);
            }

            if (result.NotFound)
            {
                return NotFound();
            }

            var partner = await _industryPartnerService.GetPartnerDetailAsync(request.PartnerId);
            if (partner is null)
            {
                return NotFound();
            }

            Response.Headers["HX-Trigger"] = "industry-partner-overview-saved";
            return Partial("Projects/IndustryPartners/_Partials/_PartnerOverviewReadBody", partner);
        }

        // Section: Drawer defaults
        private async Task<LinkProjectDrawerViewModel> BuildLinkProjectDrawerAsync()
        {
            return await Task.FromResult(new LinkProjectDrawerViewModel());
        }

        // Section: Permission helpers
        private async Task<bool> CanManagePartnersAsync()
        {
            var result = await _authorizationService.AuthorizeAsync(User, IndustryPartnerPolicies.Manage);
            return result.Succeeded;
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
