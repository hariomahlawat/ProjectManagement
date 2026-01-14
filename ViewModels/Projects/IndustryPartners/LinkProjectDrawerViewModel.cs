using System;
using System.Collections.Generic;

namespace ProjectManagement.ViewModels.Projects.IndustryPartners
{
    public class LinkProjectDrawerViewModel
    {
        // Section: Project options
        public IReadOnlyList<ProjectOptionViewModel> Projects { get; set; } = Array.Empty<ProjectOptionViewModel>();
    }

    public class ProjectOptionViewModel
    {
        // Section: Project identity
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }
}
