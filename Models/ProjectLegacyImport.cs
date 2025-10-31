using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public class ProjectLegacyImport
    {
        public long Id { get; set; }

        [Required]
        public int ProjectCategoryId { get; set; }

        [Required]
        public int TechnicalCategoryId { get; set; }

        public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(450)]
        public string ImportedByUserId { get; set; } = string.Empty;

        public int RowsReceived { get; set; }

        public int RowsImported { get; set; }

        [MaxLength(128)]
        public string? SourceFileHashSha256 { get; set; }

        public ProjectCategory? ProjectCategory { get; set; }

        public TechnicalCategory? TechnicalCategory { get; set; }
    }
}
