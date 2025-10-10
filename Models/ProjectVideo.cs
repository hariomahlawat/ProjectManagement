using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public class ProjectVideo
    {
        public int Id { get; set; }

        [Required]
        public int ProjectId { get; set; }

        public Project Project { get; set; } = null!;

        [Required]
        [MaxLength(260)]
        public string StorageKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(260)]
        public string OriginalFileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ContentType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public int? DurationSeconds { get; set; }

        [MaxLength(200)]
        public string? Title { get; set; }

        [MaxLength(512)]
        public string? Description { get; set; }

        public int Ordinal { get; set; }

        public bool IsFeatured { get; set; }

        public int? TotId { get; set; }

        public ProjectTot? Tot { get; set; }

        public int Version { get; set; } = 1;

        public DateTime CreatedUtc { get; set; }

        public DateTime UpdatedUtc { get; set; }

        [MaxLength(260)]
        public string? PosterStorageKey { get; set; }

        [MaxLength(128)]
        public string? PosterContentType { get; set; }
    }
}
