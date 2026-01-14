namespace ProjectManagement.ViewModels.Projects.IndustryPartners
{
    public class LinkProjectDrawerViewModel
    {
        // Section: Selected project
        public ProjectSearchItemViewModel? SelectedProject { get; set; }

        // Section: Form state
        public string? SelectedRole { get; set; }
        public string? Notes { get; set; }

        // Section: Validation
        public string? LinkProjectError { get; set; }
    }
}
