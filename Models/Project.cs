using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
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

        public ProjectLifecycleStatus LifecycleStatus { get; set; } = ProjectLifecycleStatus.Active;

        public bool IsLegacy { get; set; }

        public DateOnly? CompletedOn { get; set; }

        public int? CompletedYear { get; set; }

        public DateOnly? CancelledOn { get; set; }

        [MaxLength(512)]
        public string? CancelReason { get; set; }

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

        public bool IsArchived { get; set; }

        public DateTimeOffset? ArchivedAt { get; set; }

        [MaxLength(450)]
        public string? ArchivedByUserId { get; set; }

        public bool IsDeleted { get; set; }

        public DateTimeOffset? DeletedAt { get; set; }

        [MaxLength(450)]
        public string? DeletedByUserId { get; set; }

        [MaxLength(512)]
        public string? DeleteReason { get; set; }

        [MaxLength(32)]
        public string? DeleteMethod { get; set; }

        [MaxLength(450)]
        public string? DeleteApprovedByUserId { get; set; }

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
        private ICollection<ProjectPhoto> _photos = new List<ProjectPhoto>();
        private ICollection<ProjectVideo> _videos = new List<ProjectVideo>();

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

        public ICollection<ProjectPhoto> Photos
        {
            get => _photos;
            set => _photos = value ?? new List<ProjectPhoto>();
        }

        public ICollection<ProjectVideo> Videos
        {
            get => _videos;
            set => _videos = value ?? new List<ProjectVideo>();
        }

        public int? CoverPhotoId { get; set; }

        public int CoverPhotoVersion { get; set; } = 1;

        public int? FeaturedVideoId { get; set; }

        public int FeaturedVideoVersion { get; set; } = 1;

        public ProjectTot? Tot { get; set; }

        [NotMapped]
        public ProjectPhoto? CoverPhoto => CoverPhotoId.HasValue
            ? Photos.FirstOrDefault(p => p.Id == CoverPhotoId.Value)
            : null;

        [NotMapped]
        public ProjectVideo? FeaturedVideo => FeaturedVideoId.HasValue
            ? Videos.FirstOrDefault(v => v.Id == FeaturedVideoId.Value)
            : null;

        [NotMapped]
        public int? CurrentCoverPhotoVersion => CoverPhoto?.Version ?? (CoverPhotoId.HasValue ? CoverPhotoVersion : null);

        [NotMapped]
        public int? CurrentFeaturedVideoVersion => FeaturedVideo?.Version ?? (FeaturedVideoId.HasValue ? FeaturedVideoVersion : null);
    }
}
