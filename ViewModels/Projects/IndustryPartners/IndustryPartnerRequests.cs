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

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }

    public class UpdatePartnerOverviewRequest
    {
        // Section: Identity
        [Range(1, int.MaxValue)]
        public int PartnerId { get; set; }

        // Section: Concurrency
        public string? RowVersion { get; set; }

        [Required]
        [Display(Name = "Display Name")]
        [MaxLength(200)]
        public string DisplayName { get; set; } = string.Empty;

        [Display(Name = "Legal Name")]
        [MaxLength(200)]
        public string? LegalName { get; set; }

        [Required]
        [Display(Name = "Partner Type")]
        [MaxLength(120)]
        public string PartnerType { get; set; } = string.Empty;

        // Section: Registration
        [Display(Name = "Registration Number")]
        [MaxLength(100)]
        public string? RegistrationNumber { get; set; }

        // Section: Location
        [Display(Name = "Address")]
        [MaxLength(256)]
        public string? Address { get; set; }

        [Display(Name = "City")]
        [MaxLength(120)]
        public string? City { get; set; }

        [Display(Name = "State")]
        [MaxLength(120)]
        public string? State { get; set; }

        [Display(Name = "Country")]
        [MaxLength(120)]
        public string? Country { get; set; }

        // Section: Contact
        [Display(Name = "Website")]
        [MaxLength(256)]
        public string? Website { get; set; }

        [Display(Name = "Email")]
        [MaxLength(256)]
        [EmailAddress]
        public string? Email { get; set; }

        [Display(Name = "Phone")]
        [MaxLength(50)]
        public string? Phone { get; set; }
    }

    public class CreatePartnerRequest
    {
        // Section: Identity
        [Required]
        [Display(Name = "Display Name")]
        [MaxLength(200)]
        public string DisplayName { get; set; } = string.Empty;

        [Display(Name = "Legal Name")]
        [MaxLength(200)]
        public string? LegalName { get; set; }

        [Required]
        [Display(Name = "Partner Type")]
        [MaxLength(120)]
        public string PartnerType { get; set; } = string.Empty;

        // Section: Registration
        [Display(Name = "Registration Number")]
        [MaxLength(100)]
        public string? RegistrationNumber { get; set; }

        // Section: Location
        [Display(Name = "Address")]
        [MaxLength(256)]
        public string? Address { get; set; }

        [Display(Name = "City")]
        [MaxLength(120)]
        public string? City { get; set; }

        [Display(Name = "State")]
        [MaxLength(120)]
        public string? State { get; set; }

        [Display(Name = "Country")]
        [MaxLength(120)]
        public string? Country { get; set; }

        // Section: Contact
        [Display(Name = "Website")]
        [MaxLength(256)]
        public string? Website { get; set; }

        [Display(Name = "Email")]
        [MaxLength(256)]
        [EmailAddress]
        public string? Email { get; set; }

        [Display(Name = "Phone")]
        [MaxLength(50)]
        public string? Phone { get; set; }
    }
}
