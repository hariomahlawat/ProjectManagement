using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Services.IndustryPartners;
using ProjectManagement.ViewModels.Projects.IndustryPartners;

namespace ProjectManagement.Pages.Projects.IndustryPartners
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IIndustryPartnerService _industryPartnerService;

        public IndexModel(ApplicationDbContext dbContext, IIndustryPartnerService industryPartnerService)
        {
            _dbContext = dbContext;
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
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            await _industryPartnerService.LinkProjectAsync(request);
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

        // Section: Drawer helpers
        private async Task<LinkProjectDrawerViewModel> BuildLinkProjectDrawerAsync()
        {
            var projects = await _dbContext.Projects
                .AsNoTracking()
                .Where(project => !project.IsDeleted)
                .OrderBy(project => project.Name)
                .Select(project => new
                {
                    project.Id,
                    project.Name,
                    project.CaseFileNumber
                })
                .ToListAsync();

            var options = projects.Select(project => new ProjectOptionViewModel
            {
                Id = project.Id,
                DisplayName = string.IsNullOrWhiteSpace(project.CaseFileNumber)
                    ? project.Name
                    : $"{project.Name} ({project.CaseFileNumber})"
            }).ToList();

            return new LinkProjectDrawerViewModel
            {
                Projects = options
            };
        }
    }
}
