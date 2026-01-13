using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProjectManagement.ViewModels.Projects.IndustryPartners;

namespace ProjectManagement.Pages.Projects.IndustryPartners
{
    [Authorize]
    public class IndexModel : PageModel
    {
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

        public void OnGet()
        {
            var allPartners = IndustryPartnerSampleData.BuildSamplePartners();

            // Section: Directory filtering
            var filteredPartners = ApplyFilters(allPartners);

            // Section: Directory sorting
            filteredPartners = ApplySort(filteredPartners);

            Partners = filteredPartners.ToList();
            TotalCount = Partners.Count;

            // Section: Selected detail
            SelectedPartner = PartnerId.HasValue
                ? allPartners.FirstOrDefault(partner => partner.Id == PartnerId)
                : null;
        }

        // Section: Directory filtering helpers
        private IEnumerable<PartnerDetailViewModel> ApplyFilters(IEnumerable<PartnerDetailViewModel> partners)
        {
            var filteredPartners = partners;

            if (!string.IsNullOrWhiteSpace(Q))
            {
                filteredPartners = filteredPartners.Where(partner =>
                    partner.DisplayName.Contains(Q, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(partner.LegalName) &&
                     partner.LegalName.Contains(Q, StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrWhiteSpace(Type))
            {
                filteredPartners = filteredPartners.Where(partner =>
                    partner.PartnerType.Equals(Type, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(Status))
            {
                filteredPartners = filteredPartners.Where(partner =>
                    partner.Status.Equals(Status, StringComparison.OrdinalIgnoreCase));
            }

            return filteredPartners;
        }

        // Section: Directory sorting helpers
        private IEnumerable<PartnerDetailViewModel> ApplySort(IEnumerable<PartnerDetailViewModel> partners)
        {
            return Sort switch
            {
                "projects" => partners.OrderByDescending(partner => partner.ProjectCount).ThenBy(partner => partner.DisplayName),
                "updated" => partners.OrderBy(partner => partner.DisplayName),
                _ => partners.OrderBy(partner => partner.DisplayName)
            };
        }
    }
}
