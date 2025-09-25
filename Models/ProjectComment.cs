using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Models
{
    public enum ProjectCommentType
    {
        Update = 0,
        Risk = 1,
        Blocker = 2,
        Decision = 3,
        Info = 4
    }

    public class ProjectComment
    {
        public int Id { get; set; }

        [Required]
        public int ProjectId { get; set; }

        public int? ProjectStageId { get; set; }

        public int? ParentCommentId { get; set; }

        [Required]
        [MaxLength(2000)]
        [MinLength(4)]
        public string Body { get; set; } = string.Empty;

        [Required]
        public ProjectCommentType Type { get; set; }

        public bool Pinned { get; set; }

        public bool IsDeleted { get; set; }

        [Required]
        [MaxLength(450)]
        public string CreatedByUserId { get; set; } = string.Empty;

        public ApplicationUser? CreatedByUser { get; set; }

        public DateTime CreatedOn { get; set; }

        [MaxLength(450)]
        public string? EditedByUserId { get; set; }

        public ApplicationUser? EditedByUser { get; set; }

        public DateTime? EditedOn { get; set; }

        public Project? Project { get; set; }

        public ProjectStage? ProjectStage { get; set; }

        public ProjectComment? ParentComment { get; set; }

        public ICollection<ProjectComment> Replies { get; set; } = new List<ProjectComment>();

        public ICollection<ProjectCommentAttachment> Attachments { get; set; } = new List<ProjectCommentAttachment>();

        public ICollection<ProjectCommentMention> Mentions { get; set; } = new List<ProjectCommentMention>();
    }

    public class ProjectCommentAttachment
    {
        public int Id { get; set; }

        [Required]
        public int CommentId { get; set; }

        public ProjectComment Comment { get; set; } = null!;

        [Required]
        [MaxLength(260)]
        public string StoredFileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(260)]
        public string OriginalFileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ContentType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        [Required]
        [MaxLength(512)]
        public string StoragePath { get; set; } = string.Empty;

        [Required]
        [MaxLength(450)]
        public string UploadedByUserId { get; set; } = string.Empty;

        public ApplicationUser? UploadedByUser { get; set; }

        public DateTime UploadedOn { get; set; }
    }

    public class ProjectCommentMention
    {
        public int Id { get; set; }

        [Required]
        public int CommentId { get; set; }

        public ProjectComment Comment { get; set; } = null!;

        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }
    }
}
