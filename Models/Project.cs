using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models.Execution;

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

        [MaxLength(64)]
        public string? ProjectNumber { get; set; }

        [Required]
        [MaxLength(64)]
        public string CreatedByUserId { get; set; } = string.Empty;

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public int? ActivePlanVersionNo { get; set; }

        public int? CategoryId { get; set; }
        public ProjectCategory? Category { get; set; }

        // Assignments
        public string? HodUserId { get; set; }
        public ApplicationUser? HodUser { get; set; }

        public string? LeadPoUserId { get; set; }
        public ApplicationUser? LeadPoUser { get; set; }

        public ICollection<ProjectStage> Stages { get; set; } = new List<ProjectStage>();
    }
}
