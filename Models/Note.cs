using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public class Note
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string OwnerId { get; set; } = string.Empty;

        public Guid? TodoId { get; set; }
        public TodoItem? Todo { get; set; }

        [Required, StringLength(160)]
        public string Title { get; set; } = string.Empty;

        public string? Body { get; set; }

        public bool IsPinned { get; set; }

        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? DeletedUtc { get; set; }
    }
}
