using System.Collections.Generic;

namespace ProjectManagement.ViewModels.Projects.IndustryPartners
{
    public class DirectoryListViewModel
    {
        // Section: Directory state
        public IReadOnlyList<PartnerListItemViewModel> Partners { get; set; } = new List<PartnerListItemViewModel>();
        public int? SelectedPartnerId { get; set; }
        public int TotalCount { get; set; }

        // Section: Filter state
        public string? Q { get; set; }
        public string? Type { get; set; }
        public string? Status { get; set; }
        public string? Sort { get; set; }
    }
}
