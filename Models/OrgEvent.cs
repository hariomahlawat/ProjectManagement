using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public enum EventCategory : byte
    {
        Visit = 0,
        Inspection = 1,
        Conference = 2,
        Training = 3,
        Holiday = 4,
        Other = 5
    }

    public class OrgEvent
    {
        [Key] public Guid Id { get; set; }

        [Required, MaxLength(160)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; } // markdown

        [Required]
        public EventCategory Category { get; set; } = EventCategory.Other;

        [MaxLength(160)]
        public string? Location { get; set; }

        [Required]
        public DateTimeOffset StartUtc { get; set; }

        [Required]
        public DateTimeOffset EndUtc { get; set; } // exclusive end

        [Required]
        public bool IsAllDay { get; set; }

        [Required] public string CreatedById { get; set; } = string.Empty;
        public string? UpdatedById { get; set; }
        [Required] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        [Required] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        public bool IsDeleted { get; set; } = false;
    }
}

