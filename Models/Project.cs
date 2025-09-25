using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public class Project
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int? ActivePlanVersionNo { get; set; }

        // Assignments
        public string? HodUserId { get; set; }
        public ApplicationUser? HodUser { get; set; }

        public string? LeadPoUserId { get; set; }
        public ApplicationUser? LeadPoUser { get; set; }
    }
}
