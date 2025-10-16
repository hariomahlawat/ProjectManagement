using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public sealed class ProjectTotRequest
    {
        public int Id { get; set; }

        [Required]
        public int ProjectId { get; set; }

        public Project Project { get; set; } = null!;

        [Required]
        public ProjectTotStatus ProposedStatus { get; set; }

        public DateOnly? ProposedStartedOn { get; set; }

        public DateOnly? ProposedCompletedOn { get; set; }

        [MaxLength(2000)]
        public string? ProposedMetDetails { get; set; }

        public DateOnly? ProposedMetCompletedOn { get; set; }

        public bool? ProposedFirstProductionModelManufactured { get; set; }

        public DateOnly? ProposedFirstProductionModelManufacturedOn { get; set; }

        [MaxLength(2000)]
        public string? ProposedRemarks { get; set; }

        [Required]
        [MaxLength(450)]
        public string SubmittedByUserId { get; set; } = string.Empty;

        public ApplicationUser SubmittedByUser { get; set; } = null!;

        public DateTime SubmittedOnUtc { get; set; }

        [Required]
        public ProjectTotRequestDecisionState DecisionState { get; set; } = ProjectTotRequestDecisionState.Pending;

        [MaxLength(450)]
        public string? DecidedByUserId { get; set; }

        public ApplicationUser? DecidedByUser { get; set; }

        public DateTime? DecidedOnUtc { get; set; }

        [MaxLength(2000)]
        public string? DecisionRemarks { get; set; }

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
