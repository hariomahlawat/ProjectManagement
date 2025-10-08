using System;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Models
{
    public enum ProjectDocumentRequestStatus
    {
        Draft = 0,
        Submitted = 1,
        Approved = 2,
        Rejected = 3,
        Cancelled = 4
    }

    public enum ProjectDocumentRequestType
    {
        Upload = 1,
        Replace = 2,
        Delete = 3
    }

    public class ProjectDocumentRequest
    {
        public int Id { get; set; }

        [Required]
        public int ProjectId { get; set; }

        public Project Project { get; set; } = null!;

        public int? StageId { get; set; }

        public ProjectStage? Stage { get; set; }

        public int? DocumentId { get; set; }

        public ProjectDocument? Document { get; set; }

        public int? TotId { get; set; }

        public ProjectTot? Tot { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Required]
        public ProjectDocumentRequestStatus Status { get; set; } = ProjectDocumentRequestStatus.Draft;

        [Required]
        public ProjectDocumentRequestType RequestType { get; set; } = ProjectDocumentRequestType.Upload;

        [MaxLength(260)]
        public string? TempStorageKey { get; set; }

        [MaxLength(260)]
        public string? OriginalFileName { get; set; }

        [MaxLength(128)]
        public string? ContentType { get; set; }

        [Range(0, long.MaxValue)]
        public long? FileSize { get; set; }

        [Required]
        [MaxLength(450)]
        public string RequestedByUserId { get; set; } = string.Empty;

        public ApplicationUser? RequestedByUser { get; set; }

        [Required]
        public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        [MaxLength(450)]
        public string? ReviewedByUserId { get; set; }

        public ApplicationUser? ReviewedByUser { get; set; }

        public DateTimeOffset? ReviewedAtUtc { get; set; }

        [MaxLength(2000)]
        public string? ReviewerNote { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}

