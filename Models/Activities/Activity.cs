using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models;

namespace ProjectManagement.Models.Activities
{
    public class Activity
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [MaxLength(450)]
        public string? Location { get; set; }

        public DateTimeOffset? ScheduledStartUtc { get; set; }

        public DateTimeOffset? ScheduledEndUtc { get; set; }

        [Required]
        public int ActivityTypeId { get; set; }

        public ActivityType ActivityType { get; set; } = null!;

        public ApplicationUser? CreatedByUser { get; set; }

        public ApplicationUser? LastModifiedByUser { get; set; }

        [Required]
        [MaxLength(450)]
        public string CreatedByUserId { get; set; } = string.Empty;

        [MaxLength(450)]
        public string? LastModifiedByUserId { get; set; }

        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? LastModifiedAtUtc { get; set; }

        public bool IsDeleted { get; set; }

        [MaxLength(450)]
        public string? DeletedByUserId { get; set; }

        public ApplicationUser? DeletedByUser { get; set; }

        public DateTimeOffset? DeletedAtUtc { get; set; }

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public ICollection<ActivityAttachment> Attachments { get; set; } = new List<ActivityAttachment>();
    }

    public class ActivityAttachment
    {
        public int Id { get; set; }

        [Required]
        public int ActivityId { get; set; }

        public Activity Activity { get; set; } = null!;

        [Required]
        [MaxLength(260)]
        public string StorageKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(260)]
        public string OriginalFileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ContentType { get; set; } = string.Empty;

        [Range(0, long.MaxValue)]
        public long FileSize { get; set; }

        [Required]
        [MaxLength(450)]
        public string UploadedByUserId { get; set; } = string.Empty;

        public ApplicationUser? UploadedByUser { get; set; }

        public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }

    public class ActivityType
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(512)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        [Required]
        [MaxLength(450)]
        public string CreatedByUserId { get; set; } = string.Empty;

        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        [MaxLength(450)]
        public string? LastModifiedByUserId { get; set; }

        public DateTimeOffset? LastModifiedAtUtc { get; set; }

        public ApplicationUser? CreatedByUser { get; set; }

        public ApplicationUser? LastModifiedByUser { get; set; }

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public ICollection<Activity> Activities { get; set; } = new List<Activity>();
    }
}
