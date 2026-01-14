namespace ProjectManagement.ViewModels.Projects.IndustryPartners
{
    public sealed class ProjectSearchItemViewModel
    {
        // Section: Project identity
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string? ProjectCode { get; set; }
        public string? CategoryName { get; set; }
    }
}
