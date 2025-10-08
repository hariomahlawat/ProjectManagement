using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public class ProjectPhoto
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

        public int Width { get; set; }

        public int Height { get; set; }

        public int Ordinal { get; set; }

        [MaxLength(512)]
        public string? Caption { get; set; }

        public int? TotId { get; set; }

        public ProjectTot? Tot { get; set; }

        public bool IsCover { get; set; }

        public int Version { get; set; } = 1;

        public DateTime CreatedUtc { get; set; }

        public DateTime UpdatedUtc { get; set; }
    }
}
