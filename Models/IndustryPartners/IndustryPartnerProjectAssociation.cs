using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.IndustryPartners
{
    public class IndustryPartnerProjectAssociation
    {
        // Section: Identity
        public int Id { get; set; }

        public int IndustryPartnerId { get; set; }
        public IndustryPartner? IndustryPartner { get; set; }

        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        // Section: Association detail
        [MaxLength(1000)]
        public string? Notes { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime LinkedOnUtc { get; set; } = DateTime.UtcNow;
        public DateTime? DeactivatedUtc { get; set; }
    }
}
