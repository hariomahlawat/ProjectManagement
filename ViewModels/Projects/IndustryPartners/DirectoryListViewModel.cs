using System;
using System.Collections.Generic;

namespace ProjectManagement.ViewModels.Projects.IndustryPartners
{
    public class DirectoryListViewModel
    {
        // Section: Directory state
        public IReadOnlyList<PartnerDetailViewModel> Partners { get; set; } = Array.Empty<PartnerDetailViewModel>();
        public int? SelectedPartnerId { get; set; }
        public int TotalCount { get; set; }
    }
}
