using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    public enum TodoPriority : byte
    {
        Low = 0,
        Normal = 1,
        High = 2
    }

    public enum TodoStatus : byte
    {
        Open = 0,
        Done = 1
    }

    public class TodoItem
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string OwnerId { get; set; } = string.Empty;

        [Required, StringLength(160)]
        public string Title { get; set; } = string.Empty;

        public DateTimeOffset? DueAtUtc { get; set; }

        [Required]
        public TodoPriority Priority { get; set; } = TodoPriority.Normal;

        [Required]
        public bool IsPinned { get; set; }

        [Required]
        public TodoStatus Status { get; set; } = TodoStatus.Open;

        [Required]
        public int OrderIndex { get; set; }

        [Required]
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? CompletedUtc { get; set; }

        public DateTimeOffset? DeletedUtc { get; set; }
    }
}

