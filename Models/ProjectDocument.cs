using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Models
{
    public enum ProjectDocumentStatus
    {
        Published = 1,
        SoftDeleted = 2
    }

    public class ProjectDocument
    {
        public int Id { get; set; }

        [Required]
        public int ProjectId { get; set; }

        public Project Project { get; set; } = null!;

        public int? StageId { get; set; }

        public ProjectStage? Stage { get; set; }

        public int? RequestId { get; set; }

        public ProjectDocumentRequest? Request { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Description { get; set; }

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
        public ProjectDocumentStatus Status { get; set; } = ProjectDocumentStatus.Published;

        [Range(0, int.MaxValue)]
        public int FileStamp { get; set; }

        public int? TotId { get; set; }

        public ProjectTot? Tot { get; set; }

        [Required]
        [MaxLength(450)]
        public string UploadedByUserId { get; set; } = string.Empty;

        public ApplicationUser? UploadedByUser { get; set; }

        [Required]
        public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public bool IsArchived { get; set; }

        public DateTimeOffset? ArchivedAtUtc { get; set; }

        [MaxLength(450)]
        public string? ArchivedByUserId { get; set; }

        public ApplicationUser? ArchivedByUser { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}

