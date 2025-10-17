using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public enum ProjectTotStatus
    {
        NotRequired = 0,
        NotStarted = 1,
        InProgress = 2,
        Completed = 3
    }

    public sealed class ProjectTot
    {
        public int Id { get; set; }

        [Required]
        public int ProjectId { get; set; }

        public Project Project { get; set; } = null!;

        [Required]
        public ProjectTotStatus Status { get; set; } = ProjectTotStatus.NotStarted;

        public DateOnly? StartedOn { get; set; }

        public DateOnly? CompletedOn { get; set; }

        [MaxLength(2000)]
        public string? MetDetails { get; set; }

        public DateOnly? MetCompletedOn { get; set; }

        public bool? FirstProductionModelManufactured { get; set; }

        public DateOnly? FirstProductionModelManufacturedOn { get; set; }

        [MaxLength(450)]
        public string? LastApprovedByUserId { get; set; }

        public ApplicationUser? LastApprovedByUser { get; set; }

        public DateTime? LastApprovedOnUtc { get; set; }
    }
}
