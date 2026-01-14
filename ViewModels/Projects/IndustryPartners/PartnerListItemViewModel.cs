namespace ProjectManagement.ViewModels.Projects.IndustryPartners
{
    public sealed class PartnerListItemViewModel
    {
        // Section: Identity
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string PartnerType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        // Section: Directory summary
        public string? LocationSummary { get; set; }
        public int ActiveProjectCount { get; set; }
    }
}
