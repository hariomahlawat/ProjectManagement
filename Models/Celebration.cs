using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public enum CelebrationType : byte
    {
        Birthday = 0,
        Anniversary = 1
    }

    public class Celebration
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public CelebrationType EventType { get; set; }

        [Required, StringLength(120)]
        public string Name { get; set; } = string.Empty;

        [StringLength(120)]
        [Display(Name = "Spouse Name (Optional)")]
        public string? SpouseName { get; set; }

        [Range(1,31)]
        public byte Day { get; set; }

        [Range(1,12)]
        public byte Month { get; set; }

        public short? Year { get; set; }

        [Required]
        public string CreatedById { get; set; } = string.Empty;

        [Required]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? DeletedUtc { get; set; }
    }
}
