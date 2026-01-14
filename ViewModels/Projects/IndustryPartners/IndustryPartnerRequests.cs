using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.ViewModels.Projects.IndustryPartners
{
    public class PartnerSearchQuery
    {
        // Section: Query inputs
        public string? Query { get; set; }
        public string? Type { get; set; }
        public string? Status { get; set; }
        public string? Sort { get; set; }
    }

    public class LinkProjectRequest
    {
        // Section: Association inputs
        [Range(1, int.MaxValue)]
        public int PartnerId { get; set; }

        [Range(1, int.MaxValue)]
        public int ProjectId { get; set; }

        [Required]
        [MaxLength(120)]
        public string Role { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }
}
