using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public enum EventCategory : byte
    {
        Training = 0,
        Holiday = 1,
        TownHall = 2,
        Hiring = 3,
        Other = 4
    }

    public class CalendarEvent
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

        [Required]
        public bool IsAllDay { get; set; }

        [Required]
        public string CreatedById { get; set; } = string.Empty;

        [Required]
        public string UpdatedById { get; set; } = string.Empty;

        [Required]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        public bool IsDeleted { get; set; } = false;
    }
}
