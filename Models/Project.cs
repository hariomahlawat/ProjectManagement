using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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
        public string? CaseFileNumber { get; set; }

        [Required]
        [MaxLength(64)]
        public string CreatedByUserId { get; set; } = string.Empty;

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public int? ActivePlanVersionNo { get; set; }

        public int? CategoryId { get; set; }
        public ProjectCategory? Category { get; set; }

        public int? SponsoringUnitId { get; set; }
        public SponsoringUnit? SponsoringUnit { get; set; }

        public int? SponsoringLineDirectorateId { get; set; }
        public LineDirectorate? SponsoringLineDirectorate { get; set; }

        // Assignments
        public string? HodUserId { get; set; }
        public ApplicationUser? HodUser { get; set; }

        public string? LeadPoUserId { get; set; }
        public ApplicationUser? LeadPoUser { get; set; }

        public DateTimeOffset? PlanApprovedAt { get; set; }
        public string? PlanApprovedByUserId { get; set; }
        public ApplicationUser? PlanApprovedByUser { get; set; }

        private ICollection<ProjectStage> _projectStages = new List<ProjectStage>();

        public ICollection<ProjectStage> ProjectStages
        {
            get => _projectStages;
            set => _projectStages = value ?? new List<ProjectStage>();
        }

        [NotMapped]
        [Obsolete("Use ProjectStages instead.")]
        public ICollection<ProjectStage> Stages
        {
            get => ProjectStages;
            set => ProjectStages = value;
        }
    }
}
