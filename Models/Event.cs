using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjectManagement.Models
{
    public class Event
    {
        [Key]
        public Guid Id { get; set; }

        [Required, StringLength(160)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        public EventCategory Category { get; set; }

        [StringLength(160)]
        public string? Location { get; set; }

        [Required]
        public DateTimeOffset StartUtc { get; set; }

        [Required]
        public DateTimeOffset EndUtc { get; set; }

        public bool IsAllDay { get; set; }

        [StringLength(256)]
        public string? RecurrenceRule { get; set; }

        public DateTimeOffset? RecurrenceUntilUtc { get; set; }

        public string? RecurrenceExDates { get; set; }

        [StringLength(450)]
        public string? CreatedById { get; set; }

        [StringLength(450)]
        public string? UpdatedById { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public bool IsDeleted { get; set; }
    }
}

