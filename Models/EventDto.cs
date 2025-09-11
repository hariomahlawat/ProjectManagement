using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public class EventDto
    {
        public Guid? Id { get; set; }

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
    }
}
