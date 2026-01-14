using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models.IndustryPartners
{
    public class IndustryPartner
    {
        // Section: Identity
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string DisplayName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? LegalName { get; set; }

        [Required]
        [MaxLength(120)]
        public string PartnerType { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        // Section: Registration
        [MaxLength(100)]
        public string? RegistrationNumber { get; set; }

        // Section: Location
        [MaxLength(256)]
        public string? Address { get; set; }

        [MaxLength(120)]
        public string? City { get; set; }

        [MaxLength(120)]
        public string? State { get; set; }

        [MaxLength(120)]
        public string? Country { get; set; }

        // Section: Contact
        [MaxLength(256)]
        public string? Website { get; set; }

        [MaxLength(256)]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }

        // Section: Audit
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        // Section: Navigation
        public ICollection<IndustryPartnerProjectAssociation> ProjectAssociations { get; set; } = new List<IndustryPartnerProjectAssociation>();
    }
}
